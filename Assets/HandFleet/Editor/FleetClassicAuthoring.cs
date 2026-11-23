using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using HandFleet.Integration;

namespace HandFleet.Editor
{
    public static class FleetClassicAuthoring
    {
        const string Root="Assets/HandFleet/";
        static Material ivory,graphite,cyan,amber,navy,lime;
        static Mesh armor,ring;
        [MenuItem("HandFleet/Author Classic assets")]
        public static void Build()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode first.");
            foreach(string dir in new[]{"Models","Prefabs","Audio","Data"})Directory.CreateDirectory(Root+dir);
            ivory=Mat("Ivory");graphite=Mat("Graphite");cyan=Mat("Cyan");amber=Mat("Amber");navy=Mat("Console");lime=Mat("Lime");
            foreach(var m in new[]{ivory,graphite}){var color=m.color;m.shader=Shader.Find("Universal Render Pipeline/Lit");m.color=color;m.SetFloat("_Metallic",.25f);m.SetFloat("_Smoothness",.4f);EditorUtility.SetDirty(m);}
            armor=SaveMesh(Bevel(),"BeveledArmor");ring=SaveMesh(Torus(),"Ring");
            var art=Load<FleetArtAssets>("Data/ClassicArt.asset");
            art.Kestrel=UpdateKestrel();
            art.Enemies=new GameObject[Enum.GetValues(typeof(EnemyFamily)).Length];
            for(int i=0;i<art.Enemies.Length;i++) art.Enemies[i]=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Prefabs/"+(i>=(int)EnemyFamily.Sentry?"Gesture/":"")+(EnemyFamily)i+".prefab");
            art.Arena=Save(Arena(),"LaunchCradle");
            art.Sounds=new AudioClip[Enum.GetValues(typeof(FleetCue)).Length];
            for(int i=0;i<art.Sounds.Length;i++)art.Sounds[i]=Sound((FleetCue)i);
            art.Ambience=Ambient();EditorUtility.SetDirty(art);
            AuthorMission();
            var setup=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene=EditorSceneManager.OpenScene(HandFleetBuilder.ScenePath);
                var entry=UnityEngine.Object.FindFirstObjectByType<HandFleetEntry>();entry.Art=art;
                entry.Mission=AssetDatabase.LoadAssetAtPath<HandFleetMission>(Root+"Data/ClassicLaunchCradle.asset");
                EditorUtility.SetDirty(entry);EditorSceneManager.SaveScene(scene);
                var inspect=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var camera=new GameObject("Inspection camera").AddComponent<Camera>();camera.transform.position=new Vector3(.55f,.35f,-.85f);camera.transform.LookAt(Vector3.zero);camera.fieldOfView=40;camera.backgroundColor=new Color(.015f,.025f,.045f);camera.clearFlags=CameraClearFlags.SolidColor;
                var light=new GameObject("Inspection light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(35,-30,0);
                var model=(GameObject)PrefabUtility.InstantiatePrefab(art.Kestrel);model.name="Kestrel inspection";
                var atlas=(GameObject)PrefabUtility.InstantiatePrefab(art.Kestrel);atlas.transform.position=new Vector3(.3f,0,0);
                var mirror=(GameObject)PrefabUtility.InstantiatePrefab(art.Kestrel);mirror.transform.position=new Vector3(.23f,0,0);mirror.transform.localScale=new Vector3(-1,1,1);
                EditorSceneManager.SaveScene(inspect,Root+"Scenes/HandFleetInspection.unity");
            }
            finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();Debug.Log("HANDFLEET_CLASSIC_AUTHORED");
        }
        static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/"+name+".mat");
        static T Load<T>(string relative)where T:ScriptableObject
        {var obj=AssetDatabase.LoadAssetAtPath<T>(Root+relative);if(!obj){obj=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(obj,Root+relative);}return obj;}
        static Mesh SaveMesh(Mesh source,string name)
        {string path=Root+"Models/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);source.name=name;if(old){EditorUtility.CopySerialized(source,old);UnityEngine.Object.DestroyImmediate(source);return old;}AssetDatabase.CreateAsset(source,path);return source;}
        static GameObject Save(GameObject source,string name)
        {var result=PrefabUtility.SaveAsPrefabAsset(source,Root+"Prefabs/"+name+".prefab");UnityEngine.Object.DestroyImmediate(source);return result;}
        static Transform Part(Transform root,string name,Vector3 pos,Vector3 scale,Material mat,bool torus=false)
        {var t=FleetGeometry.Root(root,name);t.localPosition=pos;t.localScale=scale;t.gameObject.AddComponent<MeshFilter>().sharedMesh=torus?ring:armor;var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;return t;}
        [MenuItem("HandFleet/Update Kestrel model")]
        public static void UpdateKestrelModel() => UpdateKestrel();

        static GameObject UpdateKestrel()
        {
            const string path=Root+"Prefabs/Kestrel.prefab";
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Models/Kestrel.fbx");
            if(!source) throw new FileNotFoundException("Kestrel FBX is missing.");
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                // Preserve the prefab identity and the named attachment points used by fleet presentation.
                for(int i=root.transform.childCount-1;i>=0;i--)
                {
                    var child=root.transform.GetChild(i);
                    if(child.name=="Dock" || child.name=="Muzzle" || child.name=="Engine" ||
                        (child.name.Length==2 && child.name[0]=='F' && char.IsDigit(child.name[1])))
                    {
                        for(int j=child.childCount-1;j>=0;j--)
                            UnityEngine.Object.DestroyImmediate(child.GetChild(j).gameObject);
                        var renderer=child.GetComponent<MeshRenderer>();
                        if(renderer) UnityEngine.Object.DestroyImmediate(renderer);
                        var filter=child.GetComponent<MeshFilter>();
                        if(filter) UnityEngine.Object.DestroyImmediate(filter);
                        continue;
                    }
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
                var hull=(GameObject)PrefabUtility.InstantiatePrefab(source,root.transform);
                hull.name="Kestrel hull";
                var rear=hull.GetComponentsInChildren<Transform>().Single(t=>t.name=="FX_Forward_L");
                root.transform.Find("Engine").localPosition=root.transform.InverseTransformPoint(rear.position);
                return PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static GameObject Arena()
        {
            var go=new GameObject("Launch Cradle orbital conservatory");var root=go.transform;
            for(int i=0;i<16;i++)
            {
                float a=i*22.5f;var rot=Quaternion.Euler(0,a,0);var pos=rot*Vector3.forward*4.5f;
                Part(root,"Perimeter rib "+i,pos+Vector3.up*.4f,new Vector3(.14f,5,.18f),graphite).localRotation=rot;
                Part(root,"Maintenance foot",pos+Vector3.down*1.65f,new Vector3(.65f,.16f,1.1f),ivory).localRotation=rot;
                Part(root,"Low navigation strip",pos+Vector3.down*1.42f,new Vector3(.35f,.012f,.025f),cyan).localRotation=rot;
                Part(root,"Upper docking rib",pos+Vector3.up*2.3f,new Vector3(1.4f,.12f,.6f),graphite).localRotation=rot;
                if(i%4==0)Part(root,"Dock equipment",pos+Vector3.down*.9f,new Vector3(.42f,.7f,.35f),graphite).localRotation=rot;
            }
            var floor=Part(root,"Quiet landing deck",new Vector3(0,-1.65f,0),new Vector3(10,.12f,10),navy);
            for(int i=0;i<3;i++){var r=Part(root,"Deck orbital track",new Vector3(0,-1.57f+i*.008f,0),Vector3.one*(2.3f+i*1.5f),i==0?cyan:graphite,true);r.localRotation=Quaternion.Euler(90,0,0);}
            var planet=FleetGeometry.Shape(root,"Distant planet",PrimitiveType.Sphere,new Vector3(14,9,24),Vector3.one*9,graphite);
            var orbit=Part(root,"Planet orbital silhouette",planet.localPosition,Vector3.one*12,ivory,true);orbit.localRotation=Quaternion.Euler(65,15,0);
            // Merge static pieces by material to keep mobile draw calls bounded.
            var groups=root.GetComponentsInChildren<MeshFilter>().GroupBy(f=>f.GetComponent<Renderer>().sharedMaterial).ToArray();
            int index=0;
            foreach(var group in groups)
            {
                var combines=group.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=f.transform.localToWorldMatrix}).ToArray();
                var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(combines);mesh=SaveMesh(mesh,"CradleBatch"+index++);
                var t=FleetGeometry.Root(root,"Static "+group.Key.name);t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=group.Key;
            }
            foreach(var group in groups)foreach(var f in group)UnityEngine.Object.DestroyImmediate(f.gameObject);
            return go;
        }
        static void BatchDirect(Transform root,string name)
        {
            var groups=root.GetComponentsInChildren<MeshFilter>().Where(f=>f.transform.parent==root&&f.name!="Engine"&&!f.name.StartsWith("Iris armor")).GroupBy(f=>f.GetComponent<Renderer>().sharedMaterial).ToArray();
            int index=0;
            foreach(var group in groups)
            {
                var combined=new Mesh();combined.CombineMeshes(group.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=root.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray());
                var t=FleetGeometry.Root(root,"Batched "+group.Key.name);t.gameObject.AddComponent<MeshFilter>().sharedMesh=SaveMesh(combined,name+index++);var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=group.Key;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                foreach(var f in group)UnityEngine.Object.DestroyImmediate(f.gameObject);
            }
        }
        static Mesh Bevel()
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            Vector2[] profile={new(-.35f,-.5f),new(.35f,-.5f),new(.5f,-.35f),new(.5f,.35f),new(.35f,.5f),new(-.35f,.5f),new(-.5f,.35f),new(-.5f,-.35f)};
            for(int z=0;z<4;z++)for(int i=0;i<8;i++)vertices.Add(new Vector3(profile[i].x*(z==0||z==3?.8f:1),profile[i].y*(z==0||z==3?.8f:1),new[]{-.5f,-.35f,.35f,.5f}[z]));
            for(int z=0;z<3;z++)for(int i=0;i<8;i++){int a=z*8+i,b=z*8+(i+1)%8;triangles.AddRange(new[]{a,b,a+8,b,b+8,a+8});}
            for(int i=1;i<7;i++){triangles.AddRange(new[]{0,i+1,i,24,24+i,25+i});}
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static Mesh Torus()
        {
            var v=new List<Vector3>();var t=new List<int>();const int n=48,k=6;
            for(int i=0;i<n;i++)for(int j=0;j<k;j++){float a=i*Mathf.PI*2/n,b=j*Mathf.PI*2/k;float r=.47f+.03f*Mathf.Cos(b);v.Add(new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,Mathf.Sin(b)*.03f));}
            for(int i=0;i<n;i++)for(int j=0;j<k;j++){int a=i*k+j,b=((i+1)%n)*k+j,c=i*k+(j+1)%k,d=((i+1)%n)*k+(j+1)%k;t.AddRange(new[]{a,b,c,b,d,c});}
            var mesh=new Mesh();mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static void AuthorMission()
        {
            var mission=Load<HandFleetMission>("Data/ClassicLaunchCradle.asset");mission.ScoreVersion="Classic.v1";
            var patterns=new List<HandFleetPattern>();
            for(int i=0;i<6;i++)
            {
                var p=Load<HandFleetPattern>("Data/Classic"+(EnemyFamily)i+".asset");p.Family=(EnemyFamily)i;p.Health=new[]{3,5,7,6,3,600}[i];p.Telegraph=i==3?1.2f:.9f;p.Recovery=i==5?1.5f:2.1f;p.Speed=.65f;p.KillPoints=i==5?3000:100+i*50;patterns.Add(p);EditorUtility.SetDirty(p);
            }
            mission.Patterns=patterns.ToArray();var stages=new List<FleetStage>();
            stages.Add(new FleetStage{Title="FLIGHT / LASER TRAINING",Kind=StageKind.Practice,Duration=25,MaxEnemies=2,SpawnInterval=4,Instruction="Protect the cyan core. Aim to fire. Pinch your steering hand to fire the charged laser."});
            stages.Add(new FleetStage{Title="OPTIONAL ATLAS DOCKING",Kind=StageKind.FusionPractice,Duration=25,MaxEnemies=1,SpawnInterval=8,AllowFusion=true,Instruction="Pause to calibrate palm contact. Join gently, or skip and fly solo."});
            EnemyFamily[][] groups={
                new[]{EnemyFamily.Needle,EnemyFamily.Needle,EnemyFamily.Needle},
                new[]{EnemyFamily.Needle,EnemyFamily.Lancer,EnemyFamily.Needle,EnemyFamily.Lancer},
                new[]{EnemyFamily.Spindle,EnemyFamily.Tumbler,EnemyFamily.Comet},
                new[]{EnemyFamily.Spindle,EnemyFamily.Needle,EnemyFamily.Tumbler,EnemyFamily.Comet},
                new[]{EnemyFamily.Lancer,EnemyFamily.Needle,EnemyFamily.Lancer,EnemyFamily.Spindle},
                new[]{EnemyFamily.Tumbler,EnemyFamily.Comet,EnemyFamily.Spindle,EnemyFamily.Lancer,EnemyFamily.Needle}
            };
            for(int wave=0;wave<groups.Length;wave++)
            {
                var spawns=new List<FleetSpawn>();
                for(int round=0;round<2;round++)for(int i=0;i<groups[wave].Length;i++)
                {
                    float angle=wave==0?-35+i*35: -100+i*(200f/Mathf.Max(1,groups[wave].Length-1))+round*30;
                    Vector3 pos=Quaternion.Euler(0,angle,0)*Vector3.forward*(1.65f+(i%2)*.3f);pos.y=.15f+(i%2)*.25f;
                    spawns.Add(new FleetSpawn{At=round*12+i*1.8f,Family=groups[wave][i],Position=pos,EntryOffset=pos.normalized*.75f+Vector3.up*.4f,Group=i%2,PathAxis=Vector3.Cross(Vector3.up,pos).normalized,PathAmplitude=wave>=2?.2f:.07f,PathSpeed=.6f});
                }
                stages.Add(new FleetStage{Title=new[]{"01 / APPROACH","02 / CROSSING LANES","03 / RADIAL PRESSURE","04 / ORBITAL SWARM","05 / LASER CROSSWEAVE","06 / FINAL APPROACH"}[wave],Kind=StageKind.Combat,Completion=EncounterEnd.ClearEnemies,Duration=25,MaxEnemies=6,AllowFusion=true,Spawns=spawns.ToArray(),Instruction=wave==1?"Amber brackets warn before beams lock. Look toward directional charge sounds.":"Clear every target to advance. Pinch steering hand: charged laser."});
                stages.Add(new FleetStage{Title="FLIGHT REST",Kind=StageKind.Rest,Duration=5,MaxEnemies=0,AllowFusion=true,Instruction="Rest your arms. Pause for an indefinite break."});
            }
            stages.Add(new FleetStage{Title="GATEKEEPER / CORE BREACH",Kind=StageKind.Combat,Completion=EncounterEnd.ClearEnemies,Duration=1,MaxEnemies=1,AllowFusion=true,Spawns=new[]{new FleetSpawn{Family=EnemyFamily.Gatekeeper,Position=new Vector3(0,.35f,2),EntryOffset=Vector3.forward*2,PathAxis=Vector3.right,PathAmplitude=.4f,PathSpeed=.3f}},Instruction="Break the iris. Read bursts, then rings, then fans. Laser charge persists between phases."});
            mission.Stages=stages.ToArray();EditorUtility.SetDirty(mission);
        }
        static AudioClip Sound(FleetCue cue)
        {
            bool impact=cue==FleetCue.Hit||cue==FleetCue.Kill||cue==FleetCue.EnemyHit;bool warning=cue==FleetCue.Charge||cue==FleetCue.BossPhase;
            float duration=cue==FleetCue.Laser?2:cue==FleetCue.Victory?1.5f:warning?.8f:impact?.4f:cue==FleetCue.PlayerShot?.09f:.3f;
            int count=(int)(32000*duration);var samples=new float[count];var random=new System.Random(719+(int)cue);float noise=0;
            for(int i=0;i<count;i++)
            {
                float t=i/32000f,u=t/duration,env=Mathf.Min(1,t/.008f)*Mathf.Pow(1-u,impact?3:1.5f);
                noise=Mathf.Lerp(noise,(float)random.NextDouble()*2-1,.2f);
                float freq=impact?80:warning?270:cue==FleetCue.PlayerShot?1100:420+(int)cue*19;
                float phase=2*Mathf.PI*(freq*t+(warning?130:-freq*.2f)*t*t);
                float value=Mathf.Sin(phase)*.4f+Mathf.Sin(phase*1.503f)*.12f+noise*(impact?.7f:.1f);
                if(cue==FleetCue.Pickup||cue==FleetCue.LaserReady||cue==FleetCue.Victory)value+=Mathf.Sin(t*2*Mathf.PI*(u<.33f?660:u<.66f?880:1320))*.25f;
                samples[i]=Mathf.Clamp(value*env*.4f,-.8f,.8f);
            }
            return Wav(cue.ToString(),samples);
        }
        static AudioClip Ambient()
        {
            var samples=new float[32000*4];for(int i=0;i<samples.Length;i++){float t=i/32000f;samples[i]=(.035f*Mathf.Sin(t*2*Mathf.PI*55)+.015f*Mathf.Sin(t*2*Mathf.PI*82.5f))*(.8f+.2f*Mathf.Sin(t*Mathf.PI*.5f));}return Wav("CradleAmbience",samples);
        }
        static AudioClip Wav(string name,float[] samples)
        {
            string path=Root+"Audio/"+name+".wav";
            using(var writer=new BinaryWriter(File.Open(path,FileMode.Create)))
            {writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples.Length*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(32000);writer.Write(64000);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples.Length*2);foreach(float s in samples)writer.Write((short)(s*32767));}
            AssetDatabase.ImportAsset(path);var importer=(AudioImporter)AssetImporter.GetAtPath(path);var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.PCM;importer.defaultSampleSettings=settings;importer.forceToMono=true;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
