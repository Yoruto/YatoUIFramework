using System;
using Yato.Config;
namespace Yato.Survivor
{
    [Serializable] public sealed class SpawnWaveRecord : ConfigRecord
    {
        public float StartSeconds, EndSeconds, Interval;
        public int MaximumAlive = 200;
        public string[] Enemies = Array.Empty<string>();
        public override void Validate(ConfigValidationContext c)
        {
            if (StartSeconds < 0 || !(EndSeconds > StartSeconds) || !(Interval > 0) || MaximumAlive < 1) c.Report("Invalid spawn wave timing / capacity");
            if (Enemies == null || Enemies.Length == 0) c.Report("Wave requires enemies");
            else foreach (var id in Enemies) c.Require<EnemyRecord>(id);
        }
    }
    public sealed class SpawnWaveTable : ConfigTable<SpawnWaveRecord> { }
}
