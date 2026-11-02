using System;
using UnityEngine;
using Yato.Pooling;

namespace Yato.Survivor
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SurvivorEntity : MonoBehaviour, IPoolCallbacks
    {
        public SpriteRenderer Renderer;
        [NonSerialized] public PooledInstance Lease;
        [NonSerialized] public EnemyRecord Enemy;
        [NonSerialized] public ProjectileRecord Projectile;
        [NonSerialized] public Vector2 Velocity;
        [NonSerialized] public float Health, Age, Damage, Flash;
        [NonSerialized] public int Kind, Value, Generation, Pierce;
        [NonSerialized] public bool Initialized;
        [NonSerialized] public SurvivorEntity LastHit;
        [NonSerialized] public int LastHitGeneration;
        private SpriteSequence m_sequence;
        private Color m_color;

        private void Awake() { if (Renderer == null) Renderer = GetComponent<SpriteRenderer>(); }
        public void OnRent()
        {
            Generation++; Age = 0; Flash = 0; Initialized = false;
            Kind = 0; Value = 0; Damage = 0; Velocity = Vector2.zero;
            Enemy = null; Projectile = null; LastHit = null; LastHitGeneration = 0;
            Renderer.color = Color.white; Renderer.flipX = false;
            transform.localScale = Vector3.one; transform.rotation = Quaternion.identity;
        }
        public void OnReturn() { Initialized = false; m_sequence = null; Lease = default; }
        public void Initialize(Vector2 position, SpriteSequence sequence, float scale = 1, Color? tint = null)
        {
            transform.position = new Vector3(position.x, position.y, 0);
            transform.localScale = Vector3.one * scale;
            m_sequence = sequence; m_color = tint ?? Color.white;
            Renderer.color = m_color; Renderer.sprite = sequence.Frames[0]; Initialized = true;
        }
        public void Animate(float delta)
        {
            if (!Initialized) return;
            Age += delta;
            if (m_sequence != null) Renderer.sprite = m_sequence.Frames[(int)(Age * m_sequence.FramesPerSecond) % m_sequence.Frames.Length];
            Flash = Mathf.Max(0, Flash - delta);
            Renderer.color = Flash > 0 ? new Color(1, .35f, .3f) : m_color;
            Renderer.sortingOrder = Kind == 3 ? 30 : 100 - Mathf.RoundToInt(transform.position.y * 10);
        }
        public void SetSequence(SpriteSequence sequence)
        {
            if (m_sequence == sequence) return;
            m_sequence = sequence; Age = 0;
        }
    }
}
