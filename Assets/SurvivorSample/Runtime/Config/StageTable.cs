using System;
using Yato.Config;
namespace Yato.Survivor
{
    [Serializable] public sealed class StageRecord : ConfigRecord
    {
        public float Duration = 600, HalfWidth = 19, HalfHeight = 15;
        public float EliteInterval = 45;
        public int EnemyCapacity = 256, ProjectileCapacity = 160, PickupCapacity = 400, EffectCapacity = 96;
        public string[] Enemies = Array.Empty<string>();
        public string[] Upgrades = Array.Empty<string>();
        public string[] Waves = Array.Empty<string>();
        public override void Validate(ConfigValidationContext c)
        {
            if (!(Duration > 0) || HalfWidth < 8 || HalfHeight < 8 || EnemyCapacity < 1 || ProjectileCapacity < 1 || PickupCapacity < 1 || EffectCapacity < 1)
                c.Report("Invalid stage values");
            if (Enemies == null || Enemies.Length == 0) c.Report("Stage requires enemies");
            else foreach (var id in Enemies) c.Require<EnemyRecord>(id);
            if (Upgrades == null || Upgrades.Length == 0) c.Report("Stage requires upgrades");
            else foreach (var id in Upgrades) c.Require<UpgradeRecord>(id);
            if (!(EliteInterval > 0)) c.Report("Invalid elite interval");
            if (Waves == null || Waves.Length == 0) c.Report("Stage requires spawn waves");
            else foreach (var id in Waves) c.Require<SpawnWaveRecord>(id);
        }
    }
    public sealed class StageTable : ConfigTable<StageRecord> { }
}
