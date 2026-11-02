using System;
using Yato.Config;
namespace Yato.Survivor
{
    [Serializable] public sealed class EnemyRecord : ConfigRecord
    {
        public string Visual;
        public float Health, Speed, Damage, UnlockSeconds, Scale = 1;
        public int Experience = 1;
        public bool Elite;
        public override void Validate(ConfigValidationContext c)
        {
            if (string.IsNullOrEmpty(Visual) || !(Health > 0) || !(Speed > 0) || !(Damage > 0) ||
                UnlockSeconds < 0 || !(Scale > 0) || Experience < 1) c.Report("Invalid enemy values");
        }
    }
    public sealed class EnemyTable : ConfigTable<EnemyRecord> { }
}
