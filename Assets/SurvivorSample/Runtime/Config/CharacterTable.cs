using System;
using Yato.Config;
namespace Yato.Survivor
{
    [Serializable] public sealed class CharacterRecord : ConfigRecord
    {
        public float Health = 100, Speed = 4, PickupRadius = 1.8f;
        public string InitialWeapon = "sword";
        public override void Validate(ConfigValidationContext c)
        {
            if (!(Health > 0) || !(Speed > 0) || !(PickupRadius > 0)) c.Report("Character values must be positive");
            c.Require<WeaponRecord>(InitialWeapon);
        }
    }
    public sealed class CharacterTable : ConfigTable<CharacterRecord> { }
}
