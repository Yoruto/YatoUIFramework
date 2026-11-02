using System;
using Yato.Config;
namespace Yato.Survivor
{
    [Serializable] public sealed class RewardRecord : ConfigRecord
    {
        public int BaseExperience = 8, ExperienceGrowth = 5, ChestCoins = 20;
        public float CoinChance = .18f, PotionChance = .035f, PotionHealing = 20;
        public override void Validate(ConfigValidationContext c)
        {
            if (BaseExperience < 1 || ExperienceGrowth < 0 || ChestCoins < 0 || CoinChance < 0 || CoinChance > 1 ||
                PotionChance < 0 || PotionChance > 1 || !(PotionHealing > 0)) c.Report("Invalid reward values");
        }
    }
    public sealed class RewardTable : ConfigTable<RewardRecord> { }
}
