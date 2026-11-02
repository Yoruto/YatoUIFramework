using System;
using Yato.Resources;
using Yato.UI;
namespace Yato.Survivor
{
    public enum SurvivorPageKind { Menu, Hud, Pause, Upgrade, Settings, Results, Error, Loading }
    public static class SurvivorPages
    {
        private static readonly UIPageDef[] Definitions = {
            Yato.Generated.UIPages.SurvivorMenu, Yato.Generated.UIPages.SurvivorHud, Yato.Generated.UIPages.SurvivorPause,
            Yato.Generated.UIPages.SurvivorUpgrade, Yato.Generated.UIPages.SurvivorSettings, Yato.Generated.UIPages.SurvivorResults,
            Yato.Generated.UIPages.SurvivorError, Yato.Generated.UIPages.SurvivorLoading };
        public static UIPageDef Get(SurvivorPageKind kind)
        {
            var definition = Definitions[(int)kind];
            definition.Layer = kind == SurvivorPageKind.Hud ? UILayer.Normal : kind == SurvivorPageKind.Loading || kind == SurvivorPageKind.Error ? UILayer.System : UILayer.Popup;
            return definition;
        }
    }
    public sealed class SurvivorPageArgs
    {
        public SurvivorGame Game;
        public Action Closed;
        public string[] Choices;
    }
    public class SurvivorPageProxy : UIPageProxy
    {
        public SurvivorPageProxy(string name) : base(name) { }
        public override void RefreshView() { (Context.View as SurvivorPageView)?.Render(); }
    }
    public class SurvivorMediator : UIPageMediator<SurvivorPageView>
    {
        private IDisposable m_subscription;
        private SurvivorGame m_game;
        public SurvivorMediator() { }
        public SurvivorMediator(string name) : base(name) { }
        protected override UIPageProxy CreateProxy() => new SurvivorPageProxy(Context.InstanceName + "Proxy");
        public override void OnOpen(object args)
        {
            base.OnOpen(args); Context.Proxy.BindModel(args);
            var game = ((SurvivorPageArgs)args).Game;
            m_game = game;
            View.ActionRequested += OnAction;
            View.VolumeChanged += OnVolume;
            m_subscription = game.Services.Events.Subscribe<SurvivorUiChanged>(evt =>
            { if (game.Session == null || evt.RunId == game.Session.RunId) Context.Proxy.RefreshView(); });
        }
        private void OnAction(string action) { m_game.RunAction(action, View); }
        private void OnVolume(string field, float value) { m_game.ChangeVolume(field, value); }
        public override void OnClose(bool destroy)
        {
            View.ActionRequested -= OnAction; View.VolumeChanged -= OnVolume;
            m_subscription?.Dispose(); m_subscription = null; m_game = null;
            base.OnClose(destroy);
        }
    }
}
