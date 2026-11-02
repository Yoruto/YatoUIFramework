using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Yato.Core;
using Yato.UI.Input;

namespace Yato.Survivor
{
    [Serializable] public sealed class SpriteSequence
    {
        public string Id;
        public Sprite[] Frames = Array.Empty<Sprite>();
        public float FramesPerSecond = 8;
    }
    // Serialized subasset references keep Editor and Bundle frame selection identical.
    public sealed class SurvivorSpriteSet : ScriptableObject, IProjectValidationSource
    {
        public SpriteSequence[] Sequences = Array.Empty<SpriteSequence>();
        public InputActionAsset Input;
        public UIInputProfile InputProfile;
        public AudioClip Music, HitSound, PickupSound, UpgradeSound;
        public string CatalogKey, PlayerKey, EnemyKey, ProjectileKey, PickupKey, EffectKey;
        public SpriteSequence Find(string id)
        {
            foreach (var entry in Sequences) if (entry.Id == id) return entry;
            throw new KeyNotFoundException("Sprite sequence not found: " + id);
        }
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Sequences)
            {
                if (item == null || string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id)) { errors.Add("Duplicate or empty sequence ID"); continue; }
                if (item.Frames == null || item.Frames.Length == 0) errors.Add("Missing frames: " + item.Id);
                else foreach (var sprite in item.Frames) if (sprite == null) errors.Add("Null frame: " + item.Id);
                if (!(item.FramesPerSecond > 0)) errors.Add("Invalid animation speed: " + item.Id);
            }
            if (Input == null || InputProfile == null) errors.Add("Missing input asset / profile");
            foreach (var key in new[] { CatalogKey, PlayerKey, EnemyKey, ProjectileKey, PickupKey, EffectKey })
                if (string.IsNullOrWhiteSpace(key)) errors.Add("Missing resource key");
            return errors;
        }
    }
}
