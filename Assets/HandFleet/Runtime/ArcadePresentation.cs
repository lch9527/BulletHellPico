using UnityEngine;

namespace HandFleet
{
    // Arcade presentation keeps text out of the flight: a scoreboard on the far wall, and a prompt at the ship only while parked or when the hand is lost.
    public sealed class ArcadePresentation : MonoBehaviour
    {
        HandFleetRun run; Transform view,console; TextMesh prompt,board,table,banner;
        float nextText; int bannerWave=-1;
        public string Leaderboard="HIGH SCORES";
        public void Build(HandFleetRun simulation,Transform camera)
        {
            run=simulation; view=camera; console=transform.Find("Flight service console");
            prompt=Text("Ship prompt",Vector3.zero,.005f,48,TextAnchor.MiddleCenter,Color.cyan);
            board=Text("Arena scoreboard",new Vector3(0,.95f,2.6f),.012f,64,TextAnchor.MiddleCenter,new Color(.34f,.9f,.95f));
            // Above the console's top-right corner: at eye height it sat behind the console once that was raised to eye level.
            table=Text("High score table",new Vector3(.95f,.9f,1.7f),.006f,64,TextAnchor.UpperLeft,Color.white);
            table.transform.localRotation=Quaternion.Euler(0,25,0);
            // Wave title, shown in the play window during each wave's opening break.
            banner=Text("Wave banner",Vector3.zero,.02f,72,TextAnchor.MiddleCenter,new Color(.78f,.38f,1f));banner.gameObject.SetActive(false);
        }
        string WaveTitle=>run.BossWave?"BOSS":run.Endless?$"ENDLESS {run.Wave-run.WaveCount-1}":$"WAVE {run.Wave}";
        TextMesh Text(string name,Vector3 position,float size,int font,TextAnchor anchor,Color color)
        {
            var t=FleetGeometry.Root(transform,name); t.localPosition=position;
            var mesh=t.gameObject.AddComponent<TextMesh>(); mesh.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.GetComponent<Renderer>().sharedMaterial=mesh.font.material; mesh.fontSize=font; mesh.characterSize=size; mesh.anchor=anchor; mesh.color=color; return mesh;
        }
        void LateUpdate()
        {
            if(run==null) return;
            bool idle=run.State==RunState.Calibration||run.State==RunState.Results;
            if(console) console.gameObject.SetActive(idle||run.State==RunState.Paused);
            table.gameObject.SetActive(idle);
            bool held=run.State==RunState.TrackingHold||run.State==RunState.Recovering;
            prompt.gameObject.SetActive(run.State==RunState.Results||held);
            prompt.transform.localPosition=run.Motor.Position+Vector3.down*.12f;
            prompt.transform.rotation=Quaternion.LookRotation(prompt.transform.position-view.position,view.up);
            bool breakTitle=run.InWaveBreak&&run.Wave>0;
            if(banner.gameObject.activeSelf!=breakTitle)banner.gameObject.SetActive(breakTitle);
            if(breakTitle)
            {
                // Set when the wave changes, not on the 0.1 s text throttle, so the previous wave's number never flashes up.
                if(bannerWave!=run.Wave){bannerWave=run.Wave;banner.text=WaveTitle;}
                banner.transform.localPosition=run.ViewEye+new Vector3(0,.2f,2.2f);
                banner.transform.rotation=Quaternion.LookRotation(banner.transform.position-view.position,Vector3.up);
                var c=banner.color;c.a=.6f+.4f*Mathf.Sin(Time.unscaledTime*6);banner.color=c;
            }
            if(Time.unscaledTime<nextText) return;
            nextText=Time.unscaledTime+.1f;
            string end=run.Won?(run.Endless?"BOSS DEFEATED - SHIP DOWN":"FLEET DESTROYED"):"SHIP DOWN";
            prompt.text=held?"HAND LOST":run.State==RunState.Results?end+"\nGRAB THE SHIP TO FLY AGAIN":"GRAB THE SHIP";
            board.text=run.State==RunState.Calibration?"":$"{run.Score:000000}"+(run.Wave>0?"\n"+WaveTitle:"");
            table.text=Leaderboard;
        }
    }
}
