using UnityEngine;
namespace HandFleet
{
    public sealed class GesturePresentation : MonoBehaviour
    {
        HandFleetRun run;Transform view,console;TextMesh hud,alert;LineRenderer tracer,lockRing;LineRenderer[] boundary;
        float nextText;
        public void Build(HandFleetRun simulation,Transform camera,Material player,Material warning)
        {
            run=simulation;view=camera;console=transform.Find("Flight service console");
            var t=FleetGeometry.Root(transform,"Flight HUD");hud=t.gameObject.AddComponent<TextMesh>();hud.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.GetComponent<Renderer>().sharedMaterial=hud.font.material;hud.fontSize=48;hud.characterSize=.005f;hud.anchor=TextAnchor.MiddleCenter;hud.color=Color.cyan;
            var a=FleetGeometry.Root(transform,"Rear threat indicator");alert=a.gameObject.AddComponent<TextMesh>();alert.font=hud.font;a.GetComponent<Renderer>().sharedMaterial=hud.font.material;alert.fontSize=48;alert.characterSize=.004f;alert.anchor=TextAnchor.MiddleCenter;alert.color=new Color(1,.55f,.1f);
            tracer=FleetGeometry.Line(transform,"Shot tracer",player,.003f,2);
            lockRing=FleetGeometry.Line(transform,"Acquired target",player,.002f,32,true);FleetGeometry.Ring(lockRing,.12f);
            boundary=new LineRenderer[3];
            for(int i=0;i<3;i++){boundary[i]=FleetGeometry.Line(transform,"Lethal flight boundary "+i,warning,.002f,96,true);boundary[i].transform.localRotation=Quaternion.Euler(i==1?90:0,i==2?90:0,0);}
        }
        void LateUpdate()
        {
            if(run==null)return;
            bool live=run.State==RunState.Running;
            bool idle=run.State==RunState.Calibration||run.State==RunState.Results;
            // The menu exists only before launch, after the flight ends, and on an explicit pause; never mid-flight.
            if(console)console.gameObject.SetActive(idle||run.State==RunState.Paused);
            hud.transform.localPosition=run.Motor.Position+Vector3.down*.15f;
            hud.transform.rotation=Quaternion.LookRotation(hud.transform.position-view.position,view.up);
            if(Time.unscaledTime>=nextText)
            {
                nextText=Time.unscaledTime+.1f;
                if(idle)hud.text=run.State==RunState.Results?(run.Won?"MISSION COMPLETE":"CORE LOST")+"\nGRAB THE DRONE TO FLY AGAIN":"GRAB THE DRONE\nTO LAUNCH";
                else if(run.State==RunState.TrackingHold)hud.text="HAND LOST\nBRING YOUR HAND INTO VIEW";
                else if(run.State==RunState.Recovering)hud.text=$"RESUMING IN {Mathf.CeilToInt(run.RecoveryTime)}";
                else hud.text=(run.StageTime<4?run.Stage.Title+"\n":"")+$"{run.Mode}  HULL {run.Shields}\nENERGY {run.Energy:F0}%   HEAT {run.ShieldHeat:P0}"+(run.ShieldCooldown>0?$"\nSHIELD COOLING {run.ShieldCooldown:F1}s":"")+(run.Motor.Position.magnitude+run.Settings.CoreRadius>run.Calibration.Reach-.05f?"\nBOUNDARY - MOVE INWARD":"");
            }
            foreach(var ring in boundary){FleetGeometry.Ring(ring,run.Calibration.Reach);ring.startWidth=ring.endWidth=run.Motor.Position.magnitude>run.Calibration.Reach-.07f?.006f:.001f;}
            tracer.gameObject.SetActive(live&&run.Clock<run.ShotUntil);tracer.SetPosition(0,run.Motor.Position);tracer.SetPosition(1,run.ShotEnd);
            bool danger=false;float side=1;
            foreach(var enemy in run.Enemies)if(enemy.Active&&(enemy.State==EnemyState.Telegraph||enemy.State==EnemyState.Arriving))
            {var relative=view.InverseTransformPoint(transform.TransformPoint(enemy.Position));if(relative.z<=Mathf.Abs(relative.x)*.7f){danger=true;side=relative.x<0?-1:1;break;}}
            alert.gameObject.SetActive(live&&danger);alert.transform.position=view.position+view.forward*.6f+view.right*(side*.28f);alert.transform.rotation=view.rotation;alert.text=side<0?"< THREAT":"THREAT >";
            bool found=false;foreach(var e in run.Enemies)if(e.Active&&e.Id==run.TargetId){found=true;lockRing.transform.localPosition=e.Position;lockRing.transform.rotation=Quaternion.LookRotation(lockRing.transform.position-view.position);FleetGeometry.Ring(lockRing,HandFleetRun.EnemyRadius(e)*1.3f);break;}lockRing.gameObject.SetActive(live&&found);
        }
    }
}
