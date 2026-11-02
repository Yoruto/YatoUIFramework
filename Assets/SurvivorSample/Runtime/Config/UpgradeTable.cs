using System;
using Yato.Config;
namespace Yato.Survivor
{
    [Serializable] public sealed class UpgradeRecord : ConfigRecord
    {
        public string Title, Description, Icon, WeaponId;
        public int Maximum = 5, Weight = 10;
        public override void Validate(ConfigValidationContext c)
        {
            if (string.IsNullOrEmpty(Title) || Maximum < 1 || Weight < 1) c.Report("Invalid upgrade");
            if (!string.IsNullOrEmpty(WeaponId)) c.Require<WeaponRecord>(WeaponId);
        }
    }
    public sealed class UpgradeTable : ConfigTable<UpgradeRecord> { }
}
