using UnityEngine;

namespace HandFleet
{
    public static class FleetGeometry
    {
        public static Transform Shape(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent,false); go.transform.localPosition=position; go.transform.localScale=scale;
            var collider=go.GetComponent<Collider>(); collider.enabled=false;
            if(Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
            var r=go.GetComponent<Renderer>(); r.sharedMaterial=material; r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false;
            return go.transform;
        }
        public static Transform Root(Transform parent,string name) { var t=new GameObject(name).transform;t.SetParent(parent,false);return t; }
        public static LineRenderer Line(Transform parent,string name,Material material,float width,int count,bool loop=false)
        {
            var t=Root(parent,name); var line=t.gameObject.AddComponent<LineRenderer>(); line.sharedMaterial=material; line.useWorldSpace=false;
            line.startWidth=line.endWidth=width; line.positionCount=count;line.loop=loop;line.numCapVertices=2;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return line;
        }
        public static void Ring(LineRenderer line,float radius)
        { for(int i=0;i<line.positionCount;i++){float a=i*Mathf.PI*2/line.positionCount;line.SetPosition(i,new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*radius);} }
    }
    public sealed class FleetVisuals : MonoBehaviour
    {
        public Material Ivory,Graphite,Cyan,Amber,Lime,Pink,Trail;
        TrailRenderer[] trails=System.Array.Empty<TrailRenderer>(); bool trailing;
        LineRenderer aimLine,aimGlow; Transform aimDot; Renderer aimDotRenderer; MaterialPropertyBlock aimBlock; Material aimMaterial; Texture2D aimTexture;
        Transform lockBox; LineRenderer[] lockCorners; int lockedId=-1; float lockedAt;
        LineRenderer warpOuter,warpInner; static readonly Color WarpViolet=new Color(.78f,.38f,1f);
        static readonly Color LaserRed=new Color(1f,.08f,.05f), LaserHot=new Color(1f,.55f,.5f);
        const float AimLength=3f;
        public HandFleetSettings Settings;
        public FleetArtAssets Art;
        FleetProjectileViews projectiles; FleetEnemyViews fleet;
        Transform ship,bank,core,secondHull,target;
        Transform[,] fingers=new Transform[2,5]; Transform[] engines=new Transform[2];
        Transform[] pickupViews;
        Renderer coreRenderer;
        LineRenderer tether,join;
        Quaternion cosmetic=Quaternion.identity;
        public void Build(HandFleetRun run)
        {
            ship=FleetGeometry.Root(transform,"Authoritative ship core");
            bank=FleetGeometry.Root(ship,"Cosmetic bank around core");
            core=FleetGeometry.Shape(ship,"Damage core - radius 2 cm",PrimitiveType.Sphere,Vector3.zero,Vector3.one*Settings.CoreRadius*2,Cyan);
            coreRenderer=core.GetComponent<Renderer>();
            BuildHull(0);secondHull=BuildHull(1);secondHull.gameObject.SetActive(false);
            BuildTrails();
            if(run.ArcadeRules) BuildAim();
            target=FleetGeometry.Shape(transform,"Command target",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.008f,Cyan);
            tether=FleetGeometry.Line(transform,"Bounded follower tether",Cyan,.003f,2);
            join=FleetGeometry.Line(ship,"Fusion progress arc",Cyan,.003f,32);
            projectiles=new FleetProjectileViews(transform,run.Bullets.Length,Pink,Amber,Cyan,Ivory);
            fleet=new FleetEnemyViews(transform,run,Art,Amber,Trail?Trail:Pink);
            pickupViews=new Transform[run.Pickups.Length];
            for(int i=0;i<pickupViews.Length;i++)
            {var line=FleetGeometry.Line(transform,"Hollow multiplier diamond",Lime,.006f,4,true);FleetGeometry.Ring(line,.035f);pickupViews[i]=line.transform;pickupViews[i].gameObject.SetActive(false);}
        }
        Transform BuildHull(int index)
        {
            if(Art&&Art.Kestrel)
            {
                var model=Instantiate(Art.Kestrel,bank,false).transform;model.name=index==0?"Kestrel":"Atlas mirrored hull";
                model.localPosition=new Vector3(index==0?.035f:-.035f,0,0);model.localScale=new Vector3(index==0?1:-1,1,1);
                for(int i=0;i<5;i++)fingers[index,i]=model.Find("F"+i);engines[index]=model.Find("Engine");Remap(model);return model;
            }
            var hull=FleetGeometry.Root(bank,index==0?"Kestrel ivory hull":"Atlas mirrored hull");float side=index==0?1:-1;
            hull.localPosition=new Vector3(side*.035f,0,0);
            // The core occupies a real opening between structural ribs; armor never covers it.
            FleetGeometry.Shape(hull,"Upper spine",PrimitiveType.Cube,new Vector3(0,.06f,0),new Vector3(.065f,.025f,.12f),Ivory);
            FleetGeometry.Shape(hull,"Lower spine",PrimitiveType.Cube,new Vector3(0,-.065f,0),new Vector3(.065f,.02f,.12f),Graphite);
            FleetGeometry.Shape(hull,"Wrist engine housing",PrimitiveType.Cylinder,new Vector3(0,0,-.08f),new Vector3(.075f,.025f,.075f),Graphite).localRotation=Quaternion.Euler(90,0,0);
            engines[index]=FleetGeometry.Shape(hull,"Thrust nozzle",PrimitiveType.Cylinder,new Vector3(0,0,-.107f),new Vector3(.022f,.008f,.022f),Ivory);engines[index].localRotation=Quaternion.Euler(90,0,0);
            for(int f=0;f<5;f++)
            {
                var pivot=FleetGeometry.Root(hull,f==4?"Thumb stabilizer":"Finger chain "+f);fingers[index,f]=pivot;
                pivot.localPosition=f==4?new Vector3(side*.007f,.084f,-.012f):new Vector3(0,.046f-f*.031f,.035f);
                for(int joint=0;joint<3;joint++)
                    FleetGeometry.Shape(pivot,"Plate "+joint,PrimitiveType.Cube,new Vector3(0,0,.02f+joint*.036f),new Vector3(.061f,f==4?.019f:.025f,.034f),Ivory);
                FleetGeometry.Shape(pivot,"Tip accent",PrimitiveType.Cube,new Vector3(0,0,.111f),new Vector3(.039f,.009f,.004f),Cyan);
            }
            return hull;
        }
        // Engine exhaust: a thin core streak plus a wider, shorter glow from the main hull's nozzle, drawn in world space so it streams behind every move.
        void BuildTrails()
        {
            var nozzle=engines[0]?engines[0]:bank;
            var material=Trail?Trail:Cyan;
            trails=new[]{Exhaust(nozzle,"Engine trail core",material,.009f,.45f,.9f),Exhaust(nozzle,"Engine trail glow",material,.024f,.18f,.35f)};
        }
        TrailRenderer Exhaust(Transform nozzle,string name,Material material,float width,float life,float alpha)
        {
            var root=FleetGeometry.Root(nozzle,name);root.localPosition=Art?Vector3.zero:new Vector3(0,-.01f,0);
            var trail=root.gameObject.AddComponent<TrailRenderer>();trail.sharedMaterial=material;trail.time=life;trail.minVertexDistance=.004f;
            trail.widthMultiplier=width;trail.widthCurve=new AnimationCurve(new Keyframe(0,1),new Keyframe(1,0));
            var cyan=Cyan?Cyan.color:new Color(.34f,.9f,.95f);
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.Lerp(cyan,Color.white,.6f),0),new GradientColorKey(cyan,.35f),new GradientColorKey(cyan*.6f,1)},new[]{new GradientAlphaKey(alpha,0),new GradientAlphaKey(alpha*.5f,.4f),new GradientAlphaKey(0,1)});
            trail.colorGradient=gradient;trail.numCapVertices=2;trail.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;trail.receiveShadows=false;
            trail.alignment=LineAlignment.View;trail.emitting=false;return trail;
        }
        // Laser sight: a red additive beam (hot thin core inside a soft glow) from the centre gun to the first enemy inside the aim cone, with a glowing dot where it lands.
        void BuildAim()
        {
            // The additive trail material is untextured, which draws a flat-edged ribbon; give the beam its own copy with a soft
            // cross-section so it reads as light: bright centre falling off smoothly to transparent edges.
            var source=Trail?Trail:Cyan;
            aimTexture=new Texture2D(1,32,TextureFormat.RGBA32,false){name="Laser cross-section",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            for(int y=0;y<32;y++){float d=Mathf.Abs((y+.5f)/32f*2-1);float a=Mathf.Exp(-d*d*6)*(1-d);aimTexture.SetPixel(0,y,new Color(1,1,1,a));}
            aimTexture.Apply(false,true);
            aimMaterial=new Material(source){name="Laser aim (runtime)"};
            if(aimMaterial.HasProperty("_BaseMap"))aimMaterial.SetTexture("_BaseMap",aimTexture);
            if(aimMaterial.HasProperty("_MainTex"))aimMaterial.SetTexture("_MainTex",aimTexture);
            if(aimMaterial.HasProperty("_BaseColor"))aimMaterial.SetColor("_BaseColor",Color.white);
            aimGlow=Beam("Laser aim glow",.012f);
            aimLine=Beam("Laser aim core",.0035f);
            aimDot=FleetGeometry.Shape(transform,"Laser aim dot",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.012f,source);
            aimDotRenderer=aimDot.GetComponent<Renderer>();aimBlock=new MaterialPropertyBlock();
            aimDot.gameObject.SetActive(false);
            // Lock-on box: four red corner brackets around the locked enemy, always facing the player's eyes.
            lockBox=FleetGeometry.Root(transform,"Lock-on box");lockCorners=new LineRenderer[4];
            for(int c=0;c<4;c++)
            {
                var corner=FleetGeometry.Line(lockBox,"Lock corner "+c,aimMaterial,1,3);
                corner.alignment=LineAlignment.View;corner.textureMode=LineTextureMode.Stretch;corner.numCapVertices=0;corner.numCornerVertices=2;corner.receiveShadows=false;
                lockCorners[c]=corner;
            }
            lockBox.gameObject.SetActive(false);
            // Warp warning: a violet portal where the next formation will appear.
            warpOuter=FleetGeometry.Line(transform,"Warp warning outer",aimMaterial,1,40,true);warpInner=FleetGeometry.Line(warpOuter.transform,"Warp warning inner",aimMaterial,1,40,true);
            foreach(var ring in new[]{warpOuter,warpInner}){ring.alignment=LineAlignment.View;ring.numCapVertices=0;ring.receiveShadows=false;}
            warpOuter.gameObject.SetActive(false);
        }
        // The outer ring collapses onto the warp point while the inner one opens, meeting as the formation arrives.
        void PresentWarp(HandFleetRun run,bool flying,Vector3 head)
        {
            if(!warpOuter) return;
            bool show=flying&&run.WarpPending;
            if(warpOuter.gameObject.activeSelf!=show)warpOuter.gameObject.SetActive(show);
            if(!show) return;
            float k=run.WarpProgress,ease=k*k*(3-2*k);var view=run.WarpPoint-head;float distance=view.magnitude;
            warpOuter.transform.localPosition=run.WarpPoint;
            warpOuter.transform.localRotation=distance>1e-4f?Quaternion.LookRotation(view/distance,Vector3.up)*Quaternion.Euler(0,0,Time.unscaledTime*90):Quaternion.identity;
            FleetGeometry.Ring(warpOuter,Mathf.Lerp(.32f,.1f,ease));FleetGeometry.Ring(warpInner,Mathf.Lerp(.01f,.1f,ease));
            float width=.004f+distance*.004f,pulse=.75f+.25f*Mathf.Sin(Time.unscaledTime*22);
            warpOuter.widthMultiplier=width;warpInner.widthMultiplier=width*1.4f;
            warpOuter.startColor=warpOuter.endColor=Alpha(WarpViolet,Mathf.Lerp(.35f,1,k)*pulse);
            warpInner.startColor=warpInner.endColor=Alpha(Color.Lerp(WarpViolet,Color.white,.5f),k*pulse);
        }
        // Snaps in from 1.8x size over 0.12 s whenever a new enemy is locked; line width grows with distance so the box stays readable far away.
        void PresentLockBox(HandFleetRun run,int target,Vector3 head)
        {
            bool show=target>=0;
            if(lockBox.gameObject.activeSelf!=show)lockBox.gameObject.SetActive(show);
            if(!show){lockedId=-1;return;}
            var e=run.Enemies[target];
            if(e.Id!=lockedId){lockedId=e.Id;lockedAt=Time.unscaledTime;}
            float t=Mathf.Clamp01((Time.unscaledTime-lockedAt)/.12f);
            float half=HandFleetRun.EnemyRadius(e)*1.35f*Mathf.Lerp(1.8f,1,t*t*(3-2*t)),arm=half*.45f;
            var view=e.Position-head;float distance=view.magnitude;
            lockBox.localPosition=e.Position;lockBox.localRotation=distance>1e-4f?Quaternion.LookRotation(view/distance,Vector3.up):Quaternion.identity;
            float pulse=.8f+.2f*Mathf.Sin(Time.unscaledTime*18);
            var color=Alpha(LaserHot,Mathf.Lerp(.4f,1,t)*pulse);
            for(int c=0;c<4;c++)
            {
                float sx=(c&1)==0?-1:1,sy=(c&2)==0?-1:1;var corner=new Vector3(sx*half,sy*half,0);
                var line=lockCorners[c];line.widthMultiplier=.003f+distance*.004f;line.startColor=line.endColor=color;
                line.SetPosition(0,corner-new Vector3(sx*arm,0,0));line.SetPosition(1,corner);line.SetPosition(2,corner-new Vector3(0,sy*arm,0));
            }
        }
        LineRenderer Beam(string name,float width)
        {
            var line=FleetGeometry.Line(transform,name,aimMaterial,width,2);
            line.alignment=LineAlignment.View;line.textureMode=LineTextureMode.Stretch;line.numCapVertices=0;line.receiveShadows=false;
            line.gameObject.SetActive(false);return line;
        }
        void PresentAim(HandFleetRun run,bool flying,Vector3 head)
        {
            if(!aimLine) return;
            aimLine.gameObject.SetActive(flying);aimGlow.gameObject.SetActive(flying);
            if(!flying) { aimDot.gameObject.SetActive(false); PresentLockBox(run,-1,head); return; }
            // Same range the guns use, so the box appears exactly when they would fire.
            float range=run.AimAt(Settings.ArenaRadius,out int target,out var direction);
            if(target<0)range=AimLength;
            PresentLockBox(run,target,head);
            var forward=run.Motor.Rotation*Vector3.forward;var muzzle=run.Motor.Position+forward*HandFleetRun.GunMuzzle;var end=muzzle+direction*range;
            aimLine.SetPosition(0,muzzle);aimLine.SetPosition(1,end);aimGlow.SetPosition(0,muzzle);aimGlow.SetPosition(1,end);
            // Searching: a dimmer red that fades out at full length. Locked: the beam snaps onto the target, brightens and stays solid to the dot.
            bool locked=target>=0;
            float pulse=locked?.85f+.15f*Mathf.Sin(Time.unscaledTime*30):1;
            aimLine.startColor=Alpha(LaserHot,(locked?1:.75f)*pulse);aimLine.endColor=Alpha(LaserHot,locked?pulse:0);
            aimGlow.startColor=Alpha(LaserRed,(locked?.9f:.55f)*pulse);aimGlow.endColor=Alpha(LaserRed,locked?.9f*pulse:0);
            aimDot.gameObject.SetActive(locked);
            if(locked){aimDot.localPosition=end;aimDot.localScale=Vector3.one*.012f*pulse;aimBlock.SetColor("_BaseColor",Alpha(LaserHot,1));aimDotRenderer.SetPropertyBlock(aimBlock);}
        }
        static Color Alpha(Color c,float a)=>new Color(c.r,c.g,c.b,a);
        void Remap(Transform model)
        {
            foreach(var r in model.GetComponentsInChildren<Renderer>())
            {var m=r.sharedMaterial;if(!m)continue;if(m.name=="Cyan")r.sharedMaterial=Cyan;else if(m.name=="Amber")r.sharedMaterial=Amber;else if(m.name=="Lime")r.sharedMaterial=Lime;}
        }
        void OnDestroy(){fleet?.Dispose();projectiles?.Dispose();if(aimMaterial)Destroy(aimMaterial);if(aimTexture)Destroy(aimTexture);}
        public void Present(HandFleetRun run,FleetInput input,float dt,bool reduced)
        {
            // A parked drone hovers gently in its cradle to invite the launch grab.
            bool parked=run.State==RunState.Calibration;
            ship.localPosition=run.Motor.Position+(parked?Vector3.up*Mathf.Sin(Time.unscaledTime*2.2f)*.01f:Vector3.zero);ship.localRotation=run.Motor.Rotation;
            bank.localScale=new Vector3(run.Calibration.Dominant==0?-1:1,1,1);
            var a=Quaternion.Inverse(run.Motor.Rotation)*run.Motion.Acceleration;
            var desired=Quaternion.Euler(Mathf.Clamp(-a.z*.6f,-Settings.MaxPitch,Settings.MaxPitch),0,Mathf.Clamp(-a.x*1.5f,-Settings.MaxBank,Settings.MaxBank));
            cosmetic=Quaternion.Slerp(cosmetic,desired,1-Mathf.Exp(-dt*12));bank.localRotation=cosmetic;
            secondHull.gameObject.SetActive(run.Fusion.Blend>0);
            secondHull.localPosition=new Vector3(-.035f-(1-run.Fusion.Blend)*.06f,0,0);
            secondHull.localScale=new Vector3(Art?-1:1,1,1)*Mathf.Max(.001f,run.Fusion.Blend);
            for(int h=0;h<2;h++)
            {
                var hand=input.Hand(h==0?run.Calibration.Dominant:1-run.Calibration.Dominant);
                for(int f=0;f<5;f++)
                {
                    float curl=hand.Valid?hand.Curl(f):0;
                    var rotation=Quaternion.Euler(-curl*45,0,Mathf.Clamp(a.x,-1,1)*Settings.FinOpening*(f-1.5f)/4);
                    fingers[h,f].localRotation=Quaternion.Slerp(fingers[h,f].localRotation,rotation,1-Mathf.Exp(-dt*12));
                }
                if(!Art)engines[h].localScale=new Vector3(.022f,.008f+Mathf.Min(.02f,run.Motion.Velocity.magnitude*.012f),.022f);
            }
            // The exhaust streams only in flight; parking or crashing cuts it and wipes the old streak so it never smears back to the cradle.
            bool flying=run.State==RunState.Running||run.State==RunState.Recovering;
            if(flying!=trailing){trailing=flying;foreach(var t in trails){t.emitting=flying;if(!flying)t.Clear();}}
            PresentAim(run,flying,input.HeadPosition);PresentWarp(run,flying,input.HeadPosition);
            core.localScale=Vector3.one*Settings.CoreRadius*2; // Never pulse the apparent damage radius.
            coreRenderer.sharedMaterial=run.Clock<run.InvulnerableUntil?Ivory:Cyan;
            target.localPosition=run.Motor.Target;target.gameObject.SetActive(run.Fusion.Heavy);
            tether.gameObject.SetActive(run.Motor.Tethered);tether.SetPosition(0,run.Motor.Position);tether.SetPosition(1,run.Motor.Target);
            join.gameObject.SetActive(run.Fusion.State==FusionState.Preview);
            for(int i=0;i<join.positionCount;i++){float angle=i/(float)(join.positionCount-1)*run.Fusion.Progress*Mathf.PI*2;join.SetPosition(i,new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),-.03f)*.12f);}
            projectiles.Present(run.Bullets);
            fleet.Present(dt,reduced);
            for(int i=0;i<pickupViews.Length;i++)
            {
                var p=run.Pickups[i];var v=pickupViews[i];if(v.gameObject.activeSelf!=p.Active)v.gameObject.SetActive(p.Active);if(!p.Active)continue;
                v.localPosition=p.Position;v.localRotation=Quaternion.Euler(0,run.Clock*35,0);
            }
        }
    }
}
