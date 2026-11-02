using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PureMVC.Patterns.Facade;
using Yato.App;
using Yato.Config;
using Yato.Core;
using Yato.Flow;
using Yato.Input;
using Yato.Resources;
using Yato.Save;
using Yato.Survivor.Domain;
using Yato.UI;
using Yato.UI.Input;

namespace Yato.Survivor
{
    [DefaultExecutionOrder(-1100)]
    public sealed class SurvivorGame : MonoBehaviour
    {
        public string ArtKey = "survivor/presentation/survivorart";
        public string ArenaPath = "Assets/SurvivorSample/Scenes/SurvivorArena.unity";
        public bool ForceBundles;
        [Tooltip("Optional isolated file-store root for automated verification; empty uses persistentDataPath.")]
        public string DataRoot;
        public SurvivorServices Services { get; private set; }
        public SurvivorRunSession Session { get; private set; }
        public SurvivorHistory History { get; private set; } = new SurvivorHistory();
        public RunSummary LastRun { get; private set; }
        public string Status { get; private set; } = "A ten-minute watch. One chance to see the sunrise.";
        public string ErrorMessage { get; private set; }
        public bool BackupAvailable { get; private set; }
        public FlowController Flow { get; private set; }
        public bool IsBusy => m_busy || (Flow?.IsBusy ?? false);
        public bool Ready { get; private set; }
        public event Action QuitRequested;
        private SceneContext m_arena;
        private UIPageHandle m_hud, m_modal, m_loading;
        private CancellationTokenSource m_lifetime;
        private ApplicationExitCoordinator m_exit;
        private IDisposable m_pauseInput;
        private InputAction m_movement;
        private bool m_busy, m_choiceOpening, m_modalOpening, m_settingsOpening, m_destroyed, m_saveWritable = true;
        private bool m_settingsSaving, m_recovering;
        private float m_hudTimer;
        private string[] m_choices;
        private Task m_operation = Task.CompletedTask;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            try
            {
                HotUpdateMaintenance.PrepareStartup();
                ResourceServiceFactory.ForceBundleMode = ForceBundles;
                if (string.IsNullOrWhiteSpace(DataRoot)) DataRoot = SurvivorPerformanceScenario.Argument("--survivor-test-data") ?? Environment.GetEnvironmentVariable("YATO_SURVIVOR_TEST_DATA");
                Services = new SurvivorServices(DataRoot);
                GameBootstrap.RegisterService(Services);
            }
            catch (Exception error) { Debug.LogException(error); ErrorMessage = error.Message; }
        }
        private void Start()
        {
            try
            {
                // The scene's GameBootstrap.Awake has claimed its host before this idempotent call.
                GameBootstrap.Initialize();
                if (GameBootstrap.LastInitializationError != null) throw GameBootstrap.LastInitializationError;
                m_lifetime = CancellationTokenSource.CreateLinkedTokenSource(RuntimeHost.LifetimeToken);
                UIRouter.Configure(Services.Resources, Facade.GetInstance(() => new Facade()));
                // The sample uses landscape; configure the existing layer scalers through their public Unity components.
                foreach (var scaler in UIRoot.Instance.GetComponentsInChildren<CanvasScaler>(true))
                { scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f; }
                Flow = new FlowController(new IFlowState[] {
                    new State("Boot", BootAsync), new State("MainMenu", MenuAsync), new State("Run", RunAsync),
                    new State("Results", ResultsAsync), new State("Error", ErrorAsync) });
                m_exit = new ApplicationExitCoordinator(() => { Ready = false; QuitRequested?.Invoke(); Application.Quit(); });
                m_exit.Register(async ct => { await StopRunAsync(); await Services.Settings.SaveAsync(ct); });
                Observe(InitializeAsync());
            }
            catch (Exception error) { Debug.LogException(error); ErrorMessage = error.Message; }
        }
        private async Task InitializeAsync()
        {
            await SwitchAsync("Boot"); await SwitchAsync("MainMenu"); Ready = true;
        }
        private async Task BootAsync(FlowContext context, CancellationToken ct)
        {
            var handle = await Services.Resources.LoadAsync<SurvivorSpriteSet>(new AssetKey(ArtKey), ct);
            try { ct.ThrowIfCancellationRequested(); Services.Art = handle.Asset; Services.Scope.Track(handle); }
            catch { handle.Dispose(); throw; }
            var config = await ConfigService.LoadAsync(Services.Resources, new AssetKey(Services.Art.CatalogKey), ct);
            try { ct.ThrowIfCancellationRequested(); Services.Config = Services.Scope.Track(config); }
            catch { config.Dispose(); throw; }
            Services.Input = new InputContextService(Services.Art.Input);
            var overrides = Services.Settings.Current.BindingOverrides;
            if (!string.IsNullOrEmpty(overrides))
            {
                try { Services.Input.LoadBindingOverrides(overrides); }
                catch (Exception error) { Status = "Saved bindings could not be loaded: " + error.Message; }
            }
            var events = GetComponentInChildren<EventSystem>();
            if (events == null) throw new InvalidOperationException("Bootstrap requires an EventSystem");
            Services.UIInput = new UIInputCoordinator(Services.Input, events, Services.Art.InputProfile);
            UIRouter.PageOpened += RefreshHudPointers;
            UIRouter.PageClosed += RefreshHudPointers;
            m_movement = Services.Input.Actions.FindAction("Gameplay/Move", true);
            m_pauseInput = Services.Input.Subscribe("Gameplay/Pause", _ => { if (Session != null && !IsBusy) Observe(OpenPauseAsync()); });
            var history = await Services.Saves.LoadAsync("profile", ct);
            BackupAvailable = history.BackupAvailable;
            if (history.Status == SaveLoadStatus.Loaded || history.Status == SaveLoadStatus.Migrated) History = history.Data;
            else if (history.Status != SaveLoadStatus.Missing)
            { m_saveWritable = false; Status = "History could not be loaded. Your existing file has been preserved. Open Settings to recover a backup."; }
            if (Services.Art.Music != null) Services.Audio.PlayMusic(Services.Art.Music);
        }
        private async Task MenuAsync(FlowContext context, CancellationToken ct)
        {
            await StopRunAsync(); UIRouter.CloseAll();
            Services.UIInput.SetBaseContext("UI");
            await OpenAsync(SurvivorPageKind.Menu);
        }
        private async Task RunAsync(FlowContext context, CancellationToken ct)
        {
            UIRouter.CloseAll(); Services.UIInput.SetBaseContext("UI");
            m_loading = await OpenAsync(SurvivorPageKind.Loading);
            try
            {
                var result = await GameBootstrap.Scene.LoadAsync(new SceneLoadRequest(ArenaPath, LoadSceneMode.Additive)
                    .WithInitializer(new ArenaInitializer(this)).WithCancellation(ct).WithReadyTimeout(30));
                if (result.Status != SceneLoadStatus.Success) throw new InvalidOperationException("Arena load: " + result.Status + " " + result.Error?.Message);
                m_arena = result.Context;
                UIRouter.Close(m_loading); m_loading = null;
                Services.UIInput.SetBaseContext("Gameplay");
                m_hud = await OpenAsync(SurvivorPageKind.Hud);
                RefreshHudPointers(m_hud);
            }
            catch { await StopRunAsync(); throw; }
        }
        private async Task ResultsAsync(FlowContext context, CancellationToken ct)
        {
            UIRouter.CloseAll(); Services.UIInput.SetBaseContext("UI");
            Status = "Your watch has been recorded.";
            if (LastRun != null)
            {
                History.Include(LastRun);
                if (m_saveWritable)
                {
                    try { await Services.Saves.SaveAsync("profile", History, ct); BackupAvailable = true; }
                    catch (Exception error) { Status = "History save failed: " + error.Message; }
                }
                else Status = "History is available for this session. Existing unreadable save was preserved.";
            }
            await OpenAsync(SurvivorPageKind.Results);
        }
        private async Task ErrorAsync(FlowContext context, CancellationToken ct)
        {
            UIRouter.CloseAll(); await StopRunAsync();
            Services.UIInput?.SetBaseContext("UI"); await OpenAsync(SurvivorPageKind.Error);
        }
        private async Task SwitchAsync(string id)
        {
            var result = await Flow.SwitchAsync(id, m_lifetime.Token);
            if (result.Status != FlowStatus.Success) throw new InvalidOperationException("Flow " + id + ": " + result.Status, result.Error);
        }
        private sealed class State : IFlowState
        {
            private readonly Func<FlowContext, CancellationToken, Task> m_enter;
            public string Id { get; }
            public State(string id, Func<FlowContext, CancellationToken, Task> enter) { Id = id; m_enter = enter; }
            public IAwaitable EnterAsync(FlowContext c, CancellationToken ct) => m_enter(c, ct).AsAwaitable();
            public void Exit(FlowContext c) { }
        }
        private sealed class ArenaInitializer : ISceneInitializer
        {
            private readonly SurvivorGame m_game;
            public ArenaInitializer(SurvivorGame game) { m_game = game; }
            public IAwaitable InitializeAsync(SceneContext context, CancellationToken ct) => Initialize(context, ct).AsAwaitable();
            private async Task Initialize(SceneContext context, CancellationToken ct)
            {
                m_game.m_arena = context;
                var session = await SurvivorRunSession.CreateAsync(m_game.Services.Resources, m_game.Services.Config,
                    m_game.Services.Art, m_game.Services.Pause, context, m_game.Services.Play, ct);
                try { ct.ThrowIfCancellationRequested(); context.Resources.Track(session); m_game.Session = session; session.Finished += m_game.OnFinished; }
                catch { session.Dispose(); throw; }
            }
        }
        private void Update()
        {
            if (!Ready || m_destroyed) return;
            if (!IsBusy && Session != null)
            {
                if (!m_choiceOpening && Session.ChoicePending) Observe(OpenChoiceAsync());
                Session?.Tick(Time.deltaTime, m_movement.ReadValue<Vector2>());
            }
            m_hudTimer -= Time.unscaledDeltaTime;
            if (m_hudTimer <= 0) { m_hudTimer = .1f; Services.Events.Publish(new SurvivorUiChanged(Session?.RunId)); }
        }
        private void OnFinished(RunSummary result)
        {
            LastRun = result;
            Observe(EndRunAsync());
        }
        private async Task EndRunAsync()
        {
            if (m_busy) return;
            m_busy = true;
            try { await StopRunAsync(); await SwitchAsync("Results"); }
            finally { m_busy = false; }
        }
        public async Task StopRunAsync()
        {
            if (Session != null) { Session.Stop(); Session.Finished -= OnFinished; }
            if (m_modal?.IsValid == true) UIRouter.Close(m_modal); m_modal = null;
            if (m_hud?.IsValid == true) UIRouter.Close(m_hud); m_hud = null;
            if (m_arena != null && m_arena.IsAlive)
            {
                var result = await GameBootstrap.Scene.UnloadAsync(m_arena);
                if (result.Status != SceneLoadStatus.Success) throw new InvalidOperationException("Arena unload: " + result.Status);
            }
            Session?.Dispose(); Session = null; m_arena = null; m_choices = null; m_choiceOpening = false;
        }
        private async Task<UIPageHandle> OpenAsync(SurvivorPageKind kind, Action closed = null, string[] choices = null)
        {
            return await UIRouter.OpenAsync(SurvivorPages.Get(kind), new SurvivorPageArgs { Game = this, Closed = closed, Choices = choices });
        }
        private async Task OpenPauseAsync()
        {
            if (m_modalOpening || m_modal?.IsValid == true || Session == null || m_choiceOpening || Session.Stopped) return;
            m_modalOpening = true;
            var token = Services.Pause.Acquire("survivor/pause");
            try { m_modal = await OpenAsync(SurvivorPageKind.Pause, () => { token.Dispose(); m_modal = null; }); }
            catch { token.Dispose(); throw; }
            finally { m_modalOpening = false; }
        }
        private async Task OpenChoiceAsync()
        {
            if (m_modalOpening || m_choiceOpening || m_modal?.IsValid == true || Session == null || Session.Stopped) return;
            m_choiceOpening = true;
            var token = Services.Pause.Acquire("survivor/upgrade");
            try
            {
                m_choices = Session.GetChoices().ToArray();
                if (m_choices.Length == 0) { Session.ResolveFullDeck(); m_choiceOpening = false; token.Dispose(); return; }
                m_modal = await OpenAsync(SurvivorPageKind.Upgrade, () => { token.Dispose(); m_modal = null; m_choiceOpening = false; }, m_choices);
            }
            catch { token.Dispose(); m_choiceOpening = false; throw; }
        }
        public void RunAction(string action, SurvivorPageView page) => Observe(HandleActionAsync(action, page));
        private async Task HandleActionAsync(string action, SurvivorPageView page)
        {
            if (action.StartsWith("Btn_Choice", StringComparison.Ordinal))
            {
                var index = int.Parse(action.Substring("Btn_Choice".Length));
                if (Session != null && m_choices != null && index < m_choices.Length && Session.SelectUpgrade(m_choices[index]))
                { m_choices = null; page.Close(); }
                return;
            }
            if (action == "Btn_Close" || action == "Btn_Resume") { page.Close(); return; }
            if (action == "Btn_Pause") { await OpenPauseAsync(); return; }
            if (action == "Btn_Settings")
            {
                if (m_settingsOpening || UIRouter.IsOpen("SurvivorSettings")) return;
                m_settingsOpening = true;
                var token = Session != null ? Services.Pause.Acquire("survivor/settings") : null;
                try { await OpenAsync(SurvivorPageKind.Settings, () => { Services.Input.CancelRebinding(); token?.Dispose(); }); }
                catch { token?.Dispose(); throw; }
                finally { m_settingsOpening = false; }
                return;
            }
            if (action == "Btn_Rebind")
            {
                Status = "Press a key for Move Up. Escape cancels.";
                Services.Input.StartRebinding("Gameplay/Move", 1, success => { Status = success ? "Binding changed. Save to keep it." : "Binding cancelled."; }, "<Keyboard>/escape");
                return;
            }
            if (action == "Btn_ResetBindings") { Services.Input.ResetBindingOverrides(); Status = "Default bindings restored. Save to keep them."; return; }
            if (action == "Btn_Save")
            {
                if (m_settingsSaving) return;
                m_settingsSaving = true;
                Status = "Saving settings...";
                try
                {
                    Services.Settings.Apply(value => value.BindingOverrides = Services.Input.SaveBindingOverrides());
                    await Services.Settings.SaveAsync(m_lifetime.Token); Status = "Settings saved.";
                }
                catch (Exception error) { Status = "Could not save settings: " + error.Message; }
                finally { m_settingsSaving = false; }
                return;
            }
            if (action == "Btn_Recover")
            {
                if (m_recovering) return;
                m_recovering = true;
                try
                {
                    var result = await Services.Saves.RecoverBackupAsync("profile", m_lifetime.Token);
                    if (result.Status == SaveLoadStatus.Loaded || result.Status == SaveLoadStatus.Migrated)
                    { History = result.Data; m_saveWritable = true; Status = "History backup recovered."; }
                    else Status = "Recovery failed: " + result.Status;
                }
                catch (Exception error) { Status = "Recovery failed: " + error.Message; }
                finally { m_recovering = false; }
                return;
            }
            if (IsBusy) return;
            m_busy = true;
            try
            {
                switch (action)
                {
                    case "Btn_Start": case "Btn_Retry": await StopRunAsync(); await SwitchAsync("Run"); break;
                    case "Btn_Menu": await StopRunAsync(); await SwitchAsync("MainMenu"); break;
                    case "Btn_Abandon":
                        Session?.End(false, "You left the watch before dawn.");
                        await StopRunAsync(); await SwitchAsync("Results"); break;
                    case "Btn_Quit": await m_exit.RequestExitAsync(m_lifetime.Token); break;
                }
            }
            finally { m_busy = false; }
        }
        public void ChangeVolume(string field, float value) => Services.Settings.Apply(settings =>
        { if (field == "Slider_Music") settings.Music = value; else if (field == "Slider_Effects") settings.Effects = value; else settings.Master = value; });
        private void RefreshHudPointers(UIPageHandle page)
        {
            var actions = Services?.Input?.Actions;
            if (actions == null || !actions.FindActionMap("Gameplay").enabled) return;
            // Keep clickable HUD controls alongside gameplay without enabling UI/Cancel on the same Escape press.
            actions.FindAction("UI/Point", true).Enable();
            actions.FindAction("UI/Click", true).Enable();
        }
        private void Observe(Task operation) { m_operation = ObserveAsync(operation); }
        private async Task ObserveAsync(Task operation)
        {
            try { await operation; }
            catch (OperationCanceledException) when (m_destroyed || (m_lifetime?.IsCancellationRequested ?? false)) { }
            catch (Exception error)
            {
                if (m_destroyed) return;
                Debug.LogException(error); ErrorMessage = error.GetBaseException().Message;
                if (Services?.Art == null) return;
                try { await SwitchAsync("Error"); }
                catch (Exception nested) { Debug.LogException(nested); }
            }
        }
        private void OnDestroy()
        {
            if (m_destroyed) return; m_destroyed = true; Ready = false;
            m_lifetime?.Cancel(); m_pauseInput?.Dispose();
            UIRouter.PageOpened -= RefreshHudPointers; UIRouter.PageClosed -= RefreshHudPointers;
            Session?.Dispose(); Session = null;
            Flow?.Dispose();
            foreach (SurvivorPageKind kind in Enum.GetValues(typeof(SurvivorPageKind))) UIRouter.CancelOpen(SurvivorPages.Get(kind).PageId);
            UIRouter.CloseAll(); m_exit?.Dispose(); QuitRequested = null;
            GameBootstrap.Shutdown(); m_lifetime?.Dispose();
        }
    }
}
