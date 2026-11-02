using System;
using System.Collections.Generic;

namespace Yato.Survivor.Domain
{
    [Serializable]
    public sealed class RunSummary
    {
        public bool Victory;
        public float Seconds;
        public int Kills, Coins, Level;
        public string Reason;
    }

    [Serializable]
    public sealed class SurvivorHistory
    {
        public int Runs, Victories, TotalKills, TotalCoins;
        public float BestSeconds;
        public void Include(RunSummary run)
        {
            Runs++;
            if (run.Victory) Victories++;
            TotalKills += run.Kills;
            TotalCoins += run.Coins;
            BestSeconds = Math.Max(BestSeconds, run.Seconds);
        }
        public static string Validate(SurvivorHistory data) => data == null || data.Runs < 0 ||
            data.Victories < 0 || data.Victories > data.Runs || data.TotalKills < 0 || data.TotalCoins < 0 ||
            float.IsNaN(data.BestSeconds) || float.IsInfinity(data.BestSeconds) || data.BestSeconds < 0
            ? "Invalid survivor history" : null;
    }

    public sealed class RunProgress
    {
        private readonly Dictionary<string, int> m_upgrades = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Level { get; private set; } = 1;
        public int Experience { get; private set; }
        public int PendingLevels { get; private set; }
        public int Kills { get; private set; }
        public int Coins { get; private set; }
        public float Elapsed { get; private set; }
        public bool Ended { get; private set; }
        public int BaseExperience { get; }
        public int ExperienceGrowth { get; }
        public int NextLevelExperience => BaseExperience + (Level - 1) * ExperienceGrowth;

        public RunProgress(int baseExperience = 8, int experienceGrowth = 5)
        {
            if (baseExperience < 1 || experienceGrowth < 0) throw new ArgumentOutOfRangeException();
            BaseExperience = baseExperience;
            ExperienceGrowth = experienceGrowth;
        }
        public void Tick(float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (!Ended) Elapsed += delta;
        }
        public void AddExperience(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (Ended) return;
            Experience += amount;
            while (Experience >= NextLevelExperience)
            {
                Experience -= NextLevelExperience;
                Level++; PendingLevels++;
            }
        }
        public bool ConsumeLevel()
        {
            if (PendingLevels == 0 || Ended) return false;
            PendingLevels--; return true;
        }
        public void AddKill() { if (!Ended) Kills++; }
        public void AddCoins(int amount) { if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount)); if (!Ended) Coins += amount; }
        public int Rank(string id) => m_upgrades.TryGetValue(id, out var rank) ? rank : 0;
        public bool Upgrade(string id, int maximum)
        {
            if (Ended || string.IsNullOrEmpty(id) || maximum < 1 || Rank(id) >= maximum) return false;
            m_upgrades[id] = Rank(id) + 1; return true;
        }
        public RunSummary Finish(bool victory, string reason)
        {
            if (Ended) throw new InvalidOperationException("A run can only finish once");
            Ended = true;
            return new RunSummary { Victory = victory, Reason = reason, Seconds = Elapsed, Kills = Kills, Coins = Coins, Level = Level };
        }
    }

    public readonly struct UpgradeOption
    {
        public readonly string Id;
        public readonly int Maximum, Weight;
        public UpgradeOption(string id, int maximum, int weight) { Id = id; Maximum = maximum; Weight = weight; }
    }

    public static class UpgradeSelection
    {
        // Weighted selection without replacement. Output order is reproducible for a fixed seed.
        public static List<string> Choose(IReadOnlyList<UpgradeOption> options, RunProgress run, Random random, int count = 3)
        {
            if (options == null || run == null || random == null || count < 0) throw new ArgumentException();
            var eligible = new List<UpgradeOption>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in options)
                if (!string.IsNullOrEmpty(option.Id) && option.Weight > 0 && run.Rank(option.Id) < option.Maximum && seen.Add(option.Id))
                    eligible.Add(option);
            var chosen = new List<string>(count);
            while (eligible.Count > 0 && chosen.Count < count)
            {
                double total = 0;
                foreach (var option in eligible) total += option.Weight;
                var roll = random.NextDouble() * total;
                var selected = eligible.Count - 1;
                for (var i = 0; i < eligible.Count; i++) { roll -= eligible[i].Weight; if (roll < 0) { selected = i; break; } }
                chosen.Add(eligible[selected].Id); eligible.RemoveAt(selected);
            }
            return chosen;
        }
    }
}
