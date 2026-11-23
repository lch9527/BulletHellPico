using System;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace HandFleet
{
    // A hull keeps its imported materials and only breaks into the same armor pieces seen in flight.
    sealed class FleetEnemyViews
    {
        sealed class Hull
        {
            public Transform Root, Model;
            public Renderer[] Plates;
            public Vector3[] Rest, Velocity, Spin;
            public Quaternion[] Rotation;
            public LineRenderer Warning, Crack;
            public TrailRenderer Wake;
            public EnemyFamily Family;
            public int Id, Struck, LastSurface;
            public float FlashUntil, BreakAge, Roll;
        }
        readonly Transform parent;
        readonly HandFleetRun run;
        readonly FleetArtAssets art;
        readonly Material warning, trail;
        readonly Hull[] slots;
        readonly Stack<Hull>[] pool;
        readonly List<Hull> debris=new();
        readonly MaterialPropertyBlock flash=new(),charred=new();
        bool reduced;
        public FleetEnemyViews(Transform root,HandFleetRun simulation,FleetArtAssets assets,Material signal,Material streak)
        {
            parent=root;run=simulation;art=assets;warning=signal;trail=streak;
            slots=new Hull[run.Enemies.Length];pool=new Stack<Hull>[Enum.GetValues(typeof(EnemyFamily)).Length];
            for(int i=0;i<pool.Length;i++)pool[i]=new Stack<Hull>();
            flash.SetColor("_BaseColor",new Color(1,.92f,.75f));flash.SetColor("_EmissionColor",new Color(2,1.5f,1.15f));
            charred.SetColor("_BaseColor",new Color(.038f,.019f,.028f));charred.SetColor("_EmissionColor",new Color(.035f,.008f,.002f));
            run.Feedback+=OnCue;
        }
        public void Dispose(){run.Feedback-=OnCue;}
        Hull Create(EnemyFamily family)
        {
            var h=new Hull{Family=family,Root=FleetGeometry.Root(parent,family.ToString())};
            var model=art&&art.Enemies!=null&&(int)family<art.Enemies.Length?art.Enemies[(int)family]:null;
            h.Model=model?Object.Instantiate(model,h.Root,false).transform:FleetGeometry.Shape(h.Root,"Hull",PrimitiveType.Sphere,Vector3.zero,Vector3.one,warning);
            h.Plates=h.Model.GetComponentsInChildren<MeshRenderer>();
            float radius=HandFleetRun.EnemyRadius(new FleetEnemy{Family=family});
            if(!model)h.Model.localScale=Vector3.one*(2*radius);
            int count=h.Plates.Length;h.Rest=new Vector3[count];h.Rotation=new Quaternion[count];h.Velocity=new Vector3[count];h.Spin=new Vector3[count];
            for(int i=0;i<count;i++){var p=h.Plates[i].transform;h.Rest[i]=p.localPosition;h.Rotation[i]=p.localRotation;}
            h.Warning=FleetGeometry.Line(h.Root,"Charge",warning,.002f,32,true);FleetGeometry.Ring(h.Warning,radius*1.12f);
            h.Crack=FleetGeometry.Line(h.Root,"Fractured seam",warning,.0018f,4);h.Crack.gameObject.SetActive(false);
            if(family==EnemyFamily.Comet||family==EnemyFamily.Follower){
                h.Wake=h.Root.gameObject.AddComponent<TrailRenderer>();h.Wake.sharedMaterial=trail;
                h.Wake.time=family==EnemyFamily.Follower?.22f:.33f;h.Wake.minVertexDistance=.008f;
                h.Wake.startWidth=radius*.52f;h.Wake.endWidth=0;h.Wake.startColor=new Color(1,.11f,.39f,.9f);h.Wake.endColor=new Color(.46f,.12f,.85f,0);
                h.Wake.emitting=false;
            }
            return h;
        }
        void Recycle(Hull h)
        {
            if(h.Wake){h.Wake.emitting=false;h.Wake.Clear();}
            h.Root.gameObject.SetActive(false);
            for(int i=0;i<h.Plates.Length;i++){
                h.Plates[i].transform.SetLocalPositionAndRotation(h.Rest[i],h.Rotation[i]);h.Plates[i].SetPropertyBlock(null);
            }
            pool[(int)h.Family].Push(h);
        }
        Hull Bind(int i,FleetEnemy e)
        {
            var h=slots[i];
            if(h!=null&&h.Id==e.Id&&h.Family==e.Family)return h;
            if(h!=null)Recycle(h);
            var free=pool[(int)e.Family];h=free.Count>0?free.Pop():Create(e.Family);
            h.Id=e.Id;h.Struck=0;h.FlashUntil=-1;h.LastSurface=-1;h.Roll=0;h.Root.localScale=Vector3.one;
            h.Crack.gameObject.SetActive(false);h.Root.gameObject.SetActive(true);slots[i]=h;Pose(h,e,0);return h;
        }
        void Pose(Hull h,FleetEnemy e,float dt)
        {
            var heading=e.Family==EnemyFamily.Follower&&e.Velocity.sqrMagnitude>.001f?e.Velocity:run.Motor.Position-e.Position;
            if(e.Family==EnemyFamily.Spindle)h.Roll=e.Age*32;
            if(e.Family==EnemyFamily.Tumbler)h.Roll=Mathf.MoveTowards(h.Roll,e.RollDegrees,dt*420);
            h.Root.SetLocalPositionAndRotation(e.Position,FleetOrdnance.Facing(heading)*Quaternion.AngleAxis(h.Roll,Vector3.forward));
            h.Warning.gameObject.SetActive(e.State==EnemyState.Telegraph);
            if(h.Wake)h.Wake.emitting=!reduced&&(e.Family==EnemyFamily.Follower||e.State==EnemyState.Attacking);
        }
        void Surface(Hull h,FleetEnemy e)
        {
            if(h.Plates.Length==0)return;
            bool critical=e.Health<=e.MaxHealth*.5f;
            int state=h.FlashUntil>run.Clock?2:critical?1:0;
            if(h.LastSurface==state)return;h.LastSurface=state;
            h.Plates[h.Struck].SetPropertyBlock(state==2?flash:critical?charred:null);
            h.Crack.gameObject.SetActive(critical);
            if(critical){
                var r=h.Plates[h.Struck];var b=r.localBounds;
                for(int n=0;n<4;n++){
                    var edge=b.center+Vector3.Scale(b.extents,new Vector3((n-1.5f)*.53f,n%2==0?.13f:-.12f,.94f));
                    h.Crack.SetPosition(n,h.Root.InverseTransformPoint(r.transform.TransformPoint(edge)));
                }
            }
        }
        // Select the authored landmark in hull space so a persistent scar stays on that part as the ship banks.
        static int DamagePlate(Hull h)
        {
            var landmark=h.Family switch {
                EnemyFamily.Tumbler=>new Vector3(.7f,.65f,.65f),
                EnemyFamily.Bell=>new Vector3(.15f,-.65f,.45f),
                EnemyFamily.Needle or EnemyFamily.Comet=>new Vector3(0,.26f,.85f),
                EnemyFamily.Lancer or EnemyFamily.Spindle=>new Vector3(.65f,.25f,.4f),
                EnemyFamily.Grenadier or EnemyFamily.Gatekeeper=>new Vector3(-.65f,.15f,.45f),
                EnemyFamily.Skipper or EnemyFamily.Sniper=>new Vector3(.1f,.6f,.5f),
                EnemyFamily.Shutter=>new Vector3(.2f,-.45f,.55f),
                _=>new Vector3(.15f,.12f,.72f)
            };
            var target=landmark*HandFleetRun.EnemyRadius(new FleetEnemy{Family=h.Family});
            int best=0;float nearest=float.PositiveInfinity;
            for(int i=0;i<h.Plates.Length;i++){
                var center=h.Root.InverseTransformPoint(h.Plates[i].bounds.center);
                float distance=(center-target).sqrMagnitude;
                if(distance<nearest){nearest=distance;best=i;}
            }
            return best;
        }
        void OnCue(FleetCombatEvent cue)
        {
            if(cue.Kind==FleetCue.Start){Clear();return;}
            if(cue.Kind!=FleetCue.Kill&&cue.Kind!=FleetCue.EnemyHit)return;
            for(int i=0;i<slots.Length;i++){
                var e=run.Enemies[i];if(e.Id!=cue.Source||cue.Source==0)continue;
                var h=Bind(i,e);if(h.Plates.Length==0)return;
                if(cue.Kind==FleetCue.EnemyHit){
                    h.Struck=DamagePlate(h);
                    h.FlashUntil=run.Clock+(reduced?.045f:.09f);h.LastSurface=-1;Surface(h,e);
                }else{
                    slots[i]=null;Pose(h,e,0);h.Warning.gameObject.SetActive(false);h.Crack.gameObject.SetActive(false);
                    if(h.Wake)h.Wake.emitting=false;
                    if(debris.Count>=slots.Length){Recycle(debris[0]);debris.RemoveAt(0);}
                    h.BreakAge=0;debris.Add(h);
                    for(int n=0;n<h.Plates.Length;n++){
                        var plate=h.Plates[n];plate.SetPropertyBlock(null);
                        var outward=h.Root.InverseTransformPoint(plate.bounds.center).normalized;
                        var direction=plate.name=="Pulse rotor"?Vector3.down*1.8f:BreakDirection(h.Family,outward,n);
                        var velocity=direction*HandFleetRun.EnemyRadius(e)*2.8f;
                        h.Velocity[n]=h.Root.TransformDirection(velocity);h.Spin[n]=new Vector3(63+n*18,81-n*21,117+n*13);
                    }
                }
                return;
            }
        }
        static Vector3 BreakDirection(EnemyFamily family,Vector3 p,int i)
        {
            switch(family){
                case EnemyFamily.Tumbler:return p.magnitude<.1f?Vector3.down:p+new Vector3(Mathf.Sign(p.x)*.5f,0,0);
                case EnemyFamily.Spindle:case EnemyFamily.Frigate:return p+Vector3.Cross(Vector3.forward,p)*1.4f;
                case EnemyFamily.Bell:case EnemyFamily.Sentry:return p*.65f+Vector3.down*(.5f+.13f*i);
                case EnemyFamily.Carrier:return new Vector3(-p.x,-.6f-Mathf.Abs(p.y),p.z);
                case EnemyFamily.Battleship:case EnemyFamily.Lancer:case EnemyFamily.Gatekeeper:return new Vector3(p.x*2.2f,p.y*.55f,p.z);
                case EnemyFamily.Comet:case EnemyFamily.Follower:return p+Vector3.back;
                case EnemyFamily.Shutter:return new Vector3(p.x*.5f,p.y*2,p.z);
                case EnemyFamily.Needle:case EnemyFamily.Sniper:case EnemyFamily.Skipper:return p+Vector3.forward*p.z;
                default:return p*1.45f;
            }
        }
        void Clear(){
            foreach(var h in debris)Recycle(h);debris.Clear();
            for(int i=0;i<slots.Length;i++)if(slots[i]!=null){Recycle(slots[i]);slots[i]=null;}
        }
        public void Present(float dt,bool reduce)
        {
            reduced=reduce;
            if(run.State==RunState.Calibration){Clear();return;}
            for(int i=0;i<slots.Length;i++){
                var e=run.Enemies[i];
                if(!e.Active){if(slots[i]!=null){Recycle(slots[i]);slots[i]=null;}continue;}
                var h=Bind(i,e);Pose(h,e,dt);Surface(h,e);
            }
            if(run.State==RunState.Results)dt=Mathf.Min(Time.unscaledDeltaTime,.05f);
            for(int i=debris.Count-1;i>=0;i--){
                var h=debris[i];h.BreakAge+=dt;float life=reduced?.32f:.66f;
                if(h.BreakAge>=life){Recycle(h);debris.RemoveAt(i);continue;}
                for(int n=0;n<h.Plates.Length;n++){
                    var p=h.Plates[n].transform;p.position+=h.Velocity[n]*dt;p.Rotate(h.Spin[n]*dt,Space.Self);
                }
                h.Root.localScale=Vector3.one*(1-Mathf.SmoothStep(0,.97f,Mathf.InverseLerp(.28f,life,h.BreakAge)));
            }
        }
    }
}
