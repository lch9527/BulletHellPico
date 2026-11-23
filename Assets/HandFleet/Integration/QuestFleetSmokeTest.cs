#if DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HandFleet.XR;

namespace HandFleet.Integration
{
    // Local, opt-in development test. Synthetic hands exercise HandFleet only.
    // Cold-launch with --ez metahackthone.handfleet_smoke_test true.
    public sealed class QuestFleetSmokeTest : MonoBehaviour
    {
        int errors;
        HandFleetSettings changedSettings;HandFleetMission changedMission;float oldInvulnerability;FleetStage[] oldStages;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            using var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity=player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent=activity.Call<AndroidJavaObject>("getIntent");
            if(!intent.Call<bool>("getBooleanExtra","metahackthone.handfleet_smoke_test",false))return;
            intent.Call("removeExtra","metahackthone.handfleet_smoke_test");
            var go=new GameObject("HandFleet development smoke test");DontDestroyOnLoad(go);go.AddComponent<QuestFleetSmokeTest>();
        }
        sealed class Hands:IHandFleetInputProvider
        {
            public bool Valid=true;
            public FleetInput Read()
            {
                float t=Time.unscaledTime;
                var position=new Vector3(Mathf.Sin(t*.7f)*.24f,Mathf.Sin(t*.43f)*.12f,0);
                var rotation=Quaternion.LookRotation(new Vector3(-position.x,-position.y,1.4f));
                var left=new FleetHand{Valid=Valid,Palm=new Pose(position-Vector3.right*.03f,rotation),Normal=Vector3.right,Forward=Vector3.forward,ConfidentFingers=31};
                var right=left;right.Palm.position=position+Vector3.right*.03f;right.Normal=Vector3.left;
                return new FleetInput{DesktopMode=FleetMode.Attack,HeadPosition=Vector3.back,Focused=true,Timestamp=Time.unscaledTimeAsDouble,Left=left,Right=right};
            }
        }
        IEnumerator Start()
        {
            Application.logMessageReceived+=Count;
            var run=Check();
            while(true)
            {
                bool next;
                try{next=run.MoveNext();}
                catch(Exception e){Debug.LogError("HANDFLEET_DEVICE_FAIL "+e);break;}
                if(!next)break;yield return run.Current;
            }
            Destroy(gameObject);
        }
        void Count(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors++;}
        void OnDestroy()
        {
            Application.logMessageReceived-=Count;
            RestoreTuning();
        }
        void RestoreTuning()
        {
            if(changedSettings)changedSettings.Invulnerability=oldInvulnerability;
            if(changedMission&&oldStages!=null)changedMission.Stages=oldStages;
            changedSettings=null;changedMission=null;oldStages=null;
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        IEnumerator Check()
        {
            Debug.Log("HANDFLEET_DEVICE_BEGIN synthetic_input=true other_gameplay=false");
            yield return SceneManager.LoadSceneAsync("HandFleet",LoadSceneMode.Single);
            float deadline=Time.realtimeSinceStartup+20;
            HandFleetEntry entry=null;
            while(Time.realtimeSinceStartup<deadline){entry=FindFirstObjectByType<HandFleetEntry>();if(entry&&entry.Ready)break;yield return null;}
            Require(entry&&entry.Ready,"Entry did not initialize");entry.TestRun=true;
            var originalProvider=entry.InputProvider;var hands=new Hands();entry.InputProvider=hands;
            var input=hands.Read();entry.Run.Start(input);
            Require(FindObjectsByType<MetaPlayerRig>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==1,"Duplicate player rig");
            Require(entry.Player.ViewCamera.stereoEnabled,"Stereo camera inactive");
            var renderers=entry.GetComponentsInChildren<Renderer>();
            Require(renderers.All(r=>r.sharedMaterial&&r.sharedMaterial.shader.isSupported),"Unsupported HandFleet material");
            Debug.Log("HANDFLEET_DEVICE_STAGE graphics stereo=true supportedMaterials=true");
            // Use normal attack logic and density. Runtime-only tuning shortens tutorials and
            // extend the final wave; a long hit-recovery window keeps the synthetic workload alive.
            var originalSettings=entry.Run.Settings;float originalInvulnerability=originalSettings.Invulnerability;
            var stages=entry.Mission.Stages;var workload=stages.Last(s=>s.Kind==StageKind.Combat&&s.Spawns.Length>1);
            changedSettings=originalSettings;oldInvulnerability=originalInvulnerability;changedMission=entry.Mission;oldStages=stages;
            originalSettings.Invulnerability=1000;
            entry.Mission.Stages=new[]{new FleetStage{Title="AUTOMATED GESTURE WORKLOAD",Kind=StageKind.Combat,HpBudget=10,Duration=180,MaxEnemies=workload.MaxEnemies,Spawns=workload.Spawns,AllowFusion=true}};
            entry.Run.Start(hands.Read());
            yield return new WaitForSecondsRealtime(3);
            Require(entry.Run.State==RunState.Running,"Synthetic run failed to start");
            Debug.Log("HANDFLEET_DEVICE_STAGE density_capture seconds=60 synthetic_input=true");
            float started=Time.realtimeSinceStartup;int frames=0;float total=0,worst=0;int maxBullets=0,maxEnemies=0;
            while(Time.realtimeSinceStartup-started<60)
            {
                float dt=Time.unscaledDeltaTime;frames++;total+=dt;worst=Mathf.Max(worst,dt);maxBullets=Mathf.Max(maxBullets,entry.Run.ActiveBullets);maxEnemies=Mathf.Max(maxEnemies,entry.Run.ActiveEnemies);yield return null;
            }
            Debug.Log($"HANDFLEET_DEVICE_TIMING seconds={total:F2} frames={frames} meanMs={total*1000/frames:F3} worstMs={worst*1000:F3} maxBullets={maxBullets} maxEnemies={maxEnemies} synthetic=true");
            hands.Valid=false;yield return null;yield return null;
            float clock=entry.Run.Clock;int shields=entry.Run.Shields,score=entry.Run.Score;var bullets=entry.Run.Bullets.Select(b=>b.Position).ToArray();
            yield return new WaitForSecondsRealtime(2);
            Require(entry.Run.Clock==clock&&entry.Run.Shields==shields&&entry.Run.Score==score,"Tracking hold changed combat state");
            Require(entry.Run.Bullets.Select(b=>b.Position).SequenceEqual(bullets),"Tracking hold moved bullets");
            hands.Valid=true;yield return new WaitForSecondsRealtime(4);
            Require(entry.Run.State==RunState.Running,"Tracking recovery did not resume");
            entry.Run.Pause();RestoreTuning();
            Require(errors==0,"Runtime errors: "+errors);
            Debug.Log("HANDFLEET_DEVICE_PASS tracking_freeze=true recovery=true synthetic_only=true wearer_validation_pending=true");
            // Discard the synthetic run entirely; do not save its score or leave fake hands active.
            yield return SceneManager.LoadSceneAsync("HandFleet",LoadSceneMode.Single);
        }
    }
}
#endif
