using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Yato.Persistence;

namespace Yato.Save.CounterSample
{
    [Serializable] public sealed class CounterData { public int Count; }
    public sealed class SaveCounterSample : MonoBehaviour
    {
        private SaveService<CounterData> m_saves;
        private readonly CancellationTokenSource m_lifetime = new CancellationTokenSource();
        private CounterData m_data = new CounterData();
        private string m_message = "Ready";
        private bool m_busy;
        private void Awake() { m_saves = new SaveService<CounterData>(new FileStore(), 1, d => d.Count < 0 ? "Count must be nonnegative" : null, directory: "yato-counter-demo"); }
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20, 20, 380, 350), GUI.skin.box);
            GUILayout.Label("Optional save module: counter slots");
            GUILayout.Label("Count: " + m_data.Count + " / " + m_message);
            GUI.enabled = !m_busy;
            if (GUILayout.Button("Increment (memory only)")) m_data.Count++;
            if (GUILayout.Button("Save slot demo")) Run(async () => { await m_saves.SaveAsync("demo", m_data, m_lifetime.Token); return "Saved"; });
            if (GUILayout.Button("Load slot demo")) Run(async () => { var r = await m_saves.LoadAsync("demo", m_lifetime.Token); if (r.Data != null) m_data = r.Data; return r.Status + " / backup=" + r.BackupAvailable; });
            if (GUILayout.Button("Explicitly recover backup")) Run(async () => { var r = await m_saves.RecoverBackupAsync("demo", m_lifetime.Token); if (r.Data != null) m_data = r.Data; return r.Status.ToString(); });
            if (GUILayout.Button("Explicitly delete slot demo")) Run(async () => { await m_saves.DeleteAsync("demo", m_lifetime.Token); return "Deleted"; });
            GUI.enabled = true; GUILayout.EndArea();
        }
        private async void Run(Func<Task<string>> operation)
        {
            m_busy = true;
            try { string message = await operation(); if (this != null) m_message = message; }
            catch (Exception error) { if (this != null) m_message = error.Message; }
            finally { m_busy = false; }
        }
        private void OnDestroy() { m_lifetime.Cancel(); m_lifetime.Dispose(); }
    }
}
