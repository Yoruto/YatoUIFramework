using System.Collections.Generic;
using NUnit.Framework;
using Yato.Resources;
using Yato.UI;

namespace Yato.Tests.EditMode
{
    public sealed class PageStackTests
    {
        private sealed class FakePage : IUIPage
        {
            public readonly List<string> Calls = new List<string>();

            public void OnCreate()
            {
                Calls.Add("Create");
            }

            public void OnOpen(object args)
            {
                Calls.Add("Open");
            }

            public void OnCover()
            {
                Calls.Add("Cover");
            }

            public void OnReveal()
            {
                Calls.Add("Reveal");
            }

            public void OnClose(bool destroy)
            {
                Calls.Add("Close");
            }
        }

        private static UIPageHandle CreateHandle(string pageId, UILayer layer, IUIPage page)
        {
            var def = new UIPageDef(pageId, null, new AssetKey("ui/" + pageId.ToLowerInvariant()), layer);
            var context = new UIPageContext { Def = def, Layer = layer, Page = page };
            var handle = new UIPageHandle(context) { IsValid = true };
            context.Handle = handle;
            return handle;
        }

        [Test]
        public void Push_Pop_TriggersCoverAndReveal()
        {
            var stack = new UIPageStack();
            var first = new FakePage();
            var second = new FakePage();

            UIPageHandle h1 = CreateHandle("A", UILayer.Normal, first);
            UIPageHandle h2 = CreateHandle("B", UILayer.Normal, second);

            stack.Push(h1);
            stack.Push(h2);
            CollectionAssert.AreEqual(new[] { "Cover" }, first.Calls, "打开上层时旧栈顶应收到 OnCover");

            Assert.IsTrue(stack.Pop(h2));
            CollectionAssert.Contains(first.Calls, "Reveal", "出栈后新栈顶应收到 OnReveal");
        }

        [Test]
        public void Back_SkipsSystemAndBackground()
        {
            var stack = new UIPageStack();
            stack.Push(CreateHandle("Bg", UILayer.Background, new FakePage()));
            stack.Push(CreateHandle("Normal", UILayer.Normal, new FakePage()));
            stack.Push(CreateHandle("Loading", UILayer.System, new FakePage()));
            stack.Push(CreateHandle("Popup", UILayer.Popup, new FakePage()));

            UIPageHandle top = stack.Back();
            Assert.AreEqual("Popup", top.PageId);

            Assert.IsTrue(stack.Pop(top));
            Assert.AreEqual("Normal", stack.Back().PageId, "System 层不响应返回键");
        }

        [Test]
        public void Find_ReturnsOpenHandle()
        {
            var stack = new UIPageStack();
            stack.Push(CreateHandle("A", UILayer.Normal, new FakePage()));

            Assert.IsNotNull(stack.Find("A"));
            Assert.IsNull(stack.Find("Missing"));
            Assert.IsTrue(stack.Contains("A"));
        }
    }
}
