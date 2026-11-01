// Copied only into a disposable consumer project's Assets/Editor. Manual verification, no CI.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yato.App;
using Yato.Core;
using Yato.FullSample;
using Yato.Resources;

namespace Yato
{
    [InitializeOnLoad]
    public static class LifecycleVerification
    {
        private const string Prefix = "Yato.06.Lifecycle.";
        private static double s_deadline;
        private static IAwaitable<Yato.Flow.FlowResult> s_stopping;
        private static int s_ticks;
        static LifecycleVerification() { Hook(); }
        private static void Hook()
        {
            EditorApplication.playModeStateChanged -= Changed;
            EditorApplication.playModeStateChanged += Changed;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }
        public static void Run()
        {
            if (SessionState.GetBool(Prefix + "running", false)) throw new InvalidOperationException("Lifecycle verification is already running");
            SessionState.SetBool(Prefix + "oldEnabled", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(Prefix + "oldOptions", (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetInt(Prefix + "index", 0);
            SessionState.SetString(Prefix + "unexpectedError", "");
            SessionState.SetBool(Prefix + "running", true);
            EditorSceneManager.OpenScene("Assets/Samples/YatoFramework/FullSample/Scenes/YatoFull.unity");
            Hook();
            SessionState.SetString(Prefix + "phase", "settling");
            s_deadline = EditorApplication.timeSinceStartup + 3;
        }
        private static void Begin()
        {
            int index = SessionState.GetInt(Prefix + "index", 0);
            if (index >= 8) { Finish(null); return; }
            // Two entries per configuration, to catch subscriptions/state surviving the first run.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)(index / 2);
            SessionState.SetString(Prefix + "phase", "entering");
            s_deadline = EditorApplication.timeSinceStartup + 30;
            EditorApplication.isPlaying = true;
        }
        private static void Changed(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Prefix + "running", false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                s_ticks = 0; RuntimeHost.Updated += CountTick;
                SessionState.SetString(Prefix + "phase", "running");
                s_deadline = EditorApplication.timeSinceStartup + 30;
            }
            if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetString(Prefix + "phase", "") == "exiting")
            {
                int index = SessionState.GetInt(Prefix + "index", 0);
                Debug.Log("[Yato.Lifecycle] PASS cycle=" + index + " options=" + (index / 2));
                SessionState.SetInt(Prefix + "index", index + 1);
                EditorApplication.delayCall += Begin;
            }
        }
        private static void CountTick(float delta) { s_ticks++; }
        private static void OnLog(string condition, string stack, LogType type)
        {
            if (SessionState.GetBool(Prefix + "running", false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) &&
                string.IsNullOrEmpty(SessionState.GetString(Prefix + "unexpectedError", "")))
                SessionState.SetString(Prefix + "unexpectedError", condition + "\n" + stack);
        }
        private static void Update()
        {
            if (!SessionState.GetBool(Prefix + "running", false)) return;
            string phase = SessionState.GetString(Prefix + "phase", "");
            if (phase == "settling") { if (EditorApplication.timeSinceStartup >= s_deadline && !EditorApplication.isUpdating && !EditorApplication.isCompiling) Begin(); return; }
            if (phase != "running" && phase != "stopping") return;
            try
            {
                if (EditorApplication.timeSinceStartup > s_deadline) throw new TimeoutException("Lifecycle phase timed out: " + phase);
                if (phase == "running")
                {
                    var sample = UnityEngine.Object.FindAnyObjectByType<FullSampleEntry>();
                    if (sample?.DemoPool == null || s_ticks < 10) return;
                    if (!GameBootstrap.IsInitialized) throw new InvalidOperationException("Bootstrap did not initialize");
                    if (UnityEngine.Object.FindObjectsByType<RuntimeHost>(FindObjectsSortMode.None).Length != 1)
                        throw new InvalidOperationException("Duplicate RuntimeHost after entering Play");
                    if (RuntimeHost.LifetimeToken.IsCancellationRequested) throw new InvalidOperationException("Host lifetime was not renewed");
                    s_stopping = sample.StopFoundationDemo();
                    SessionState.SetString(Prefix + "phase", "stopping");
                }
                else if (s_stopping != null && s_stopping.IsDone)
                {
                    var result = s_stopping.GetResult();
                    if (result.Status != Yato.Flow.FlowStatus.Success) throw new InvalidOperationException("Flow exit failed: " + result.Error);
                    if (ResourceTracker.Count != 0) throw new InvalidOperationException("Held resources after flow exit: " + ResourceTracker.Count);
                    if (DiagnosticRegistry.Capture().Any(x => x.Module == "Pooling")) throw new InvalidOperationException("Disposed pool remained registered");
                    RuntimeHost.Updated -= CountTick;
                    SessionState.SetString(Prefix + "phase", "exiting");
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            string unexpected = SessionState.GetString(Prefix + "unexpectedError", "");
            if (error == null && !string.IsNullOrEmpty(unexpected)) error = new InvalidOperationException("Unexpected Editor error (" + SessionState.GetInt(Prefix + "index", 0) + " cycles completed): " + unexpected);
            SessionState.SetBool(Prefix + "running", false);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Prefix + "oldEnabled", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(Prefix + "oldOptions", 0);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/Foundation-06-Lifecycle.txt", error == null ? "PASS: 8 Play sessions; all Domain/Scene Reload combinations, zero resource baseline" : "FAIL: " + error);
            if (error == null) Debug.Log("[Yato.Lifecycle] PASS: 8 Play sessions across all Domain/Scene Reload configurations");
            else Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
