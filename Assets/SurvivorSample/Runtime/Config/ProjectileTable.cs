using System;
using Yato.Config;
namespace Yato.Survivor
{
    [Serializable] public sealed class ProjectileRecord : ConfigRecord
    {
        public string Visual;
        public float Speed = 9, Lifetime = 3, Radius = .25f;
        public bool Homing;
        public int Pierce = 1;
        public override void Validate(ConfigValidationContext c)
        {
            if (!(Speed > 0) || !(Lifetime > 0) || !(Radius > 0) || Pierce < 1 || string.IsNullOrEmpty(Visual)) c.Report("Invalid projectile");
        }
    }
    public sealed class ProjectileTable : ConfigTable<ProjectileRecord> { }
}
