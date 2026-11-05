using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Yato.UI;
namespace Yato.Survivor
{
    public class SurvivorPageView : UIPageView
    {
        public SurvivorPageKind Kind;
        private ComponentBinder m_binder;
        private SurvivorPageArgs m_args;
        private Action m_closed;
        private readonly List<Action> m_removeListeners = new List<Action>();
        public event Action<string> ActionRequested;
        public event Action<string, float> VolumeChanged;
        protected override void Awake() { m_binder = GetComponent<ComponentBinder>(); }
        public override void OnOpen(object args)
        {
            RemoveListeners();
            m_args = (SurvivorPageArgs)args; m_closed = m_args.Closed;
            foreach (var entry in m_binder.Bindings)
            {
                if (entry.Target is Button button)
                {
                    var action = entry.Name;
                    UnityAction listener = () => ActionRequested?.Invoke(action);
                    button.onClick.AddListener(listener);
                    m_removeListeners.Add(() => { if (button != null) button.onClick.RemoveListener(listener); });
                }
                if (entry.Target is Slider slider)
                {
                    var field = entry.Name;
                    var settings = m_args.Game.Services.Settings.Current;
                    slider.SetValueWithoutNotify(field == "Slider_Music" ? settings.Music : field == "Slider_Effects" ? settings.Effects : settings.Master);
                    UnityAction<float> listener = value => VolumeChanged?.Invoke(field, value);
                    slider.onValueChanged.AddListener(listener);
                    m_removeListeners.Add(() => { if (slider != null) slider.onValueChanged.RemoveListener(listener); });
                }
            }
            Render();
        }
        private void Text(string key, string value) { if (m_binder.TryGet<Text>(key, out var target)) target.text = value; }
        private void Fill(string key, float value) { if (m_binder.TryGet<Image>(key, out var target)) target.fillAmount = Mathf.Clamp01(value); }
        private void Active(string key, bool active) { if (m_binder.TryGet<Button>(key, out var button)) button.gameObject.SetActive(active); }
        public void Render()
        {
            if (m_args?.Game == null) return;
            var game = m_args.Game; var session = game.Session;
            if (Kind == SurvivorPageKind.Menu)
            {
                Text("Txt_History", $"{game.History.Runs} EXPEDITIONS  /  {game.History.Victories} VICTORIES\nBEST  {TimeText(game.History.BestSeconds)}   /   {game.History.TotalKills} TOTAL KILLS");
                Text("Txt_Status", game.Status);
            }
            if (Kind == SurvivorPageKind.Hud && session != null)
            {
                Text("Txt_Time", TimeText(session.Progress.Elapsed));
                Text("Txt_Stage", session.Progress.Elapsed >= 480 ? "THE FINAL WATCH" : session.Progress.Elapsed >= 240 ? "THE HORDE RISES" : "HOLD UNTIL DAWN");
                Text("Txt_Level", "LV " + session.Progress.Level);
                Text("Txt_Health", $"{Mathf.CeilToInt(session.Health)} / {session.MaximumHealth:0}");
                Text("Txt_Kills", session.Progress.Kills.ToString("0000")); Text("Txt_Coins", session.Progress.Coins.ToString("000"));
                Fill("Img_Health", session.Health / session.MaximumHealth);
                Fill("Img_Experience", (float)session.Progress.Experience / session.Progress.NextLevelExperience);
                Text("Txt_Weapons", $"SWORD  {session.Progress.Rank("sword")}    BOW  {session.Progress.Rank("bow")}    ARCANE  {session.Progress.Rank("staff")}");
            }
            if (Kind == SurvivorPageKind.Upgrade && session != null)
            {
                Text("Txt_Title", session.TreasureChoice ? "A GIFT FROM THE DEPTHS" : "GROW STRONGER");
                Text("Txt_Subtitle", "Choose one blessing. The dungeon can wait.");
                for (var i = 0; i < 3; i++)
                {
                    var valid = m_args.Choices != null && i < m_args.Choices.Length;
                    Active("Btn_Choice" + i, valid); if (!valid) continue;
                    var row = game.Services.Config.Get<UpgradeRecord>(m_args.Choices[i]);
                    Text("Txt_Choice" + i, row.Title); Text("Txt_Description" + i, row.Description);
                    Text("Txt_Rank" + i, $"RANK {session.Progress.Rank(row.Id) + 1} / {row.Maximum}");
                    if (m_binder.TryGet<Image>("Img_Choice" + i, out var image)) image.sprite = game.Services.Art.Find(row.Icon).Frames[0];
                }
            }
            if (Kind == SurvivorPageKind.Results && game.LastRun != null)
            {
                var result = game.LastRun;
                Text("Txt_Title", result.Victory ? "DAWN BREAKS" : "THE LAST WATCH");
                Text("Txt_Subtitle", result.Reason);
                Text("Txt_ResultTime", TimeText(result.Seconds)); Text("Txt_ResultKills", result.Kills.ToString());
                Text("Txt_ResultLevel", result.Level.ToString()); Text("Txt_ResultCoins", result.Coins.ToString());
                Text("Txt_Status", game.Status);
            }
            if (Kind == SurvivorPageKind.Settings)
            {
                Text("Txt_Status", game.Status);
                Active("Btn_Recover", game.BackupAvailable);
            }
            if (Kind == SurvivorPageKind.Error) Text("Txt_Status", game.ErrorMessage);
        }
        public override void OnClose(bool destroy)
        {
            RemoveListeners();
            m_args = null; var closed = m_closed; m_closed = null; closed?.Invoke();
        }
        private void RemoveListeners()
        {
            foreach (var remove in m_removeListeners) remove();
            m_removeListeners.Clear();
        }
        public static string TimeText(float seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";
    }
}
