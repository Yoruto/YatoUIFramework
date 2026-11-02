using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Yato.App;
using Yato.Config;
using Yato.Core;
using Yato.Pooling;
using Yato.Resources;
using Yato.Survivor.Domain;

namespace Yato.Survivor
{
    public sealed class SurvivorRunSession : IDisposable, IDiagnosticSource
    {
        private readonly ResourceScope m_scope = new ResourceScope("survivor/run");
        private readonly List<SurvivorEntity> m_enemies = new List<SurvivorEntity>(256);
        private readonly List<SurvivorEntity> m_bullets = new List<SurvivorEntity>(160);
        private readonly List<SurvivorEntity> m_pickups = new List<SurvivorEntity>(400);
        private readonly List<SurvivorEntity> m_effects = new List<SurvivorEntity>(96);
        private readonly Queue<PendingDrop> m_pendingDrops = new Queue<PendingDrop>();
        private readonly List<UpgradeOption> m_upgradeOptions = new List<UpgradeOption>();
        private readonly float[] m_cooldowns = new float[3];
        private readonly string[] m_weapons = { "sword", "bow", "staff" };
        private readonly System.Random m_random;
        private readonly ConfigService m_config;
        private readonly SurvivorSpriteSet m_art;
        private readonly PauseService m_pause;
        private readonly Action<string> m_sound;
        private ResourceInstancePool m_enemyPool, m_bulletPool, m_pickupPool, m_effectPool;
        private SurvivorEntity m_player;
        private Camera m_camera;
        private float m_spawnTimer, m_hurtCooldown, m_nextElite = 45;
        private int m_treasureChoices;
        private bool m_disposed;
        private readonly IDisposable m_diagnostics;
        public readonly string RunId = Guid.NewGuid().ToString("N");
        public StageRecord Stage { get; }
        public CharacterRecord Character { get; }
        public RewardRecord Rewards { get; }
        public RunProgress Progress { get; }
        public float Health { get; private set; }
        public float MaximumHealth => Character.Health + Progress.Rank("vitality") * 20;
        public float MoveSpeed => Character.Speed * (1 + Progress.Rank("swift") * .12f);
        public float PickupRadius => Character.PickupRadius + Progress.Rank("magnet") * .65f;
        public bool Stopped { get; private set; }
        public bool ChoicePending => Progress.PendingLevels > 0 || m_treasureChoices > 0;
        public bool TreasureChoice => m_treasureChoices > 0 && Progress.PendingLevels == 0;
        public int EnemyCount => m_enemies.Count;
        public int ProjectileCount => m_bullets.Count;
        public int PickupCount => m_pickups.Count;
        public int PoolOverflows { get; private set; }
        public Vector2 PlayerPosition => m_player != null ? (Vector2)m_player.transform.position : Vector2.zero;
        public event Action<RunSummary> Finished;

        private SurvivorRunSession(ConfigService config, SurvivorSpriteSet art, PauseService pause, Action<string> sound, int seed)
        {
            m_config = config; m_art = art; m_pause = pause; m_sound = sound; m_random = new System.Random(seed);
            Stage = config.Get<StageRecord>("dungeon"); Character = config.Get<CharacterRecord>("knight");
            Rewards = config.Get<RewardRecord>("standard");
            m_nextElite = Stage.EliteInterval;
            Progress = new RunProgress(Rewards.BaseExperience, Rewards.ExperienceGrowth);
            Progress.Upgrade(Character.InitialWeapon, 5); Health = MaximumHealth;
            foreach (var id in Stage.Upgrades)
            {
                var row = config.Get<UpgradeRecord>(id);
                m_upgradeOptions.Add(new UpgradeOption(id, row.Maximum, row.Weight));
            }
            m_diagnostics = DiagnosticRegistry.Register(this);
        }

        public static async Task<SurvivorRunSession> CreateAsync(IResourceService resources, ConfigService config,
            SurvivorSpriteSet art, PauseService pause, SceneContext scene, Action<string> sound, CancellationToken ct, int seed = 0)
        {
            var session = new SurvivorRunSession(config, art, pause, sound, seed == 0 ? Environment.TickCount : seed);
            try
            {
                Transform root = null;
                foreach (var go in scene.Scene.GetRootGameObjects())
                {
                    if (go.GetComponent<Grid>() != null) root = go.transform;
                    var camera = go.GetComponentInChildren<Camera>();
                    if (camera != null) session.m_camera = camera;
                }
                if (root == null || session.m_camera == null) throw new InvalidOperationException("Arena requires ArenaRoot and camera");
                var player = await resources.InstantiateAsync(new AssetKey(art.PlayerKey), root, ct);
                try { ct.ThrowIfCancellationRequested(); session.m_scope.Track(player); }
                catch { player.Dispose(); throw; }
                session.m_player = player.Asset.GetComponent<SurvivorEntity>();
                session.m_player.OnRent(); session.m_player.Initialize(Vector2.zero, art.Find("knight_idle"));
                session.m_enemyPool = await session.CreatePool(resources, art.EnemyKey, session.Stage.EnemyCapacity, root, ct);
                session.m_bulletPool = await session.CreatePool(resources, art.ProjectileKey, session.Stage.ProjectileCapacity, root, ct);
                session.m_pickupPool = await session.CreatePool(resources, art.PickupKey, session.Stage.PickupCapacity, root, ct);
                session.m_effectPool = await session.CreatePool(resources, art.EffectKey, session.Stage.EffectCapacity, root, ct);
                session.m_camera.transform.position = new Vector3(0, 0, -10);
                ct.ThrowIfCancellationRequested();
                return session;
            }
            catch { session.Dispose(); throw; }
        }
        private async Task<ResourceInstancePool> CreatePool(IResourceService resources, string key, int capacity, Transform parent, CancellationToken ct)
        {
            var pool = await ResourceInstancePool.CreateAsync(resources, new AssetKey(key), capacity, parent, ct);
            try { ct.ThrowIfCancellationRequested(); m_scope.Track(pool); pool.Prewarm(Math.Min(capacity, 100)); return pool; }
            catch { pool.Dispose(); throw; }
        }
        public DiagnosticSnapshot CaptureDiagnostics() => new DiagnosticSnapshot("Survivor", RunId, Stopped ? "Stopped" : "Running", values:
            new Dictionary<string, string> { ["Enemies"] = EnemyCount.ToString(), ["Projectiles"] = ProjectileCount.ToString(),
                ["Pickups"] = PickupCount.ToString(), ["Pending rewards"] = m_pendingDrops.Count.ToString(), ["Pool full"] = PoolOverflows.ToString() });

        public void Tick(float delta, Vector2 movement)
        {
            if (Stopped || m_disposed || m_pause.IsPaused || !(delta > 0)) return;
            delta = Mathf.Min(delta, .05f);
            Progress.Tick(delta); m_hurtCooldown -= delta;
            var position = PlayerPosition + Vector2.ClampMagnitude(movement, 1) * MoveSpeed * delta;
            position.x = Mathf.Clamp(position.x, -Stage.HalfWidth, Stage.HalfWidth);
            position.y = Mathf.Clamp(position.y, -Stage.HalfHeight, Stage.HalfHeight);
            m_player.transform.position = position;
            if (movement.x != 0) m_player.Renderer.flipX = movement.x < 0;
            m_player.SetSequence(m_art.Find(movement.sqrMagnitude > .01f ? "knight_run" : "knight_idle"));
            m_player.Animate(delta);
            m_camera.transform.position = Vector3.Lerp(m_camera.transform.position,
                new Vector3(Mathf.Clamp(position.x, -8, 8), Mathf.Clamp(position.y, -7, 7), -10), Mathf.Min(1, delta * 6));

            m_spawnTimer -= delta;
            if (m_spawnTimer <= 0)
            {
                foreach (var id in Stage.Waves)
                {
                    var wave = m_config.Get<SpawnWaveRecord>(id);
                    if (Progress.Elapsed < wave.StartSeconds || Progress.Elapsed >= wave.EndSeconds) continue;
                    m_spawnTimer += wave.Interval;
                    if (m_enemies.Count < wave.MaximumAlive) SpawnEnemy(false, wave.Enemies);
                    break;
                }
            }
            if (Progress.Elapsed >= m_nextElite) { m_nextElite += Stage.EliteInterval; SpawnEnemy(true, Stage.Enemies); }
            UpdateEnemies(delta);
            if (Health <= 0) { End(false, "The dungeon claimed another soul."); return; }
            UpdateWeapons(delta); UpdateBullets(delta); UpdatePickups(delta); UpdateEffects(delta);
            for (var i = 0; i < 8 && m_pendingDrops.Count > 0; i++)
            {
                var pending = m_pendingDrops.Peek();
                if (!TryDrop(pending)) break;
                m_pendingDrops.Dequeue();
            }
            if (Progress.Elapsed >= Stage.Duration) End(true, "You held the line until dawn.");
        }
        private void SpawnEnemy(bool elite, string[] ids)
        {
            EnemyRecord enemy = null;
            var available = 0;
            foreach (var id in ids)
            {
                var candidate = m_config.Get<EnemyRecord>(id);
                if (candidate.UnlockSeconds <= Progress.Elapsed && candidate.Elite == elite && m_random.Next(++available) == 0) enemy = candidate;
            }
            if (enemy == null) return;
            if (!m_enemyPool.TryRent(out var lease)) { PoolOverflows++; return; }
            var entity = lease.GameObject.GetComponent<SurvivorEntity>();
            var pos = PlayerPosition;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var angle = (float)m_random.NextDouble() * Mathf.PI * 2;
                pos = PlayerPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 12;
                pos.x = Mathf.Clamp(pos.x, -Stage.HalfWidth, Stage.HalfWidth); pos.y = Mathf.Clamp(pos.y, -Stage.HalfHeight, Stage.HalfHeight);
                if ((pos - PlayerPosition).sqrMagnitude >= 36) break;
                if (attempt == 15) pos = new Vector2(PlayerPosition.x >= 0 ? -Stage.HalfWidth : Stage.HalfWidth,
                    PlayerPosition.y >= 0 ? -Stage.HalfHeight : Stage.HalfHeight);
            }
            entity.Initialize(pos, m_art.Find(enemy.Visual), enemy.Scale, elite ? new Color(1, .65f, .65f) : Color.white);
            entity.Lease = lease; entity.Enemy = enemy;
            entity.Health = enemy.Health * (1 + Progress.Elapsed / 180); entity.Kind = 1;
            m_enemies.Add(entity);
        }
        private void UpdateEnemies(float delta)
        {
            foreach (var entity in m_enemies)
            {
                var offset = PlayerPosition - (Vector2)entity.transform.position;
                if (offset.sqrMagnitude > .22f) entity.transform.position += (Vector3)(offset.normalized * entity.Enemy.Speed * delta);
                entity.Renderer.flipX = offset.x < 0; entity.Animate(delta);
                if (offset.sqrMagnitude < .6f && m_hurtCooldown <= 0)
                {
                    var armor = Mathf.Min(.5f, Progress.Rank("ward") * .08f);
                    Health = Mathf.Max(0, Health - entity.Enemy.Damage * (1 - armor));
                    m_hurtCooldown = .65f; m_player.Flash = .18f; m_sound?.Invoke("hit");
                }
            }
        }
        private SurvivorEntity Nearest(Vector2 origin, float range)
        {
            SurvivorEntity result = null; var closest = range * range;
            foreach (var enemy in m_enemies)
            {
                var square = ((Vector2)enemy.transform.position - origin).sqrMagnitude;
                if (square < closest) { closest = square; result = enemy; }
            }
            return result;
        }
        private void UpdateWeapons(float delta)
        {
            for (var i = 0; i < m_weapons.Length; i++)
            {
                var rank = Progress.Rank(m_weapons[i]); if (rank == 0) continue;
                m_cooldowns[i] -= delta; if (m_cooldowns[i] > 0) continue;
                var weapon = m_config.Get<WeaponRecord>(m_weapons[i]);
                var range = weapon.Range + (i == 0 ? rank * .12f : 0);
                var target = Nearest(PlayerPosition, range); if (target == null) continue;
                m_cooldowns[i] = weapon.Cooldown * Mathf.Pow(.88f, Progress.Rank("haste"));
                var damage = weapon.Damage * (1 + .25f * (rank - 1) + .2f * Progress.Rank("might"));
                if (i == 0)
                {
                    AddEffect(PlayerPosition, "slash", range * 1.2f, .22f);
                    for (var n = m_enemies.Count - 1; n >= 0; n--)
                        if (((Vector2)m_enemies[n].transform.position - PlayerPosition).sqrMagnitude < range * range) Damage(m_enemies[n], damage);
                }
                else
                {
                    var projectiles = 1 + Progress.Rank("multishot") + (rank >= 4 ? 1 : 0);
                    for (var n = 0; n < projectiles; n++)
                        Fire(weapon, target, damage, (n - (projectiles - 1) * .5f) * 12);
                }
            }
        }
        private void Fire(WeaponRecord weapon, SurvivorEntity target, float damage, float angle)
        {
            if (!m_bulletPool.TryRent(out var lease)) { PoolOverflows++; return; }
            var bullet = lease.GameObject.GetComponent<SurvivorEntity>();
            var row = m_config.Get<ProjectileRecord>(weapon.ProjectileId);
            bullet.Initialize(PlayerPosition, m_art.Find(row.Visual), .6f);
            bullet.Lease = lease; bullet.Projectile = row; bullet.Damage = damage; bullet.Pierce = row.Pierce;
            bullet.Velocity = Quaternion.Euler(0, 0, angle) * ((Vector2)target.transform.position - PlayerPosition).normalized * row.Speed;
            m_bullets.Add(bullet);
        }
        private void UpdateBullets(float delta)
        {
            for (var i = m_bullets.Count - 1; i >= 0; i--)
            {
                var bullet = m_bullets[i]; var start = (Vector2)bullet.transform.position;
                if (bullet.Projectile.Homing)
                {
                    var target = Nearest(start, 12);
                    if (target != null) bullet.Velocity = Vector2.Lerp(bullet.Velocity,
                        ((Vector2)target.transform.position - start).normalized * bullet.Projectile.Speed, delta * 5);
                }
                var end = start + bullet.Velocity * delta;
                bullet.transform.position = end; bullet.Animate(delta);
                bullet.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(bullet.Velocity.y, bullet.Velocity.x) * Mathf.Rad2Deg);
                for (var n = m_enemies.Count - 1; n >= 0 && bullet.Pierce > 0; n--)
                {
                    var enemy = m_enemies[n];
                    if (bullet.LastHit == enemy && bullet.LastHitGeneration == enemy.Generation) continue;
                    var segment = end - start;
                    var point = start + segment * Mathf.Clamp01(Vector2.Dot((Vector2)enemy.transform.position - start, segment) / Mathf.Max(.0001f, segment.sqrMagnitude));
                    var radius = bullet.Projectile.Radius + .3f * enemy.Enemy.Scale;
                    if (((Vector2)enemy.transform.position - point).sqrMagnitude > radius * radius) continue;
                    bullet.LastHit = enemy; bullet.LastHitGeneration = enemy.Generation;
                    Damage(enemy, bullet.Damage); bullet.Pierce--;
                }
                if (bullet.Pierce <= 0 || bullet.Age >= bullet.Projectile.Lifetime) Return(m_bullets, i);
            }
        }
        private void Damage(SurvivorEntity enemy, float damage)
        {
            if (!enemy.Initialized || enemy.Health <= 0) return;
            enemy.Health -= damage; enemy.Flash = .1f;
            if (enemy.Health > 0) return;
            var position = (Vector2)enemy.transform.position; var config = enemy.Enemy;
            Progress.AddKill();
            EnqueueDrop(position, 0, config.Experience);
            if (m_random.NextDouble() < Rewards.CoinChance) EnqueueDrop(position + Vector2.right * .2f, 1, 1);
            if (m_random.NextDouble() < Rewards.PotionChance) EnqueueDrop(position + Vector2.up * .2f, 2, 1);
            if (config.Elite) EnqueueDrop(position, 3, 1);
            AddEffect(position, "explosion", .7f, .3f);
            m_enemies.Remove(enemy); enemy.Lease.Dispose();
        }
        private readonly struct PendingDrop
        {
            public readonly Vector2 Position; public readonly int Kind, Value;
            public PendingDrop(Vector2 position, int kind, int value) { Position = position; Kind = kind; Value = value; }
        }
        private void EnqueueDrop(Vector2 position, int kind, int value)
        {
            var pending = new PendingDrop(position, kind, value);
            if (!TryDrop(pending)) m_pendingDrops.Enqueue(pending);
        }
        private bool TryDrop(PendingDrop drop)
        {
            if (!m_pickupPool.TryRent(out var lease)) { PoolOverflows++; return false; }
            var pickup = lease.GameObject.GetComponent<SurvivorEntity>();
            pickup.Initialize(drop.Position, m_art.Find(drop.Kind == 0 ? "ExperienceGem" : drop.Kind == 1 ? "coin" : drop.Kind == 2 ? "potion" : "chest"), drop.Kind == 0 ? .35f : .7f);
            pickup.Kind = 3; pickup.Value = drop.Value; pickup.Damage = drop.Kind; pickup.Lease = lease;
            m_pickups.Add(pickup); return true;
        }
        private void UpdatePickups(float delta)
        {
            for (var i = m_pickups.Count - 1; i >= 0; i--)
            {
                var pickup = m_pickups[i]; pickup.Animate(delta);
                var offset = PlayerPosition - (Vector2)pickup.transform.position;
                if (offset.sqrMagnitude < PickupRadius * PickupRadius)
                    pickup.transform.position += (Vector3)(offset.normalized * delta * 8);
                if (offset.sqrMagnitude > .25f) continue;
                switch ((int)pickup.Damage)
                {
                    case 0: Progress.AddExperience(pickup.Value); break;
                    case 1: Progress.AddCoins(pickup.Value); break;
                    case 2: Health = Mathf.Min(MaximumHealth, Health + Rewards.PotionHealing); break;
                    case 3: Progress.AddCoins(Rewards.ChestCoins); m_treasureChoices++; break;
                }
                if ((int)pickup.Damage != 0 || m_random.Next(5) == 0) m_sound?.Invoke("pickup");
                Return(m_pickups, i);
            }
        }
        private void AddEffect(Vector2 position, string id, float scale, float lifetime)
        {
            if (!m_effectPool.TryRent(out var lease)) { PoolOverflows++; return; }
            var effect = lease.GameObject.GetComponent<SurvivorEntity>();
            effect.Initialize(position, m_art.Find(id), scale); effect.Lease = lease; effect.Health = lifetime;
            m_effects.Add(effect);
        }
        private void UpdateEffects(float delta)
        {
            for (var i = m_effects.Count - 1; i >= 0; i--)
            { m_effects[i].Animate(delta); if (m_effects[i].Age >= m_effects[i].Health) Return(m_effects, i); }
        }
        private static void Return(List<SurvivorEntity> list, int index)
        {
            var entity = list[index]; var last = list.Count - 1;
            list[index] = list[last]; list.RemoveAt(last); entity.Lease.Dispose();
        }
        public List<string> GetChoices() => UpgradeSelection.Choose(m_upgradeOptions, Progress, m_random);
        public bool SelectUpgrade(string id)
        {
            if (!ChoicePending || Stopped) return false;
            var row = m_config.Get<UpgradeRecord>(id);
            if (!Progress.Upgrade(id, row.Maximum)) return false;
            if (id == "vitality") Health = Mathf.Min(MaximumHealth, Health + 20);
            if (Progress.PendingLevels > 0) Progress.ConsumeLevel(); else m_treasureChoices--;
            m_sound?.Invoke("upgrade"); return true;
        }
        public void ResolveFullDeck()
        {
            if (!ChoicePending || Stopped) return;
            Health = MaximumHealth; Progress.AddCoins(10);
            if (Progress.PendingLevels > 0) Progress.ConsumeLevel(); else m_treasureChoices--;
        }
        public void End(bool victory, string reason)
        {
            if (Stopped || m_disposed) return;
            Stopped = true; Finished?.Invoke(Progress.Finish(victory, reason));
        }
        public void Stop() { Stopped = true; }
        internal void MaintainPressureScenario()
        {
            // This fixture is called only by an explicit --survivor-profile-output launch.
            // It measures actual renderer / update / bounded-pool work while keeping a stable population.
            Health = MaximumHealth;
            while (Progress.PendingLevels > 0) Progress.ConsumeLevel();
            m_treasureChoices = 0;
            for (var n = m_enemies.Count; n < 200; n++) SpawnEnemy(n % 8 == 0, Stage.Enemies);
            var target = Nearest(PlayerPosition, 100);
            var weapon = m_config.Get<WeaponRecord>("bow");
            for (var n = m_bullets.Count; target != null && n < 100; n++) Fire(weapon, target, 1, n * 17);
            while (m_pickups.Count > 200) Return(m_pickups, m_pickups.Count - 1);
            for (var n = m_pickups.Count; n < 200; n++)
            {
                var angle = n * .618f * Mathf.PI * 2;
                TryDrop(new PendingDrop(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 8, 0, 1));
            }
        }
        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true; Stopped = true; Finished = null;
            foreach (var list in new[] { m_enemies, m_bullets, m_pickups, m_effects })
            { for (var i = list.Count - 1; i >= 0; i--) Return(list, i); }
            m_pendingDrops.Clear(); m_scope.Dispose(); m_diagnostics.Dispose();
        }
    }
}
