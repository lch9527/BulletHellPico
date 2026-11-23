using System.Collections;
using System.Linq;
using HandFleet.Integration;
using NUnit.Framework;
using HandFleet.XR;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HandFleet.Tests
{
    public sealed class MissionTests
    {
        sealed class FakeInput:IHandFleetInputProvider
        {
            public double Time;
            public FleetInput Read()
            {
                var left=new FleetHand{Valid=true,Palm=new Pose(Vector3.left*.03f,Quaternion.identity),Normal=Vector3.right,Forward=Vector3.forward};var right=left;right.Palm.position=Vector3.right*.03f;right.Normal=Vector3.left;
                return new FleetInput{Timestamp=Time,Focused=true,Left=left,Right=right};
            }
        }
        [UnityTearDown]public IEnumerator Cleanup(){yield return SceneManager.LoadSceneAsync("HandFleet");yield return null;}
        static void OneRig(){Assert.AreEqual(1,Object.FindObjectsByType<MetaPlayerRig>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length);Assert.AreEqual(1,Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l=>l.isActiveAndEnabled));}
        [UnityTest]public IEnumerator StandaloneSceneBootsItsOwnPlayerAndQuitsInsteadOfReturningToAMenu()
        {
            yield return SceneManager.LoadSceneAsync("HandFleet");yield return null;yield return null;
            var entry=Object.FindFirstObjectByType<HandFleetEntry>();Assert.IsNotNull(entry);Assert.IsTrue(entry.Ready);OneRig();
            Assert.AreSame(MetaPlayerRig.Shared,entry.Player);Assert.AreEqual(RunState.Calibration,entry.Run.State);
            var exit=entry.GetComponentsInChildren<FleetControl>(true).Single(b=>b.name=="MAIN MENU");
            Assert.AreEqual("HOLD TO QUIT",exit.Label.text,"HandFleet is the whole app, so its exit control quits.");
        }
        [UnityTest]public IEnumerator FullArcadeRunBeatsTheBossGoesEndlessAndRetryRestoresPools()
        {
            yield return SceneManager.LoadSceneAsync("HandFleet");yield return null;yield return null;
            var entry=Object.FindFirstObjectByType<HandFleetEntry>();var input=new FakeInput();entry.InputProvider=input;
            var run=new HandFleetRun(entry.Settings,entry.Mission);Assert.IsTrue(run.Start(input.Read()));
            for(int step=0;step<60000&&!run.Won&&run.State==RunState.Running;step++)
            {
                input.Time+=.02;run.Tick(input.Read(),.02f);
                // A deterministic perfect player removes each spawned target, and the boss once it is the last wave.
                for(int i=0;i<run.Enemies.Length;i++)if(run.Enemies[i].Active&&(run.Enemies[i].Family!=EnemyFamily.Gatekeeper||run.Enemies[i].State!=EnemyState.Arriving))run.DamageEnemy(i,100000,false);
            }
            Assert.IsTrue(run.Won);Assert.Greater(run.Score,0);
            if(entry.Settings.EndlessAfterBoss){Assert.AreEqual(RunState.Running,run.State);Assert.IsTrue(run.Endless);Assert.AreEqual(entry.Settings.ArcadeWaves+2,run.Wave);}
            else Assert.AreEqual(RunState.Results,run.State);
            // Endless runs end when the ship is hit.
            for(int n=0;n<20&&run.State==RunState.Running;n++){run.Emit(true,7,run.Motor.Position+Vector3.forward*.05f,Vector3.back*2,.01f,1);input.Time+=.02;run.Tick(input.Read(),.02f);}
            Assert.AreEqual(RunState.Results,run.State);Assert.IsTrue(run.Won);
            run.Start(input.Read());Assert.AreEqual(0,run.ActiveBullets);Assert.AreEqual(0,run.ActiveEnemies);Assert.AreEqual(0,run.Clock);Assert.AreEqual(1,run.Shields);Assert.AreEqual(0,run.Score);Assert.AreEqual(0,run.Wave);Assert.AreEqual(0,run.WeaponLevel);
        }
        [UnityTest]public IEnumerator HeavyEngineFollowsNearestHullAndStopsWhenItLeaves()
        {
            yield return SceneManager.LoadSceneAsync("HandFleet");yield return null;yield return null;
            var entry=Object.FindFirstObjectByType<HandFleetEntry>();
            entry.TestRun=true;
            var run=entry.Run;
            Assert.IsTrue(run.Start(new FakeInput().Read()));
            var effects=entry.GetComponentInChildren<FleetEffects>();
            var drive=effects.GetComponentsInChildren<AudioSource>().Single(v=>v.clip==entry.Art.HeavyEngineLoop);
            run.Enemies[0]=new FleetEnemy{Active=true,Family=EnemyFamily.Frigate,Position=run.Motor.Position+Vector3.forward};
            run.Enemies[1]=new FleetEnemy{Active=true,Family=EnemyFamily.Carrier,Position=run.Motor.Position+Vector3.right*2};
            effects.Present(run,false,1);
            Assert.IsTrue(drive.isPlaying);
            Assert.AreEqual(run.Enemies[0].Position,drive.transform.localPosition);
            run.Enemies[0].Active=false;
            effects.Present(run,false,1);
            Assert.AreEqual(run.Enemies[1].Position,drive.transform.localPosition);
            run.Enemies[1].Active=false;
            effects.Present(run,false,1);
            Assert.IsFalse(drive.isPlaying);
        }
        [UnityTest]public IEnumerator FinalHullImpactCanPlayAfterTheEnginesStop()
        {
            yield return SceneManager.LoadSceneAsync("HandFleet");yield return null;yield return null;
            var entry=Object.FindFirstObjectByType<HandFleetEntry>();
            entry.TestRun=true;
            var input=new FakeInput();
            entry.InputProvider=input;
            var run=entry.Run;
            Assert.IsTrue(run.Start(input.Read()));
            var effects=entry.GetComponentInChildren<FleetEffects>();
            effects.Present(run,false,1);
            run.Hit();
            Assert.AreEqual(RunState.Results,run.State);
            effects.Present(run,false,1);
            var voices=effects.GetComponentsInChildren<AudioSource>();
            var impact=entry.Art.Sounds[(int)FleetCue.Hit];
            Assert.IsTrue(voices.Any(v=>v.clip==impact && v.isPlaying),"Kestrel hull impact must survive the results transition.");
            Assert.IsFalse(voices.Any(v=>v.loop && v.isPlaying),"Flight engines must stop when the Kestrel is destroyed.");
            Assert.IsTrue(run.Start(input.Read()));
            Assert.IsFalse(voices.Any(v=>v.clip==impact && v.isPlaying),"Retry must stop the previous impact.");
        }
        [UnityTest]public IEnumerator TrackingLossFreezesTheWorldAndResumesWhenTheHandReturns()
        {
            yield return SceneManager.LoadSceneAsync("HandFleet");yield return null;yield return null;
            var entry=Object.FindFirstObjectByType<HandFleetEntry>();var fake=new FakeInput();var run=new HandFleetRun(entry.Settings,entry.Mission);var input=fake.Read();run.Start(input);
            run.Emit(true,1,Vector3.forward,Vector3.back,.01f,1);
            input.Right.Valid=false;run.Tick(input,.02f);Assert.AreEqual(RunState.TrackingHold,run.State);Assert.IsFalse(run.RecoveryChoiceRequired);
            var bullet=run.Bullets[0].Position;for(int i=0;i<100;i++){input.Timestamp+=.02;run.Tick(input,.02f);}Assert.AreEqual(0,run.Clock);Assert.AreEqual(bullet,run.Bullets[0].Position);
            input.Right.Valid=true;for(int i=0;i<12;i++){input.Timestamp+=.02;run.Tick(input,.02f);}
            Assert.AreEqual(RunState.Running,run.State);Assert.AreEqual(1,run.Shields);Assert.Greater(run.InvulnerableUntil,run.Clock);
        }
    }
}
