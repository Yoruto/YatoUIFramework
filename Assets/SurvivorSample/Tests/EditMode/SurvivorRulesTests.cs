using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Yato.Config;
using Yato.Survivor.Domain;

namespace Yato.Survivor.Tests
{
    public sealed class SurvivorRulesTests
    {
        [Test] public void ExperienceCanQueueMultipleLevelsWithoutDroppingRemainder()
        {
            var run = new RunProgress(8, 5); run.AddExperience(30);
            Assert.AreEqual(3, run.Level); Assert.AreEqual(2, run.PendingLevels); Assert.AreEqual(9, run.Experience);
            Assert.True(run.ConsumeLevel()); Assert.True(run.ConsumeLevel()); Assert.False(run.ConsumeLevel());
        }
        [Test] public void NegativeExperienceAndInvalidTimeAreRejected()
        {
            var run = new RunProgress(); Assert.Throws<ArgumentOutOfRangeException>(() => run.AddExperience(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.NaN)); Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(-1));
        }
        [Test] public void CompletedRunCannotGrantMoreRewardsOrFinishTwice()
        {
            var run = new RunProgress(); run.Tick(5); run.AddKill(); run.AddCoins(2);
            var result = run.Finish(false, "test"); run.AddKill(); run.AddCoins(30); run.Tick(10); run.AddExperience(50);
            Assert.AreEqual(5, run.Elapsed); Assert.AreEqual(1, result.Kills); Assert.AreEqual(2, run.Coins);
            Assert.AreEqual(0, run.Experience); Assert.Throws<InvalidOperationException>(() => run.Finish(true, "duplicate"));
        }
        [Test] public void UpgradeDeckFiltersMaxedAndDuplicateIds()
        {
            var run = new RunProgress(); run.Upgrade("maxed", 1);
            var options = new[] { new UpgradeOption("maxed", 1, 100), new UpgradeOption("a", 5, 2), new UpgradeOption("a", 5, 2), new UpgradeOption("b", 5, 3), new UpgradeOption("disabled", 5, 0) };
            var chosen = UpgradeSelection.Choose(options, run, new Random(9));
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, chosen);
        }
        [Test] public void WeightedChoicesAreReproducibleAndUnique()
        {
            var options = Enumerable.Range(0, 10).Select(i => new UpgradeOption("choice" + i, 4, i + 1)).ToArray();
            var a = UpgradeSelection.Choose(options, new RunProgress(), new Random(55));
            var b = UpgradeSelection.Choose(options, new RunProgress(), new Random(55));
            CollectionAssert.AreEqual(a, b); Assert.AreEqual(3, a.Distinct().Count());
        }
        [Test] public void FullDeckReturnsEmptyAndUpgradeCannotExceedCap()
        {
            var run = new RunProgress(); Assert.True(run.Upgrade("x", 1)); Assert.False(run.Upgrade("x", 1));
            Assert.IsEmpty(UpgradeSelection.Choose(new[] { new UpgradeOption("x", 1, 10) }, run, new Random(1)));
        }
        [Test] public void HistoryAccumulatesAndValidates()
        {
            var history = new SurvivorHistory(); history.Include(new RunSummary { Seconds = 600, Coins = 12, Kills = 200, Victory = true });
            history.Include(new RunSummary { Seconds = 8, Coins = 1, Kills = 2 });
            Assert.AreEqual(2, history.Runs); Assert.AreEqual(1, history.Victories); Assert.AreEqual(202, history.TotalKills);
            Assert.AreEqual(600, history.BestSeconds); Assert.IsNull(SurvivorHistory.Validate(history));
            history.BestSeconds = float.NaN; Assert.NotNull(SurvivorHistory.Validate(history));
        }
        [Test] public void RealCatalogAndSpriteSetValidate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ConfigCatalog>("Assets/GameRes/Config/Survivor/SurvivorCatalog.asset");
            Assert.NotNull(catalog); Assert.IsEmpty(catalog.Validate());
            var art = AssetDatabase.LoadAssetAtPath<SurvivorSpriteSet>("Assets/GameRes/Survivor/Presentation/SurvivorArt.asset");
            Assert.NotNull(art); Assert.IsEmpty(art.Validate()); Assert.AreEqual(6, art.Find("knight_run").Frames.Length);
            Assert.AreEqual(4, art.Find("skeleton").Frames.Length); Assert.NotNull(art.Music);
        }
        [Test] public void ConfiguredSpawnWavesCoverTheStageWithoutGapsOrOverlap()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ConfigCatalog>("Assets/GameRes/Config/Survivor/SurvivorCatalog.asset");
            using (var config = new ConfigService(catalog))
            {
                var stage = config.Get<StageRecord>("dungeon");
                var waves = stage.Waves.Select(config.Get<SpawnWaveRecord>).OrderBy(w => w.StartSeconds).ToArray();
                var end = 0f;
                foreach (var wave in waves) { Assert.AreEqual(end, wave.StartSeconds); Assert.LessOrEqual(wave.MaximumAlive, stage.EnemyCapacity); end = wave.EndSeconds; }
                Assert.AreEqual(stage.Duration, end);
            }
        }
    }
}
