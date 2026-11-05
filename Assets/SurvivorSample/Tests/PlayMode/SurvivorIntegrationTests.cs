using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Yato.App;
using Yato.Core;
using Yato.Persistence;
using Yato.Save;
using Yato.Survivor.Domain;
using Yato.UI;

namespace Yato.Survivor.Tests
{
    public sealed class SurvivorIntegrationTests
    {
        private string m_oldRoot;
        private Keyboard m_keyboard;
        private InputSettings.BackgroundBehavior m_background;
        private ApplicationLifecycle m_lifecycle;
        private bool m_focused;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode m_editorInput;
#endif
        private SurvivorGame Game => UnityEngine.Object.FindAnyObjectByType<SurvivorGame>();
        private static SurvivorPageView Page(SurvivorPageKind kind) => UnityEngine.Object.FindObjectsByType<SurvivorPageView>().FirstOrDefault(p => p.Kind == kind);
        private static void Click(SurvivorPageKind kind, string name)
        {
            var page = Page(kind); Assert.NotNull(page, kind.ToString()); page.GetComponent<ComponentBinder>().Get<Button>(name).onClick.Invoke();
        }
        private IEnumerator Until(Func<bool> predicate, string reason)
        {
            var deadline = Time.realtimeSinceStartup + 30;
            while (!predicate())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, reason + ": " + Game?.ErrorMessage);
                yield return null;
            }
        }
        [UnitySetUp] public IEnumerator Setup()
        {
            m_background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            m_focused = ApplicationLifecycle.IsFocused;
#if UNITY_EDITOR
            m_editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            m_oldRoot = Environment.GetEnvironmentVariable("YATO_SURVIVOR_TEST_DATA");
            Environment.SetEnvironmentVariable("YATO_SURVIVOR_TEST_DATA", Path.Combine(Path.GetTempPath(), "YatoSurvivorTests-" + Guid.NewGuid().ToString("N")));
            GameBootstrap.ResetForTests();
            yield return SceneManager.LoadSceneAsync("Assets/SurvivorSample/Scenes/SurvivorBootstrap.unity", LoadSceneMode.Single);
            yield return Until(() => Game != null && Game.Ready, "bootstrap ready");
            // Batch mode has no focused Game view; exercise movement with a focused application.
            m_lifecycle = UnityEngine.Object.FindAnyObjectByType<ApplicationLifecycle>();
            Assert.NotNull(m_lifecycle);
            m_lifecycle.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
        }
        [UnityTearDown] public IEnumerator Teardown()
        {
            if (m_keyboard != null) InputSystem.RemoveDevice(m_keyboard);
            if (Game != null) UnityEngine.Object.Destroy(Game.gameObject);
            yield return null;
            GameBootstrap.ResetForTests();
            Environment.SetEnvironmentVariable("YATO_SURVIVOR_TEST_DATA", m_oldRoot);
            InputSystem.settings.backgroundBehavior = m_background;
            if (m_lifecycle != null)
                m_lifecycle.SendMessage("OnApplicationFocus", m_focused, SendMessageOptions.DontRequireReceiver);
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = m_editorInput;
#endif
        }
        [UnityTest] public IEnumerator RoutedBattlePauseUpgradeAndRestartReleaseOwnedResources()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(Game.DataRoot));
            Click(SurvivorPageKind.Menu, "Btn_Start");
            yield return Until(() => Game.Session != null && !Game.IsBusy, "run ready");
            var runId = Game.Session.RunId;
            Assert.True(Game.Services.Input.Actions.FindAction("UI/Click").enabled);
            m_keyboard = InputSystem.AddDevice<Keyboard>(); InputSystem.EnableDevice(m_keyboard);
            var beforeMove = Game.Session.PlayerPosition;
            InputSystem.QueueStateEvent(m_keyboard, new KeyboardState(Key.W)); InputSystem.Update();
            Assert.Greater(Game.Services.Input.Actions.FindAction("Gameplay/Move").ReadValue<Vector2>().y, .9f);
            yield return Until(() => Game.Session.PlayerPosition.y > beforeMove.y, "Keyboard movement did not reach the live game update");
            Assert.Greater(Game.Session.PlayerPosition.y, beforeMove.y, "Keyboard movement did not reach the live game update");
            InputSystem.QueueStateEvent(m_keyboard, new KeyboardState()); InputSystem.Update();
            var sessionType = typeof(SurvivorRunSession);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var player = (SurvivorEntity)sessionType.GetField("m_player", flags).GetValue(Game.Session);
            player.transform.position = new Vector2(Game.Session.Stage.HalfWidth, Game.Session.Stage.HalfHeight);
            var enemies = (System.Collections.Generic.List<SurvivorEntity>)sessionType.GetField("m_enemies", flags).GetValue(Game.Session);
            var first = enemies.Count;
            for (var n = 0; n < 20; n++) sessionType.GetMethod("SpawnEnemy", flags).Invoke(Game.Session, new object[] { false, Game.Session.Stage.Enemies });
            foreach (var enemy in enemies.Skip(first))
                Assert.GreaterOrEqual(((Vector2)enemy.transform.position - Game.Session.PlayerPosition).sqrMagnitude, 36, "Boundary clamping spawned an enemy on top of the player");
            Game.Session.Progress.AddExperience(8);
            Click(SurvivorPageKind.Hud, "Btn_Pause");
            yield return Until(() => Page(SurvivorPageKind.Pause) != null, "pause");
            Assert.IsNull(Page(SurvivorPageKind.Upgrade), "An upgrade must wait for an opening pause dialog to close");
            var elapsed = Game.Session.Progress.Elapsed;
            yield return new WaitForSecondsRealtime(.1f);
            Assert.AreEqual(elapsed, Game.Session.Progress.Elapsed);
            Click(SurvivorPageKind.Pause, "Btn_Resume");
            Assert.AreEqual(0, Game.Services.Pause.PauseCount);
            Game.Session.Progress.AddExperience(30);
            while (Game.Session.Progress.PendingLevels > 0)
            {
                yield return Until(() => Page(SurvivorPageKind.Upgrade) != null, "upgrade");
                Assert.True(Game.Services.Pause.IsPaused);
                Click(SurvivorPageKind.Upgrade, "Btn_Choice0");
                yield return null;
            }
            Game.Session.End(false, "PlayMode verification");
            yield return Until(() => Game.Session == null && !Game.IsBusy && Page(SurvivorPageKind.Results) != null, "results");
            Assert.AreEqual(1, SceneManager.sceneCount); Assert.AreEqual(0, Game.Services.Pause.PauseCount);
            var baseline = Game.Services.Resources.Stats.ActiveAssetCount;
            Click(SurvivorPageKind.Results, "Btn_Retry");
            yield return Until(() => Game.Session != null && !Game.IsBusy, "restart");
            Assert.AreNotEqual(runId, Game.Session.RunId); Assert.AreEqual(1, Game.Session.Progress.Level);
            Game.Session.End(false, "PlayMode verification second run");
            yield return Until(() => Game.Session == null && !Game.IsBusy && Page(SurvivorPageKind.Results) != null, "second results");
            Assert.AreEqual(baseline, Game.Services.Resources.Stats.ActiveAssetCount);
            Assert.AreEqual(0, Game.Services.Pause.PauseCount);
        }

        [UnityTest] public IEnumerator ReopeningSettingsKeepsOneSubscriptionAndOneVolumeAction()
        {
            var bus = (EventBus)Game.Services.Events;
            int menuSubscriptions = bus.SubscriberCount<SurvivorUiChanged>();
            Click(SurvivorPageKind.Menu, "Btn_Settings");
            yield return Until(() => Page(SurvivorPageKind.Settings) != null, "settings");
            var page = Page(SurvivorPageKind.Settings);
            for (int i = 0; i < 3; i++)
            {
                var reopened = UIRouter.OpenAsync(SurvivorPages.Get(SurvivorPageKind.Settings), new SurvivorPageArgs { Game = Game });
                yield return Until(() => reopened.IsDone, "settings reopen");
                Assert.AreSame(page, reopened.GetResult().Context.View);
            }
            Assert.AreEqual(menuSubscriptions + 1, bus.SubscriberCount<SurvivorUiChanged>());

            int changed = 0, viewEvents = 0;
            Action<SurvivorSettings> onChanged = _ => changed++;
            Action<string, float> onVolume = (_, __) => viewEvents++;
            Game.Services.Settings.Changed += onChanged;
            page.VolumeChanged += onVolume;
            try
            {
                var slider = page.GetComponent<ComponentBinder>().Get<Slider>("Slider_Master");
                slider.onValueChanged.Invoke(.43f);
                Assert.AreEqual(1, viewEvents, "重复 OnOpen 不能累积 UnityEvent listeners");
                Assert.AreEqual(1, changed, "一个音量事件只能调用一次设置 Apply");
            }
            finally
            {
                Game.Services.Settings.Changed -= onChanged;
                page.VolumeChanged -= onVolume;
            }
            page.Close();
            Assert.AreEqual(menuSubscriptions, bus.SubscriberCount<SurvivorUiChanged>(), "关闭后不能残留被覆盖的旧订阅");
        }

        [UnityTest] public IEnumerator CancellingResultsSavePropagatesCancellationWithoutOpeningPage()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var savesField = typeof(SurvivorServices).GetField("<Saves>k__BackingField", flags);
            var summaryField = typeof(SurvivorGame).GetField("<LastRun>k__BackingField", flags);
            var previousSaves = Game.Services.Saves;
            var previousSummary = Game.LastRun;
            var store = new CancelledSaveStore(Path.Combine(Game.DataRoot, "cancel-results"));
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    savesField.SetValue(Game.Services, new SaveService<SurvivorHistory>(store, 1, SurvivorHistory.Validate));
                    summaryField.SetValue(Game, new RunSummary { Level = 1, Reason = "Cancellation regression" });
                    var operation = (Task)typeof(SurvivorGame).GetMethod("ResultsAsync", flags)
                        .Invoke(Game, new object[] { null, cancellation.Token });
                    Assert.IsTrue(store.WriteStarted, "测试必须在真实保存 await 处取消");
                    cancellation.Cancel();
                    yield return Until(() => operation.IsCompleted, "results cancellation");
                    Assert.IsTrue(operation.IsCanceled);
                    Assert.Catch<OperationCanceledException>(() => operation.GetAwaiter().GetResult());
                    Assert.IsFalse(UIRouter.IsOpen(SurvivorPages.Get(SurvivorPageKind.Results).PageId));
                }
                finally
                {
                    savesField.SetValue(Game.Services, previousSaves);
                    summaryField.SetValue(Game, previousSummary);
                }
            }
        }

        private sealed class CancelledSaveStore : IFileStore
        {
            public CancelledSaveStore(string rootPath) { RootPath = rootPath; }
            public string RootPath { get; }
            public bool WriteStarted;
            public bool Exists(string path) => false;
            public Task WriteAllTextAsync(string path, string text, CancellationToken ct = default)
            { WriteStarted = true; return Task.Delay(Timeout.Infinite, ct); }
            public long GetFileSize(string path) => throw new NotSupportedException();
            public DateTime GetLastWriteTimeUtc(string path) => throw new NotSupportedException();
            public byte[] ReadAllBytes(string path) => throw new NotSupportedException();
            public string ReadAllText(string path) => throw new NotSupportedException();
            public void WriteAllBytes(string path, byte[] data, CancellationToken ct = default) => throw new NotSupportedException();
            public void WriteAllText(string path, string text, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<string> ReadAllTextAsync(string path, CancellationToken ct = default) => throw new NotSupportedException();
            public Task WriteAllBytesAsync(string path, byte[] data, CancellationToken ct = default) => throw new NotSupportedException();
            public bool Delete(string path) => throw new NotSupportedException();
            public void EnsureDirectory(string path) => throw new NotSupportedException();
            public IReadOnlyList<string> EnumerateFiles(string directory, string pattern = "*", bool recursive = false) => throw new NotSupportedException();
            public IDisposable AcquireLock(string path) => throw new NotSupportedException();
        }
    }
}
