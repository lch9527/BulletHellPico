using System.Collections;
using HandFleet.Integration;
using NUnit.Framework;
using Oculus.Interaction;
using HandFleet.XR;
using UnityEngine;
using UnityEngine.TestTools;

namespace HandFleet.Tests
{
    public sealed class ConsoleTests
    {
        [Test]public void XRConsoleFacesTheEyeJustBelowEyeLevel()
        {
            // The arena origin (ship cradle) is placed 0.45 m ahead of and 0.35 m below the eye at launch.
            var eye=new Vector3(0,.35f,-.45f);var pose=HandFleetEntry.ConsolePose(eye);
            // The visible panel (title down to one control row) is centred ~0.15 m above the console origin.
            var centre=pose.position+pose.rotation*Vector3.up*.15f;
            float below=Mathf.Atan2(eye.y-centre.y,centre.z-eye.z)*Mathf.Rad2Deg;
            Assert.That(below,Is.InRange(6f,16f),"The panel should sit a little below the eye line, not at the knees.");
            Assert.That(Vector3.Distance(eye,pose.position),Is.InRange(.8f,1.1f));
            Assert.Greater(Vector3.Dot(pose.rotation*Vector3.back,(eye-pose.position).normalized),.999f,"The labelled face (-z) must face the eye.");
            Assert.Greater(pose.position.z,.3f,"The console must stay behind the parked ship at the arena origin.");
        }
        [UnityTest]public IEnumerator MenuButtonHoverComesBackAfterAPressDebounce()
        {
            bool available=true,hover=false;int presses=0;
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var button=go.AddComponent<MetaMenuButton>();
            button.Initialize(()=>presses++,()=>available,(h,s)=>hover=h,new Vector3(0,0,-.51f),Quaternion.identity);
            button.Ray.PublishPointerEvent(new PointerEvent(1,PointerEventType.Hover,default));Assert.IsTrue(hover);
            available=false;yield return null;Assert.IsFalse(hover,"No highlight while presses are blocked.");
            available=true;yield return null;Assert.IsTrue(hover,"Hover must come back without the ray leaving the button.");
            button.Ray.PublishPointerEvent(new PointerEvent(1,PointerEventType.Select,default));Assert.AreEqual(1,presses);
            Object.Destroy(go);
        }
        [Test]public void SliderMapsTheRayAlongItsTrackAndSnapsToFivePercent()
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.localScale=new Vector3(.54f,.052f,.016f);
            go.transform.SetPositionAndRotation(new Vector3(0,1,1),Quaternion.Euler(13,20,0));
            go.AddComponent<FleetControl>();var slider=go.AddComponent<FleetSlider>();float changed=-1;slider.Changed=v=>changed=v;
            slider.Build(-.08f,.25f,null,null,null,null,null,null);
            var root=go.transform.Find("Slider");
            slider.SetFromWorld(root.TransformPoint(new Vector3(.25f,0,0)));Assert.AreEqual(1,slider.Value,1e-5f);Assert.AreEqual(1,changed,1e-5f);
            slider.SetFromWorld(root.TransformPoint(new Vector3(-.3f,0,0)));Assert.AreEqual(0,slider.Value,1e-5f,"Past the start clamps to 0.");
            slider.SetFromWorld(root.TransformPoint(new Vector3(-.08f+.33f*.52f,0,0)));Assert.AreEqual(.5f,slider.Value,1e-5f,"52% snaps to 50%.");
            // A ray from in front of the panel, aimed above the thin track, still maps by its sideways position.
            var target=root.TransformPoint(new Vector3(-.08f+.33f*.8f,.03f,0));var from=target-go.transform.forward*.9f+go.transform.up*.2f;
            Assert.IsTrue(slider.SetFromRay(new Ray(from,target-from)));Assert.AreEqual(.8f,slider.Value,1e-5f);
            Object.DestroyImmediate(go);
        }
        [UnityTest]public IEnumerator ArcadeConsoleUsesNeedleMeshAndOpensSettings()
        {
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("HandFleet");
            var entry=Object.FindFirstObjectByType<HandFleetEntry>();
            float deadline=Time.realtimeSinceStartup+10;
            while(!entry.Ready && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.IsTrue(entry.Ready);
            yield return new WaitForSecondsRealtime(.65f);
            var console=entry.Arena.Find("Flight service console");
            var deck=console.Find("Needle Flight Deck");
            var mesh=deck.Find("Housing").GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.vertexCount,100);
            Assert.IsTrue(deck.Find("Launch cue").gameObject.activeInHierarchy);
            var drawer=console.Find("Settings drawer");
            Assert.IsFalse(drawer.gameObject.activeSelf);
            var settings=deck.Find("SETTINGS").GetComponent<FleetControl>();
            settings.Press();
            Assert.IsTrue(drawer.gameObject.activeInHierarchy);
            Assert.IsFalse(deck.Find("Launch cue").gameObject.activeSelf);
            Assert.IsNotNull(drawer.GetComponentInChildren<FleetSlider>());
            var quit=deck.Find("MAIN MENU").GetComponent<FleetControl>();
            Assert.AreEqual(ControlKind.Hold,quit.Kind);
            Assert.IsFalse(quit.GetComponent<Collider>().bounds.Intersects(settings.GetComponent<Collider>().bounds));
            yield return new WaitForSecondsRealtime(.45f);
            settings.Press();
            Assert.IsFalse(drawer.gameObject.activeSelf);
            Assert.IsTrue(deck.Find("Launch cue").gameObject.activeInHierarchy);
        }
    }
}
