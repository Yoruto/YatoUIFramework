using System;
using Yato.Config;
namespace Yato.Survivor
{
    [Serializable] public sealed class WeaponRecord : ConfigRecord
    {
        public string Icon, ProjectileId;
        public float Damage, Cooldown, Range;
        public override void Validate(ConfigValidationContext c)
        {
            if (!(Damage > 0) || !(Cooldown > 0) || !(Range > 0) || string.IsNullOrEmpty(Icon)) c.Report("Invalid weapon");
            if (!string.IsNullOrEmpty(ProjectileId)) c.Require<ProjectileRecord>(ProjectileId);
        }
    }
    public sealed class WeaponTable : ConfigTable<WeaponRecord> { }
}
