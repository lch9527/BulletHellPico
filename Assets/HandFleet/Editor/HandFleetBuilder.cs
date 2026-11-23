using System;
using System.IO;
using System.Linq;
using HandFleet.Integration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HandFleet.Editor
{
    public static class HandFleetBuilder
    {
        public const string ScenePath="Assets/HandFleet/Scenes/HandFleet.unity";
        const string Data="Assets/HandFleet/Data/";
        [MenuItem("HandFleet/Create missing assets")]
        public static void Create()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play mode before authoring HandFleet.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save open scene changes before authoring HandFleet.");
            Directory.CreateDirectory(Data);Directory.CreateDirectory("Assets/HandFleet/Scenes");Directory.CreateDirectory("Assets/HandFleet/Materials");AssetDatabase.Refresh();
            var settings=LoadOrCreate<HandFleetSettings>(Data+"FlightSettings.asset");
            var needle=Pattern("Needle",EnemyFamily.Needle,3,.9f,1.9f,.65f,100);
            var tumbler=Pattern("Tumbler",EnemyFamily.Tumbler,5,1,2.2f,.55f,150);
            var spindle=Pattern("Spindle",EnemyFamily.Spindle,7,1.1f,2.5f,.5f,200);
            var mission=AssetDatabase.LoadAssetAtPath<HandFleetMission>(Data+"LaunchCradle.asset");
            if(!mission)
            {
                mission=LoadOrCreate<HandFleetMission>(Data+"LaunchCradle.asset");mission.Patterns=new[]{needle,tumbler,spindle};
                mission.Stages=new[]{
                    Stage("FLIGHT PRACTICE",StageKind.Practice,25,1,5,EnemyFamily.Needle),
                    Stage("01 / COMMITTED BURSTS",StageKind.Combat,20,2,3.5f,EnemyFamily.Needle),
                    Stage("REST",StageKind.Rest,5,0,5),
                    Stage("02 / READ THE PULSE TILES",StageKind.Combat,20,2,4,EnemyFamily.Tumbler),
                    Stage("REST",StageKind.Rest,5,0,5),
                    Stage("FUSION PRACTICE",StageKind.FusionPractice,25,1,5,EnemyFamily.Spindle),
                    Stage("03 / READ THE RING",StageKind.Combat,25,2,4,EnemyFamily.Spindle),
                    Stage("REST",StageKind.Rest,5,0,5),
                    Stage("04 / CHOOSE YOUR TARGET",StageKind.Combat,30,4,3,EnemyFamily.Needle,EnemyFamily.Tumbler),
                    Stage("REST",StageKind.Rest,5,0,5),
                    Stage("05 / BRING THE FLEET HOME",StageKind.Combat,30,5,2.8f,EnemyFamily.Needle,EnemyFamily.Tumbler,EnemyFamily.Spindle)
                };EditorUtility.SetDirty(mission);
            }
            var ivory=Material("Ivory",new Color(.914f,.906f,.863f));var graphite=Material("Graphite",new Color(.12f,.17f,.22f));
            var cyan=Material("Cyan",new Color(.337f,.902f,.949f));var amber=Material("Amber",new Color(1,.702f,.298f));
            var lime=Material("Lime",new Color(.718f,.941f,.416f));var pink=Material("Pink",new Color(.949f,.427f,.678f));var panel=Material("Console",new Color(.031f,.059f,.122f));
            if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            {
                var setup=EditorSceneManager.GetSceneManagerSetup();
                try
                {
                    var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                    var entry=new GameObject("HandFleet shared-player entry").AddComponent<HandFleetEntry>();
                    entry.StandalonePlayer=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HandFleet/Prefabs/HandFleetPlayer.prefab");
                    if(!entry.StandalonePlayer)throw new InvalidOperationException("Shared MetaHackathonPlayer prefab is missing.");
                    entry.FlightDeckPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HandFleet/Prefabs/NeedleFlightDeck.prefab");
                    entry.Settings=settings;entry.Mission=mission;entry.Ivory=ivory;entry.Graphite=graphite;entry.Cyan=cyan;entry.Amber=amber;entry.Lime=lime;entry.Pink=pink;entry.PanelMaterial=panel;
                    var light=new GameObject("HandFleet lighting").AddComponent<Light>();light.type=LightType.Directional;light.intensity=.8f;light.shadows=LightShadows.None;light.transform.rotation=Quaternion.Euler(45,-30,0);
                    // The shared player supplies the only active camera at runtime.
                    EditorSceneManager.SaveScene(scene,ScenePath);
                }
                finally {EditorSceneManager.RestoreSceneManagerSetup(setup);}
            }
            EditorBuildSettings.scenes=EditorBuildSettings.scenes.Where(s=>s.path!=ScenePath).Append(new EditorBuildSettingsScene(ScenePath,true)).ToArray();
            AssetDatabase.SaveAssets();Debug.Log("HANDFLEET_ASSETS_READY");
        }
        static FleetStage Stage(string title,StageKind kind,float seconds,int max,float spawn,params EnemyFamily[] families)
            =>new FleetStage{Title=title,Kind=kind,Duration=seconds,MaxEnemies=max,SpawnInterval=spawn,Families=families.Length>0?families:new[]{EnemyFamily.Needle}};
        static T LoadOrCreate<T>(string path)where T:ScriptableObject
        {var a=AssetDatabase.LoadAssetAtPath<T>(path);if(a)return a;a=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(a,path);return a;}
        static HandFleetPattern Pattern(string name,EnemyFamily family,float health,float telegraph,float recovery,float speed,int score)
        {
            string path=Data+name+".asset";var p=AssetDatabase.LoadAssetAtPath<HandFleetPattern>(path);if(p)return p;
            p=LoadOrCreate<HandFleetPattern>(path);p.Family=family;p.Health=health;p.Telegraph=telegraph;p.Recovery=recovery;p.Speed=speed;p.KillPoints=score;EditorUtility.SetDirty(p);return p;
        }
        static Material Material(string name,Color color)
        {
            string path="Assets/HandFleet/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m)return m;
            var shader=Shader.Find("Universal Render Pipeline/Unlit");if(!shader)throw new InvalidOperationException("URP Unlit shader is missing.");
            m=new Material(shader){color=color};AssetDatabase.CreateAsset(m,path);return m;
        }
        [MenuItem("HandFleet/Open scene")]
        public static void Open()
        {if(EditorApplication.isPlaying)return;if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;Create();EditorSceneManager.OpenScene(ScenePath);}
        [MenuItem("HandFleet/Play desktop preview")]
        public static void Play(){Open();if(SceneManager.GetActiveScene().path==ScenePath)EditorApplication.isPlaying=true;}
    }
}
