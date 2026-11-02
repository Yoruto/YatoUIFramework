using System;
using UnityEngine;
using Yato.Config;
namespace Yato.FullSample
{
    [Serializable] public sealed class DemoConfigRecord : ConfigRecord
    {
        public int PoolCapacity = 8;
        public override void Validate(ConfigValidationContext context)
        { if (PoolCapacity < 1 || PoolCapacity > 128) context.Report("PoolCapacity must be between 1 and 128"); }
    }
    [CreateAssetMenu(menuName = "Yato/Samples/Demo Config Table")]
    public sealed class DemoConfigTable : ConfigTable<DemoConfigRecord> { }
}
