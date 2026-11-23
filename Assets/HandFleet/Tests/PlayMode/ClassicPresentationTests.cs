using System.Collections;
using System.Linq;
using HandFleet.Integration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace HandFleet.Tests
{
    public sealed class ClassicPresentationTests
    {
        [Test]public void PresentationLeaseRestoresCameraAndIsIdempotent()
        {
            var root=new GameObject("Presentation lease test");var camera=root.AddComponent<Camera>();camera.backgroundColor=Color.magenta;camera.clearFlags=CameraClearFlags.Skybox;
            var lease=new FleetVRPresentation(root);Assert.AreEqual(1,camera.backgroundColor.a);Assert.AreEqual(CameraClearFlags.SolidColor,camera.clearFlags);
            lease.Dispose();lease.Dispose();Assert.AreEqual(Color.magenta,camera.backgroundColor);Assert.AreEqual(CameraClearFlags.Skybox,camera.clearFlags);Object.DestroyImmediate(root);
        }
        [UnityTest]public IEnumerator AuthoredSceneHasArtAndNoExtraCamera()
        {
            yield return SceneManager.LoadSceneAsync("HandFleet");yield return null;yield return null;
            var entry=Object.FindFirstObjectByType<HandFleetEntry>();Assert.IsTrue(entry.Ready);entry.TestRun=true;
            Assert.IsNotNull(entry.Art.Kestrel);Assert.AreEqual(System.Enum.GetValues(typeof(EnemyFamily)).Length,entry.Art.Enemies.Length);Assert.IsTrue(entry.Art.Enemies.All(e=>e));Assert.IsTrue(entry.Art.Sounds.All(s=>s));
            foreach(var cue in new[]{FleetCue.WarpArrival,FleetCue.TilePulse,FleetCue.CarrierLaunch})
                Assert.IsNotNull(entry.Art.SoundFor(new FleetCombatEvent(cue,Vector3.zero,Vector3.forward),out _),cue.ToString());
            var engines=entry.GetComponentInChildren<FleetEffects>().GetComponentsInChildren<AudioSource>();
            foreach(var name in new[]{"Kestrel engine","Nearest Follower engine","Heavy ship engine"})
            {
                var engine=engines.Single(source=>source.name==name);
                Assert.IsTrue(engine.clip && engine.loop && engine.spatialBlend==1,name);
            }
            Assert.AreEqual(0,entry.GetComponentsInChildren<Camera>(true).Length);Assert.IsTrue(entry.Mission.Arcade);Assert.IsFalse(entry.Mission.GestureCampaign);
            Assert.IsNotNull(entry.GetComponentInChildren<ArcadePresentation>());Assert.IsNull(entry.GetComponentInChildren<GesturePresentation>());
            Assert.IsTrue(entry.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterial&&r.sharedMaterial.shader.isSupported));
            Assert.IsTrue(entry.Player.GetComponentsInChildren<OVRPassthroughLayer>(true).All(l=>!l.enabled));
            Assert.AreEqual(1,entry.Player.ViewCamera.backgroundColor.a);
        }
    }
}
