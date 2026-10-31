using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Yato.Persistence;
using Yato.Save;
using Yato.Save.CounterSample;
using Yato.UI;
using Yato.UI.Input.FocusSample;

namespace Yato.OptionalConsumerTests
{
    public sealed class OptionalConsumerTests
    {
        [UnityTest] public IEnumerator OptionalSamples_RealUGUIFocusAndCounterStorage()
        {
            yield return SceneManager.LoadSceneAsync("FocusSample");
            float deadline = Time.realtimeSinceStartup + 5;
            while (EventSystem.current?.currentSelectedGameObject == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsNotNull(EventSystem.current);
            GameObject original = EventSystem.current.currentSelectedGameObject;
            Assert.IsNotNull(original);
            var entry = UnityEngine.Object.FindObjectOfType<FocusSampleEntry>();
            entry.Open(true);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual("FocusPopup", UIRouter.TopPage.PageId);
            EventSystem.current.currentSelectedGameObject.GetComponent<Button>().onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual("FocusMain", UIRouter.TopPage.PageId);
            Assert.AreSame(original, EventSystem.current.currentSelectedGameObject);
            yield return SceneManager.LoadSceneAsync("CounterSample");
            Assert.IsNotNull(UnityEngine.Object.FindObjectOfType<SaveCounterSample>());
            string root = Path.Combine(Application.temporaryCachePath, "optional-counter-" + Guid.NewGuid().ToString("N"));
            try
            {
                var service = new SaveService<CounterData>(new FileStore(root), 1);
                var saved = service.SaveAsync("demo", new CounterData { Count = 7 });
                while (!saved.IsCompleted) yield return null;
                saved.GetAwaiter().GetResult();
                var loaded = service.LoadAsync("demo");
                while (!loaded.IsCompleted) yield return null;
                Assert.AreEqual(7, loaded.GetAwaiter().GetResult().Data.Count);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
