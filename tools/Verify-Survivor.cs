using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Yato.Survivor;
using Yato.UI;

public static class VerifySurvivor
{
    private static SurvivorGame Game => UnityEngine.Object.FindAnyObjectByType<SurvivorGame>();
    private static SurvivorPageView Page(SurvivorPageKind kind) => UnityEngine.Object.FindObjectsByType<SurvivorPageView>().FirstOrDefault(p => p.Kind == kind);
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Click(SurvivorPageKind page, string button)
    {
        var view = Page(page); Check(view != null, "Missing page: " + page);
        view.GetComponent<ComponentBinder>().Get<Button>(button).onClick.Invoke();
    }
    private static async Task Until(Func<bool> condition, string reason)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(reason + ": " + Game?.ErrorMessage); await Task.Delay(15); }
    }
    public static async Task<string> Lifecycle()
    {
        await Until(() => Game?.Ready == true, "boot");
        var baseline = 0;
        for (var i = 0; i < 100; i++)
        {
            if (Game.Session == null)
            {
                var menu = Page(SurvivorPageKind.Menu);
                Click(menu != null ? SurvivorPageKind.Menu : SurvivorPageKind.Results, menu != null ? "Btn_Start" : "Btn_Retry");
            }
            await Until(() => Game.Session != null && !Game.IsBusy && Game.Flow.CurrentStateId == "Run", "start run");
            var session = Game.Session;
            if (i == 0)
            {
                Click(SurvivorPageKind.Hud, "Btn_Pause");
                Click(SurvivorPageKind.Hud, "Btn_Pause");
                await Until(() => Page(SurvivorPageKind.Pause) != null, "pause");
                Check(Game.Services.Pause.PauseCount == 1, "Repeated pause leaked a token: " + Game.Services.Pause.PauseCount);
                var elapsed = session.Progress.Elapsed; await Task.Delay(100);
                Check(session.Progress.Elapsed == elapsed, "Paused game advanced");
                Click(SurvivorPageKind.Pause, "Btn_Resume");
                await Until(() => Game.Services.Pause.PauseCount == 0, "resume");
                session.Progress.AddExperience(50);
                while (session.Progress.PendingLevels > 0)
                {
                    await Until(() => Page(SurvivorPageKind.Upgrade) != null, "upgrade");
                    Check(Game.Services.Pause.IsPaused, "Upgrade did not pause");
                    Click(SurvivorPageKind.Upgrade, "Btn_Choice0"); await Task.Delay(25);
                }
            }
            session.End(false, "Lifecycle verification");
            await Until(() => Game.Session == null && !Game.IsBusy && Game.Flow.CurrentStateId == "Results", "end run");
            Check(Game.Services.Pause.PauseCount == 0, "Pause leak");
            Check(!session.Progress.ConsumeLevel(), "Ended run changed");
            Check(UnityEngine.SceneManagement.SceneManager.sceneCount == 1, "Arena scene leak");
            var assets = Game.Services.Resources.Stats.ActiveAssetCount;
            if (i == 0) baseline = assets;
            Check(assets == baseline, "Asset baseline grew at iteration " + i + ": " + assets);
            if (i % 10 == 0) File.WriteAllText("Logs/Survivor-Lifecycle-Progress.json", "{\"completed\":" + (i + 1) + "}");
        }
        var report = "{\"cycles\":100,\"pauseAndUpgrades\":true,\"sceneLeaks\":0,\"stableAssetCount\":" + baseline + "}";
        File.WriteAllText("Logs/Survivor-Lifecycle-Verification.json", report);
        return report;
    }
    public static async Task<string> FullCombat()
    {
        await Until(() => Game?.Ready == true && !Game.IsBusy, "boot");
        if (Game.Session == null)
        {
            var menu = Page(SurvivorPageKind.Menu);
            Click(menu != null ? SurvivorPageKind.Menu : SurvivorPageKind.Results, menu != null ? "Btn_Start" : "Btn_Retry");
        }
        await Until(() => Game.Session != null && !Game.IsBusy, "run");
        var session = Game.Session;
        var ticks = 0;
        var enemies = (System.Collections.Generic.List<SurvivorEntity>)typeof(SurvivorRunSession).GetField("m_enemies", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(session);
        var pickups = (System.Collections.Generic.List<SurvivorEntity>)typeof(SurvivorRunSession).GetField("m_pickups", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(session);
        var preference = new[] { "bow", "staff", "haste", "might", "sword", "swift", "vitality", "magnet", "multishot", "ward" };
        while (!session.Stopped)
        {
            if (session.ChoicePending)
            {
                await Until(() => Page(SurvivorPageKind.Upgrade) != null, "upgrade popup");
                var binder = Page(SurvivorPageKind.Upgrade).GetComponent<ComponentBinder>();
                var selected = 0; var best = int.MaxValue;
                for (var n = 0; n < 3; n++)
                {
                    if (!binder.Get<Button>("Btn_Choice" + n).gameObject.activeSelf) continue;
                    var title = binder.Get<Text>("Txt_Choice" + n).text;
                    for (var priority = 0; priority < preference.Length; priority++)
                        if (Game.Services.Config.Get<UpgradeRecord>(preference[priority]).Title == title)
                        {
                            var id = preference[priority];
                            var score = id == "bow" && session.Progress.Rank(id) == 0 ? -5 : id == "staff" && session.Progress.Rank(id) == 0 ? -4 :
                                id == "vitality" && session.Health < session.MaximumHealth * .65f ? -6 : id == "swift" && session.Progress.Rank(id) < 2 ? -3 :
                                id == "haste" ? 0 : id == "might" ? 1 : id == "sword" ? 2 : priority + 3;
                            if (score < best) { best = score; selected = n; }
                        }
                }
                Click(SurvivorPageKind.Upgrade, "Btn_Choice" + selected); await Task.Delay(15); continue;
            }
            for (var n = 0; n < 120 && !session.Stopped && !session.ChoicePending; n++)
            {
                var t = session.Progress.Elapsed;
                var destination = new Vector2(Mathf.Cos(t * .35f) * 9, Mathf.Sin(t * .35f) * 7);
                var origin = session.PlayerPosition;
                var nearest = 2.25f;
                foreach (var pickup in pickups)
                {
                    var distance = ((Vector2)pickup.transform.position - origin).sqrMagnitude;
                    if ((int)pickup.Damage == 2 && session.Health < session.MaximumHealth * .8f) distance *= .1f;
                    if (distance < nearest) { nearest = distance; destination = pickup.transform.position; }
                }
                var movement = (destination - origin).normalized;
                var avoid = Vector2.zero;
                foreach (var enemy in enemies)
                {
                    var offset = origin - (Vector2)enemy.transform.position;
                    if (offset.sqrMagnitude < 2.25f) avoid += offset.normalized * (1.5f - offset.magnitude) * 1.2f;
                }
                movement = (movement + avoid).normalized;
                session.Tick(.05f, movement); ticks++;
            }
            await Task.Delay(1);
        }
        await Until(() => Game.Session == null && !Game.IsBusy && Game.Flow.CurrentStateId == "Results", "results");
        var result = Game.LastRun;
        var report = Newtonsoft.Json.JsonConvert.SerializeObject(new { result, ticks, bundle = Game.ForceBundles, simulation = "accelerated actual session ticks, actual routed upgrade buttons, no health/damage cheats" }, Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText("Logs/Survivor-Full-Combat.json", report);
        Check(result.Victory, "Combat bot died after " + result.Seconds + "s with " + result.Kills + " kills");
        return report;
    }

    public static async Task<string> SettingsAndInput()
    {
        await Until(() => Game?.Ready == true && !Game.IsBusy, "boot");
        if (Page(SurvivorPageKind.Results) != null)
        { Click(SurvivorPageKind.Results, "Btn_Menu"); await Until(() => Page(SurvivorPageKind.Menu) != null && !Game.IsBusy, "menu"); }
        Click(SurvivorPageKind.Menu, "Btn_Settings");
        await Until(() => Page(SurvivorPageKind.Settings) != null, "settings");
        var settings = Page(SurvivorPageKind.Settings).GetComponent<ComponentBinder>();
        settings.Get<Slider>("Slider_Master").value = .42f;
        Check(Mathf.Abs(Game.Services.Settings.Current.Master - .42f) < .001f, "Slider did not apply settings");
        Click(SurvivorPageKind.Settings, "Btn_Save"); await Until(() => Game.Status == "Settings saved.", "settings save");
        Game.Services.Settings.Load(); Check(Mathf.Abs(Game.Services.Settings.Current.Master - .42f) < .001f, "Settings round trip");
        Click(SurvivorPageKind.Settings, "Btn_Rebind");
        var rebindKeyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
        try
        {
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(rebindKeyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.I));
            UnityEngine.InputSystem.InputSystem.Update();
            await Until(() => Game.Status.StartsWith("Binding changed"), "rebind completion");
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(rebindKeyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            UnityEngine.InputSystem.InputSystem.Update();
            Click(SurvivorPageKind.Settings, "Btn_Save"); await Until(() => Game.Status == "Settings saved.", "save bindings");
            var saved = Game.Services.Settings.Current.BindingOverrides;
            Check(!string.IsNullOrEmpty(saved), "Bindings were not serialized");
            Game.Services.Input.ResetBindingOverrides(); Game.Services.Input.LoadBindingOverrides(saved);
            Check(Game.Services.Input.Actions.FindAction("Gameplay/Move").bindings[1].effectivePath == "<Keyboard>/i", "Binding round trip failed");
            Check(Game.Services.Art.Input.FindAction("Gameplay/Move").bindings[1].overridePath == null, "Source actions were mutated");
            Click(SurvivorPageKind.Settings, "Btn_ResetBindings");
            Click(SurvivorPageKind.Settings, "Btn_Save"); await Until(() => Game.Status == "Settings saved.", "reset saved bindings");
        }
        finally { UnityEngine.InputSystem.InputSystem.RemoveDevice(rebindKeyboard); }
        Click(SurvivorPageKind.Settings, "Btn_Close");
        Click(SurvivorPageKind.Menu, "Btn_Start"); await Until(() => Game.Session != null && !Game.IsBusy, "run");
        var input = Game.Services.Input.Actions;
        Check(input.FindAction("Gameplay/Move").enabled, "Gameplay input disabled");
        Check(input.FindAction("UI/Point").enabled && input.FindAction("UI/Click").enabled, "HUD pointer actions disabled");
        Check(!input.FindAction("UI/Cancel").enabled, "UI cancel must not share the gameplay Escape event");
        var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
        try
        {
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
            UnityEngine.InputSystem.InputSystem.Update();
            Check(input.FindAction("Gameplay/Move").ReadValue<Vector2>().y > .9f, "Move binding did not receive keyboard input");
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            UnityEngine.InputSystem.InputSystem.Update();
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Escape));
            UnityEngine.InputSystem.InputSystem.Update();
            await Task.Delay(100);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            UnityEngine.InputSystem.InputSystem.Update();
            await Until(() => Page(SurvivorPageKind.Pause) != null, "Escape pause");
            Check(Game.Services.Pause.PauseCount == 1, "Escape pause token count");
            await Task.Delay(80);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Escape));
            UnityEngine.InputSystem.InputSystem.Update(); await Task.Delay(80);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            UnityEngine.InputSystem.InputSystem.Update();
            await Until(() => Page(SurvivorPageKind.Pause) == null && Game.Services.Pause.PauseCount == 0, "Escape resume");
            Check(input.FindAction("UI/Click").enabled, "HUD pointer not restored");
        }
        finally { UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard); }
        Game.Session.Progress.AddExperience(8);
        await Until(() => Page(SurvivorPageKind.Upgrade) != null, "gamepad upgrade page");
        var oldBackground = UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior;
        var oldEditorInput = UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode;
        UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
        UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
        UnityEngine.InputSystem.InputSystem.EnableDevice(gamepad);
        try
        {
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(gamepad, new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.South));
            UnityEngine.InputSystem.InputSystem.Update();
            UnityEngine.EventSystems.EventSystem.current.currentInputModule.Process();
            await Task.Delay(100);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(gamepad, new UnityEngine.InputSystem.LowLevel.GamepadState());
            UnityEngine.InputSystem.InputSystem.Update();
            await Until(() => Page(SurvivorPageKind.Upgrade) == null && Game.Session.Progress.PendingLevels == 0, "gamepad submit");
            Check(Game.Services.Pause.PauseCount == 0, "Gamepad choice leaked pause");
        }
        finally
        {
            UnityEngine.InputSystem.InputSystem.RemoveDevice(gamepad);
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = oldBackground;
            UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
        }
        Game.Session.End(false, "Input verification"); await Until(() => Game.Session == null && !Game.IsBusy, "end");
        var saves = Game.Services.Saves;
        await saves.SaveAsync("recovery-test", new Yato.Survivor.Domain.SurvivorHistory { Runs = 1 });
        await saves.SaveAsync("recovery-test", new Yato.Survivor.Domain.SurvivorHistory { Runs = 2 });
        var store = new Yato.Persistence.FileStore(Game.DataRoot);
        store.WriteAllText("survivor/history/slot-recovery-test/primary.json", "not valid json");
        var corrupt = await saves.LoadAsync("recovery-test");
        Check(corrupt.Status == Yato.Save.SaveLoadStatus.Corrupt && corrupt.BackupAvailable, "Corrupt save classification / backup");
        var refused = false;
        try { await saves.SaveAsync("recovery-test", new Yato.Survivor.Domain.SurvivorHistory()); }
        catch (System.IO.InvalidDataException) { refused = true; }
        Check(refused, "Corrupt primary was overwritten");
        var recovered = await saves.RecoverBackupAsync("recovery-test");
        Check(recovered.Status == Yato.Save.SaveLoadStatus.Loaded && recovered.Data.Runs == 1, "Explicit recovery failed");
        await saves.DeleteAsync("recovery-test");
        const string report = "{\"settingsRoundTrip\":true,\"rebindRoundTrip\":true,\"sourceActionsUnchanged\":true,\"gamepadSubmit\":true,\"keyboardMove\":true,\"escapePauseResume\":true,\"hudPointer\":true,\"corruptSavePreserved\":true,\"explicitBackupRecovery\":true}";
        File.WriteAllText("Logs/Survivor-Settings-Input-Verification.json", report);
        return report;
    }

}
