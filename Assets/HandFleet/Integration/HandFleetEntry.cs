using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HandFleet.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace HandFleet.Integration
{
    public sealed partial class HandFleetEntry : MonoBehaviour
    {
        public HandFleetSettings Settings;
        public HandFleetMission Mission;
        public FleetArtAssets Art;
        // Only used when HandFleet runs without MainMenu. PicoBuild swaps any "StandalonePlayer" field for the portable
        // UnityXR player in -Pico.apk builds, so this name is what makes a HandFleet-only PICO APK get the right rig.
        [FormerlySerializedAs("PlayerPrefab")] public GameObject StandalonePlayer;
        public Material Ivory,Graphite,Cyan,Amber,Lime,Pink,PanelMaterial,EngineTrail;
        public MetaPlayerRig Player { get; private set; }
        public HandFleetRun Run { get; private set; }
        public Transform Arena { get; private set; }
        public IHandFleetInputProvider InputProvider { get; set; }
        // Explicit development harnesses must not write synthetic scores or preferences.
        public bool TestRun { get; set; }
        public FleetInput LastInput { get; private set; }
        public bool Ready=>Run!=null;
        public static bool PreviewAutoStart;
        FleetVisuals visuals;
        MetaFleetInput metaInput; DesktopFleetInput desktopInput;
        readonly List<FleetControl> controls=new();
        readonly List<MetaPinchSelector> selectors=new();
        readonly List<Material> ownedMaterials=new();
        TextMesh title,status,hint; Transform console,consoleFace,quitFill;
        FleetControl quit; FleetSlider volumeSlider; float quitHold,previewAt;
        FleetControl primary;
        FleetEffects effects; FleetVRPresentation presentation;
        string ScoreKey=>"HandFleet."+Mission.ScoreVersion+".Best";
        bool focused=true,reduced,alternate,resultsSaved,pendingAssist;
        // HandFleet is the whole app: there is no main menu, so the exit control quits.
        const bool standalone=true;
        string ExitHint=>standalone?"or hold QUIT.":"or return to Main Menu.";
        float volume=.35f,uiAt,bindAt,inputAt,calibrationTimer=-1;
        bool contactCapture,waitRelease=true;
        string message="Reach out and grab the drone with either hand to launch.";
        // Grab-to-launch: either hand closes (pinch or fist) on the parked drone and the flight starts at once.
        const float GrabRadius=.14f,GrabReach=.05f,FistCurl=.55f,ParkDelay=1.5f;
        readonly bool[] grabArmed=new bool[2];
        float resultsAt=-1; bool parked=true;
        public bool AwaitingGrab=>Run!=null&&parked&&calibrationTimer<0&&(Run.State==RunState.Calibration||Run.State==RunState.Results);
        int best,bestAssisted,lastRank;
        ArcadeLeaderboard scores=new(); ArcadePresentation arcade; FleetHandHider hands;
        string TableKey=>ScoreKey+".Table";
        // One sentence per line; the console still wraps anything wider than the panel.
        const string ArcadeBrief="Point your ship at an enemy: it locks on and fires.\nDodge with your hand: one hit ends the run.\nFly through green rings to upgrade your guns.";

        IEnumerator Start()
        {
            Player=MetaPlayerRig.Shared;
            if(!Player)
            {
                var root=Instantiate(StandalonePlayer);Player=root.GetComponentInChildren<MetaPlayerRig>(true);
                Player.Initialize(Player.XRRig,Player.DesktopCamera);
            }
            if(Player.IsXR)
            {
                float deadline=Time.realtimeSinceStartup+5;
                while(Player.ViewCamera.transform.position.y-Player.FloorHeight<.4f&&Time.realtimeSinceStartup<deadline)yield return null;
            }
            presentation=new FleetVRPresentation(Player.gameObject);
            Arena=FleetGeometry.Root(transform,"HandFleet play area");PlaceArena();
            // Copy only colors that preferences change; shared asset materials remain untouched.
            Cyan=Own(Cyan);Amber=Own(Amber);Lime=Own(Lime);
            Run=new HandFleetRun(Settings,Mission);
            Run.Calibration.Dominant=PlayerPrefs.GetInt("HandFleet.Hand",1);
            Run.Calibration.Sensitivity=PlayerPrefs.GetFloat("HandFleet.Sensitivity",1);
            Run.Calibration.Reach=PlayerPrefs.GetFloat("HandFleet.Reach",.55f);
            Run.AssistEnabled=!Mission.GestureCampaign && !Mission.Arcade && (PlayerPrefs.GetInt("HandFleet.Assist",0)!=0 || !Player.IsXR);
            reduced=PlayerPrefs.GetInt("HandFleet.Reduced",0)!=0;alternate=PlayerPrefs.GetInt("HandFleet.Palette",0)!=0;volume=PlayerPrefs.GetFloat("HandFleet.Volume",.35f);
            best=PlayerPrefs.GetInt(ScoreKey,0);bestAssisted=PlayerPrefs.GetInt(ScoreKey+".Assisted",0);ApplyPalette();
            if(Mission.Arcade){scores=ArcadeLeaderboard.Parse(PlayerPrefs.GetString(TableKey,""));best=Mathf.Max(best,scores.Best);}
            visuals=Arena.gameObject.AddComponent<FleetVisuals>();visuals.Settings=Settings;visuals.Art=Art;
            visuals.Ivory=Ivory;visuals.Graphite=Graphite;visuals.Cyan=Cyan;visuals.Amber=Amber;visuals.Lime=Lime;visuals.Pink=Pink;visuals.Trail=EngineTrail;visuals.Build(Run);
            BuildScenery();BuildConsole();effects=Arena.gameObject.AddComponent<FleetEffects>();effects.Build(Run,Art,Cyan,Amber,Lime,Ivory);
            if(Player.IsXR) {hands=new FleetHandHider(Player.XRRig.transform);metaInput=new MetaFleetInput(Player,Arena);InputProvider??=metaInput;Application.targetFrameRate=90;if(OVRManager.display!=null)OVRManager.display.displayFrequency=90;}
            else {desktopInput=new DesktopFleetInput();InputProvider??=desktopInput;}
            if(Run.GestureRules)Arena.gameObject.AddComponent<GesturePresentation>().Build(Run,Player.ViewCamera.transform,Cyan,Amber);
            else if(Run.ArcadeRules){arcade=Arena.gameObject.AddComponent<ArcadePresentation>();arcade.Build(Run,Player.ViewCamera.transform);arcade.Leaderboard=scores.Display(0);}
            Run.Feedback+=OnFeedback;
            inputAt=Time.unscaledTime+.5f;LastInput=InputProvider.Read();
            if(PreviewAutoStart&&!Player.IsXR) {PreviewAutoStart=false;Run.Start(LastInput);}
            Debug.Log("HANDFLEET_READY xr="+Player.IsXR+" pooledBullets="+Run.Bullets.Length);
        }
        Material Own(Material source) {var m=new Material(source);ownedMaterials.Add(m);return m;}
        void PlaceArena()
        {
            var view=Player.ViewCamera.transform;
            if(Player.IsXR)
            {
                var forward=Vector3.ProjectOnPlane(view.forward,Vector3.up).normalized;if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
                var position=view.position+forward*.45f-Vector3.up*.35f;
                Arena.SetPositionAndRotation(position,Quaternion.LookRotation(forward));Player.ViewCamera.backgroundColor=new Color(.012f,.021f,.045f,1);
            }
            else
            {
                Arena.SetPositionAndRotation(new Vector3(0,1.1f,0),Quaternion.identity);
                view.position=Arena.TransformPoint(new Vector3(0,.25f,-1.05f));view.LookAt(Arena.TransformPoint(new Vector3(0,0,.65f)));
                Player.ViewCamera.fieldOfView=65;Player.ViewCamera.backgroundColor=new Color(.03f,.055f,.085f);
            }
        }
        void BuildScenery()
        {
            if(Mission.GestureCampaign||Mission.Arcade)
            {
                var cage=FleetGeometry.Root(Arena,"Surrounding combat chamber");
                for(int i=0;i<16;i++)
                {
                    float a=i*Mathf.PI/8;var panel=FleetGeometry.Shape(cage,"Chamber panel "+i,PrimitiveType.Cube,new Vector3(Mathf.Sin(a)*3,0,Mathf.Cos(a)*3),new Vector3(1.12f,3,.035f),Graphite);
                    panel.localRotation=Quaternion.Euler(0,i*22.5f,0);
                    FleetGeometry.Shape(cage,"Depth marker "+i,PrimitiveType.Sphere,new Vector3(Mathf.Sin(a)*2.95f,.8f,Mathf.Cos(a)*2.95f),Vector3.one*.025f,Cyan);
                }
            }
            if(Art&&Art.Arena){Instantiate(Art.Arena,Arena,false);return;}
            var backdrop=FleetGeometry.Root(Arena,"Sparse orbital cradle - open passthrough");
            for(int side=-1;side<=1;side+=2)
            {
                FleetGeometry.Shape(backdrop,"Perimeter rib",PrimitiveType.Cube,new Vector3(side*.9f,.15f,1.8f),new Vector3(.035f,1.1f,.04f),Graphite);
                FleetGeometry.Shape(backdrop,"Ivory dock foot",PrimitiveType.Cube,new Vector3(side*.9f,-.4f,1.8f),new Vector3(.25f,.03f,.12f),Ivory);
                FleetGeometry.Shape(backdrop,"Navigation marker",PrimitiveType.Cube,new Vector3(side*.9f,.68f,1.8f),new Vector3(.08f,.015f,.025f),Cyan);
            }
            var boundary=FleetGeometry.Line(backdrop,"Arena perimeter",Graphite,.008f,4,true);
            boundary.SetPosition(0,new Vector3(-.85f,-.35f,1.9f));boundary.SetPosition(1,new Vector3(.85f,-.35f,1.9f));boundary.SetPosition(2,new Vector3(.85f,.65f,1.9f));boundary.SetPosition(3,new Vector3(-.85f,.65f,1.9f));
        }
        void BuildConsole()
        {
            var panel=FleetGeometry.Root(Arena,"Flight service console");console=panel;PlaceConsole();
            if (Run.ArcadeRules) { BuildFlightDeck(); return; }
            consoleFace=FleetGeometry.Shape(panel,"Opaque navy console",PrimitiveType.Cube,Vector3.zero,new Vector3(1.3f,.65f,.018f),PanelMaterial);
            title=Label(panel,"HANDFLEET",new Vector3(-.60f,.275f,-.02f),.038f,new Color(.34f,.9f,.95f));
            status=Label(panel,"",new Vector3(-.60f,.205f,-.02f),.030f,Color.white);
            // Anchored at its top so extra lines grow down towards the controls (LayoutConsole moves them if needed).
            hint=Label(panel,"",new Vector3(-.60f,HintTop,-.02f),.024f,new Color(.72f,.81f,.86f));hint.anchor=TextAnchor.UpperLeft;
            // In XR the flight starts by grabbing the drone, so the start button exists only for desktop preview.
            primary=Control(panel,0,"START",Primary,()=>!Player.IsXR||!Idle);
            Control(panel,1,"RETRY",()=>{Run.Abort();parked=true;message="Grab the drone to launch a new flight.";},()=>Run.State==RunState.Paused);
            // The object keeps the MAIN MENU name (tests press it by name); only a HandFleet-only APK relabels it. There it quits
            // on a 1 s hold: one pinch used to quit at once, which read as a crash (PICO 2026-11-21: a pinch, then
            // "Quit requested" 11 ms later). Pinned to the top-right corner, away from the parked ship below the console.
            var exit=Control(panel,2,"MAIN MENU",()=>Application.Quit(),()=>true,.3f,standalone?ControlKind.Hold:ControlKind.Press);
            exit.Pinned=true;
            if(standalone){exit.Label.text="HOLD TO QUIT";quit=exit;quitFill=FleetGeometry.Shape(exit.transform,"Hold progress",PrimitiveType.Cube,Vector3.zero,Vector3.one,Amber);quitFill.gameObject.SetActive(false);}
            // The hand that grabs the drone becomes the flying hand; the toggle remains for desktop preview.
            Control(panel,3,"HAND",()=>{Run.Calibration.Dominant=1-Run.Calibration.Dominant;Run.Calibration.Ready=false;SavePreferences();},()=>!Player.IsXR&&SettingsVisible());
            Control(panel,4,"REACH",()=>{Run.Calibration.Reach=Run.Calibration.Reach<.45f?.55f:Run.Calibration.Reach<.6f?.7f:.4f;SavePreferences();},SettingsVisible);
            Control(panel,5,"SENSITIVITY",()=>{Run.Calibration.Sensitivity=Run.Calibration.Sensitivity<.9f?1:Run.Calibration.Sensitivity<1.2f?1.35f:.75f;SavePreferences();},SettingsVisible);
            Control(panel,6,"FUSION ASSIST",()=>{Run.AssistEnabled=!Run.AssistEnabled;SavePreferences();},SettingsVisible);
            Control(panel,7,"PALETTE",()=>{alternate=!alternate;ApplyPalette();SavePreferences();},SettingsVisible,.33f);
            Control(panel,8,"FLASHES",()=>{reduced=!reduced;SavePreferences();},SettingsVisible,.33f);
            // Pinch the slider and sweep sideways; each 5% step plays a short preview at the new level, release saves it.
            var volumeControl=Control(panel,9,"VOLUME",null,SettingsVisible,.54f,ControlKind.Slider);
            BindVolume(volumeControl);
            Control(panel,10,"CALIBRATE FUSION",()=>BeginCapture(true),SettingsVisible);
            Control(panel,11,"RECALIBRATE AIM",()=>{Run.Pause();BeginCapture(false);},()=>Run.State==RunState.Paused);
            Control(panel,12,"CONTINUE LEFT",()=>Run.RecoverSolo(LastInput,0),()=>Run.State==RunState.TrackingHold);
            Control(panel,13,"CONTINUE RIGHT",()=>Run.RecoverSolo(LastInput,1),()=>Run.State==RunState.TrackingHold);
            Control(panel,14,"RESTORE FUSION",()=>Run.RestoreFusion(LastInput),()=>Run.State==RunState.TrackingHold&&Run.RecoveryChoiceRequired);
            Control(panel,15,"TOGGLE FUSION",()=>pendingAssist=true,()=>Run.State==RunState.Running&&Run.AssistEnabled);
            Control(panel,16,"SKIP PRACTICE",()=>Run.SkipFusionPractice(),()=>Run.State==RunState.Running&&Run.Stage.Kind==StageKind.FusionPractice);
            Control(panel,17,"REST / PAUSE",()=>Run.Pause(),()=>Run.State==RunState.Running);
        }
        void BindVolume(FleetControl volumeControl)
        {
            volumeSlider=volumeControl.gameObject.AddComponent<FleetSlider>();
            volumeSlider.CanStart=()=>CanPress()&&SettingsVisible();
            volumeSlider.Changed=v=>{volume=v;volumeControl.Label.text=VolumeText;if(Time.unscaledTime>=previewAt){previewAt=Time.unscaledTime+.12f;effects.PreviewVolume(volume);}};
            volumeSlider.Committed=v=>{volume=v;SavePreferences();Debug.Log("HANDFLEET_BUTTON "+VolumeText);};
            volumeSlider.Build(-.08f,.25f,Ivory,Cyan,Ivory,Cyan,Amber,()=>Player.IsXR?Player.XRRig.GetComponentsInChildren<Oculus.Interaction.RayInteractor>(true):Array.Empty<Oculus.Interaction.RayInteractor>());
            volumeSlider.SetValue(volume);
        }
        // XR: the console stands just below eye level and turns to face the eye. It used to hang 0.89 m under the eye, upright,
        // which put it at knee height for a seated player. Desktop keeps the old pose because the preview camera frames it.
        // ConsoleDrop is to the console origin; the visible panel's centre sits ~0.15 m above it (title at the top, one control
        // row in arcade), so that centre lands ~11 degrees below the eye line.
        const float ConsoleDrop=.33f,ConsoleDepth=.5f;
        public static Pose ConsolePose(Vector3 arenaEye)
        {
            var position=new Vector3(0,arenaEye.y-ConsoleDrop,ConsoleDepth);
            return new Pose(position,Quaternion.LookRotation(position-arenaEye,Vector3.up));
        }
        void PlaceConsole()
        {
            if(!Player.IsXR){console.localPosition=new Vector3(0,-.54f,.55f);console.localRotation=Quaternion.identity;return;}
            var pose=ConsolePose(Arena.InverseTransformPoint(Player.ViewCamera.transform.position));console.localPosition=pose.position;console.localRotation=pose.rotation;
        }
        bool SettingsVisible()=>Run.State==RunState.Calibration||Run.State==RunState.Paused||Run.State==RunState.Results;
        bool Idle=>Run.State==RunState.Calibration||Run.State==RunState.Results;
        // Controls are created in slot order (controls[slot]); LayoutConsole places the visible ones.
        FleetControl Control(Transform parent,int slot,string text,Action action,Func<bool> visible,float width=.39f,ControlKind kind=ControlKind.Press)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=text;go.transform.SetParent(parent,false);
            go.transform.localPosition=new Vector3(0,.025f,-.035f);go.transform.localScale=new Vector3(width,.052f,.016f);
            go.GetComponent<Renderer>().sharedMaterial=Graphite;go.GetComponent<Collider>().isTrigger=true;
            var button=go.AddComponent<FleetControl>();button.Width=width;button.Kind=kind;button.Visible=visible;
            button.Action=()=>{if(!CanPress()||action==null)return;Debug.Log("HANDFLEET_BUTTON "+button.Label.text);action();inputAt=Time.unscaledTime+.35f;waitRelease=true;};
            if(kind==ControlKind.Hold)button.HoldAction=action;
            var labelRoot=FleetGeometry.Root(go.transform,"Label");labelRoot.localScale=new Vector3(1/width,1/.052f,1/.016f);
            button.Label=Label(labelRoot,text,new Vector3(-width/2+.015f,0,-.013f),.024f,Color.white);
            // A pinch on a hold or slider control is not a press: those act from Update / FleetSlider.
            Action press=kind==ControlKind.Press?button.Press:()=>{};
            go.AddComponent<MetaMenuButton>().Initialize(press,()=>CanPress()&&visible(),(hover,selected)=>{button.Hovered=hover;button.Held=selected;button.Label.color=selected?Color.yellow:hover?Color.cyan:Color.white;},new Vector3(0,0,-.51f),Quaternion.identity);
            controls.Add(button);return button;
        }
        static readonly int[] GestureHidden={6,10,11,14,15,16};
        // Visible controls flow left to right in slot order and wrap only when a row is full, so hidden slots leave no
        // gaps (arcade XR: PALETTE, FLASHES, VOLUME on one row). The pinned exit sits on the title row, top right.
        // The panel ends just under the last row instead of keeping room for all six.
        void LayoutConsole()
        {
            const float Inner=1.24f,Gap=.02f,RowStep=.064f,FirstRow=.025f,Top=.325f;
            float x=-Inner/2,y=Mathf.Min(FirstRow,HintTop-hintLines*HintLine-.035f);
            foreach(var c in controls)
            {
                if(!c.gameObject.activeSelf)continue;
                if(c.Pinned){c.transform.localPosition=new Vector3(Inner/2-c.Width/2,.275f,-.035f);continue;}
                if(x>-Inner/2&&x+c.Width>Inner/2+.001f){x=-Inner/2;y-=RowStep;}
                c.transform.localPosition=new Vector3(x+c.Width/2,y,-.035f);x+=c.Width+Gap;
            }
            float bottom=y-.055f;
            consoleFace.localPosition=new Vector3(0,(Top+bottom)/2,0);consoleFace.localScale=new Vector3(1.3f,Top-bottom,.018f);
        }
        string VolumeText=>$"VOLUME {Mathf.RoundToInt(volume*100)}%";
        // Reaching for the parked ship points the hand ray at the console behind it, so a grab pinch could press a
        // button (the 2026-11-21 accidental quit was a pinch right after the ship went down). Clicks wait until both
        // hands are clear of the ship.
        const float NearShip=.25f;
        bool HandNearShip()
        {
            if(!Player.IsXR||!AwaitingGrab)return false;
            for(int h=0;h<2;h++){var hand=LastInput.Hand(h);if(hand.Valid&&Vector3.Distance(hand.Palm.position,Run.Motor.Position)<NearShip)return true;}
            return false;
        }
        const float QuitHoldSeconds=1f;
        void UpdateQuitHold(float dt)
        {
            if(!quit)return;
            bool held=quit.gameObject.activeInHierarchy&&quit.Held&&CanPress();
            quitHold=held?quitHold+dt:0;float p=Mathf.Clamp01(quitHold/QuitHoldSeconds);
            quitFill.gameObject.SetActive(p>0);quitFill.localPosition=new Vector3(-.5f+p/2,0,-.56f);quitFill.localScale=new Vector3(p,1,.1f);
            if(p<1)return;
            quitHold=0;Debug.Log("HANDFLEET_BUTTON HOLD TO QUIT");quit.HoldAction?.Invoke();
        }
        static TextMesh Label(Transform parent,string text,Vector3 pos,float size,Color color)
        {
            var t=FleetGeometry.Root(parent,text);t.localPosition=pos;var label=t.gameObject.AddComponent<TextMesh>();
            label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.GetComponent<Renderer>().sharedMaterial=label.font.material;
            label.text=text;label.characterSize=size*10/64;label.fontSize=64;label.anchor=TextAnchor.MiddleLeft;label.color=color;return label;
        }
        bool CanPress()=>Run!=null&&calibrationTimer<0&&!waitRelease&&Time.unscaledTime>=inputAt&&!HandNearShip();
        void Primary()
        {
            switch(Run.State)
            {
                case RunState.Calibration:case RunState.Results:Launch(Run.Calibration.Dominant);break;
                case RunState.Running:Run.Pause();break;
                case RunState.Paused:if(!Run.Calibration.Ready)Run.Recalibrate(LastInput);Run.Resume();break;
                case RunState.TrackingHold:if(Run.GestureRules)Run.RecoverSolo(LastInput,Run.Calibration.Dominant);else Run.RestoreFusion(LastInput);break;
            }
        }
        bool restartCapture;
        void BeginCapture(bool contact)
        {
            restartCapture=Run.State==RunState.Calibration||Run.State==RunState.Results;
            if(!contact&&Run.State==RunState.Paused&&Run.Shields<=0)restartCapture=true;
            contactCapture=contact;calibrationTimer=3;
            message=contact?"Gently touch palms, fingers aligned forward. Hold until capture.":"Relax your chosen hand in handshake pose. Point fingers forward.";
        }
        public void RestartRun() {resultsSaved=false;lastRank=0;if(arcade)arcade.Leaderboard=scores.Display(0);Run.Start(LastInput);}
        // Starts immediately from the grabbing hand's current pose: that pose becomes neutral aim, no countdown.
        void Launch(int hand)
        {
            Run.Calibration.Dominant=hand;
            if(!LastInput.Focused||!LastInput.Hand(hand).Valid){message="Bring your hand into view, then grab the drone.";return;}
            RestartRun();if(Run.State!=RunState.Running)return;
            SavePreferences();parked=false;resultsAt=-1;grabArmed[0]=grabArmed[1]=false;
            message=Run.ArcadeRules?ArcadeBrief:"Protect the cyan core. Aim with your hand; weapons fire automatically. Back of hand: shield. Palm outward: laser above 80% energy.";
        }
        static bool Grabbing(FleetHand hand)
        {
            if(!hand.Valid)return false;if(hand.Pinching)return true;
            int n=0;float curl=0;for(int f=0;f<4;f++)if((hand.ConfidentFingers&(1<<f))!=0){n++;curl+=hand.Curl(f);}
            return n>=3&&curl/n>=FistCurl;
        }
        void UpdateGrab()
        {
            if(!Idle){parked=false;resultsAt=-1;return;}
            if(Run.State==RunState.Results)
            {
                // Let the defeat effect play where the drone fell, then return it to the launch cradle.
                if(resultsAt<0)resultsAt=Time.unscaledTime;
                if(!parked&&Time.unscaledTime-resultsAt>=ParkDelay){Run.ParkDrone();parked=true;grabArmed[0]=grabArmed[1]=false;}
            }
            if(!Player.IsXR||!AwaitingGrab)return;
            for(int h=0;h<2;h++)
            {
                var hand=LastInput.Hand(h);bool grab=Grabbing(hand);
                // A grab must begin open so a hand that is still clenched from the last flight never relaunches by accident.
                if(!grab){grabArmed[h]=hand.Valid;continue;}
                if(!grabArmed[h])continue;
                var reach=hand.Palm.position+hand.Forward*GrabReach;
                if(Vector3.Distance(reach,Run.Motor.Position)>GrabRadius&&Vector3.Distance(hand.Palm.position,Run.Motor.Position)>GrabRadius)continue;
                Launch(h);return;
            }
        }
        void Update()
        {
            if(!Ready)return;
            float dt=Time.unscaledDeltaTime;
            if(metaInput!=null)metaInput.Focused=focused;
            if(desktopInput!=null)desktopInput.Focused=focused;
            if(Time.unscaledTime>=bindAt&&Player.IsXR)
            {
                bindAt=Time.unscaledTime+1;
                foreach(var ray in Player.XRRig.GetComponentsInChildren<Oculus.Interaction.RayInteractor>(true))
                {var s=MetaPinchSelector.BindToRay(ray);if(s&&!selectors.Contains(s)){s.SuppressSelection=null;selectors.Add(s);}}
            }
            // XR presses arrive as ISDK Select events, one per pinch per hand, so they need no release gate. Requiring every
            // hand to be open let a relaxed idle hand (its pinch latch stays down below 4.5 cm) block the other hand's
            // clicks for as long as it stayed curled: 60 s on PICO 2026-11-21, 8 right-hand pinches ignored.
            bool released=Mouse.current==null||!Mouse.current.leftButton.isPressed;
            if(released&&Time.unscaledTime>=inputAt)waitRelease=false;
            DesktopMenu();UpdateQuitHold(dt);
            var snapshot=InputProvider.Read();snapshot.ToggleAssist|=pendingAssist;pendingAssist=false;LastInput=snapshot;
            var keys=Keyboard.current;
            if(!Player.IsXR&&keys!=null&&(keys.pKey.wasPressedThisFrame||keys.escapeKey.wasPressedThisFrame)) {if(Run.State==RunState.Paused)Run.Resume();else Run.Pause();}
            if(calibrationTimer>=0)
            {
                calibrationTimer-=dt;
                if(calibrationTimer<0)
                {
                    if(contactCapture)message=Run.Calibration.CalibrateContact(LastInput)?"Fusion calibrated. Resume, then join in fusion practice or any combat encounter.":"Could not read both aligned palms. Try again; solo play is always available.";
                    else if(!LastInput.Focused||!LastInput.Hand(Run.Calibration.Dominant).Valid)message="Bring your chosen hand into view, then try calibration again.";
                    else if(restartCapture){RestartRun();message="Protect the cyan core. Aim with your hand; weapons fire automatically. Back of hand: shield. Palm outward: laser above 80% energy.";}
                    else {Run.Recalibrate(LastInput);message="Aim recalibrated. Resume when ready.";}
                    inputAt=Time.unscaledTime+.35f;waitRelease=true;
                }
            }
            else {Run.Tick(LastInput,dt);UpdateGrab();}
            // Hands disappear while flying and come back when the ship is parked, crashed, paused or the hand is lost.
            hands?.Apply(Run.State==RunState.Running||Run.State==RunState.Recovering);
            visuals.Present(Run,LastInput,Run.State==RunState.Running?dt:0,reduced);effects.Present(Run,reduced,volume);
            if(Run.State==RunState.Results&&!resultsSaved)SaveResult();
            if(Time.unscaledTime>=uiAt){uiAt=Time.unscaledTime+.1f;UpdateConsole();}
        }
        void DesktopMenu()
        {
            if(Player.IsXR||Mouse.current==null)return;
            var ray=Player.ViewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());FleetControl hover=null;
            if(Physics.Raycast(ray,out var hit,20,~0,QueryTriggerInteraction.Collide))hover=hit.collider.GetComponent<FleetControl>();
            var button=Mouse.current.leftButton;
            if(desktopInput!=null)desktopInput.SuppressPointer=hover!=null||volumeSlider.Dragging;
            foreach(var c in controls){c.Label.color=c==hover?Color.cyan:Color.white;c.Hovered=c==hover;c.Held=c==hover&&c.Kind==ControlKind.Hold&&button.isPressed;}
            if(hover&&hover.Kind==ControlKind.Press&&button.wasPressedThisFrame)hover.Press();
            volumeSlider.Mouse(ray,hover&&hover.Kind==ControlKind.Slider&&button.wasPressedThisFrame,button.isPressed);
        }
        void UpdateConsole()
        {
            if (Run.ArcadeRules) { UpdateFlightDeck(); return; }
            bool held=Run.State==RunState.TrackingHold||Run.State==RunState.Recovering;
            string mode=Run.Fusion.Heavy?"ATLAS / FUSED":"KESTREL / SOLO";
            title.text="HANDFLEET   /   "+mode+(Run.Assisted?"   ASSISTED":"");
            string shields=new string('|',Mathf.Max(0,Run.Shields));
            status.text=$"SHIELDS {shields}    {Run.Score:000000}  x{Run.Multiplier}    LASER {(Run.LaserRemaining>0?"FIRING":Run.LaserReady?"READY":Run.LaserCharge+"/"+Settings.LaserKills)}";
            if(calibrationTimer>=0)hint.text=$"{message}\nCapture in {Mathf.CeilToInt(calibrationTimer)}...";
            else if(Run.State==RunState.Results)hint.text=$"{(Run.Won?"MISSION COMPLETE":"CORE LOST")}  |  Score {Run.Score}  |  Best {(Run.Assisted?bestAssisted:best)}\n{(Player.IsXR?"Grab the drone to fly again, "+ExitHint:"Start a new flight, "+ExitHint)}";
            else if(held)hint.text=Run.State==RunState.Recovering?$"Hands ready. Resuming in {Mathf.CeilToInt(Run.RecoveryTime)}...":Run.HoldTime<Settings.PromptDelay?"":Run.RecoveryChoiceRequired?"Tracking paused. Restore touching palms or choose a valid solo hand.":"Tracking paused. Bring your controlling hand into view.";
            else if(Run.State==RunState.Running)hint.text=Run.Stage.Kind==StageKind.FusionPractice?"FUSION PRACTICE: pause to calibrate touching palms, or use assist.\nSolo is always valid. Skip practice or rest whenever you like.":$"{Run.Stage.Title}  /  {(Run.Stage.Completion==EncounterEnd.ClearEnemies?"CLEAR TARGETS":Mathf.CeilToInt(Mathf.Max(0,Run.Stage.Duration-Run.StageTime))+"s")}\n{(Run.Stage.Kind==StageKind.Rest?"Rest your arms. Pause for as long as you need.":Run.Stage.Instruction??"Steer the cyan core. Fingers: attack. Back: shield. Palm: laser.")}";
            else hint.text=(Run.State==RunState.Paused?"PAUSED. ":"")+message;
            primary.Label.text=Run.State==RunState.Running?"PAUSE":Run.State==RunState.Paused?"RESUME":held?(Run.GestureRules?"CONTINUE":"RESTORE FUSION"):Run.State==RunState.Results?"NEW FLIGHT":"START";
            // One pass, so a control hidden by the mode is never switched on and back off (that re-registered its
            // interactables ten times a second).
            var hidden=Run.GestureRules?GestureHidden:null;
            for(int i=0;i<controls.Count;i++){bool show=controls[i].Visible()&&(hidden==null||Array.IndexOf(hidden,i)<0);if(controls[i].gameObject.activeSelf!=show)controls[i].gameObject.SetActive(show);}
            if(Run.GestureRules){title.text="HANDFLEET / "+Run.Mode;status.text=$"HULL {shields}  ENERGY {Run.Energy:F0}%  HEAT {Run.ShieldHeat:P0}  COOLDOWN {Run.ShieldCooldown:F1}s";}
            controls[3].Label.text="HAND: "+(Run.Calibration.Dominant==0?"LEFT":"RIGHT");controls[4].Label.text=$"REACH: {Run.Calibration.Reach:F2}m";
            controls[5].Label.text=$"SENSITIVITY: {Run.Calibration.Sensitivity:F2}";controls[6].Label.text="FUSION ASSIST: "+(Run.AssistEnabled?"ON":"OFF");
            controls[7].Label.text="PALETTE: "+(alternate?"ALTERNATE":"STANDARD");controls[8].Label.text="FLASHES: "+(reduced?"REDUCED":"NORMAL");
            if(!volumeSlider.Dragging)volumeSlider.SetValue(volume);controls[9].Label.text=VolumeText;
            // Every branch above writes the unwrapped hint; wrap it to the panel once per change.
            var raw=hint.text;if(raw!=hintRaw){hintRaw=raw;hintWrapped=Wrap(hint,raw,HintWidth,out hintLines);}hint.text=hintWrapped;
            LayoutConsole();
        }
        // TextMesh never wraps, so a long hint ran past the panel's right edge. Words wrap at the panel's inner width,
        // measured with the font's own glyph advances.
        const float HintTop=.18f,HintWidth=1.18f,HintLine=.028f;
        string hintRaw,hintWrapped="";int hintLines=1;
        public static string Wrap(TextMesh label,string text,float width,out int lines)
        {
            var font=label.font;font.RequestCharactersInTexture(text+" ",label.fontSize,label.fontStyle);
            float unit=label.characterSize*.1f;
            float Advance(char c)=>font.GetCharacterInfo(c,out var info,label.fontSize,label.fontStyle)?info.advance*unit:label.fontSize*unit*.55f;
            var result=new System.Text.StringBuilder(text.Length+8);lines=0;float space=Advance(' ');
            foreach(var paragraph in text.Split('\n'))
            {
                if(lines++>0)result.Append('\n');
                float x=0;bool lineStart=true;
                foreach(var word in paragraph.Split(' '))
                {
                    float w=0;foreach(var c in word)w+=Advance(c);
                    if(!lineStart&&x+space+w>width){result.Append('\n');lines++;x=0;lineStart=true;}
                    if(!lineStart){result.Append(' ');x+=space;}
                    result.Append(word);x+=w;lineStart=false;
                }
            }
            return result.ToString();
        }
        void ApplyPalette(){Cyan.color=alternate?new Color(.45f,.7f,1):new Color(.337f,.902f,.949f);Amber.color=alternate?new Color(1,.4f,.65f):new Color(1,.702f,.298f);Lime.color=alternate?Color.white:new Color(.718f,.941f,.416f);}
        void SavePreferences()
        {
            if(TestRun)return;
            PlayerPrefs.SetInt("HandFleet.Hand",Run.Calibration.Dominant);PlayerPrefs.SetFloat("HandFleet.Sensitivity",Run.Calibration.Sensitivity);PlayerPrefs.SetFloat("HandFleet.Reach",Run.Calibration.Reach);
            PlayerPrefs.SetInt("HandFleet.Assist",Run.AssistEnabled?1:0);PlayerPrefs.SetInt("HandFleet.Reduced",reduced?1:0);PlayerPrefs.SetInt("HandFleet.Palette",alternate?1:0);PlayerPrefs.SetFloat("HandFleet.Volume",volume);PlayerPrefs.Save();
        }
        void SaveResult()
        {
            if(TestRun){resultsSaved=true;return;}
            resultsSaved=true;
            if(Run.ArcadeRules)
            {
                lastRank=scores.Insert(Run.Score,DateTime.Now.ToString("yyyy-MM-dd"));PlayerPrefs.SetString(TableKey,scores.Serialize());
                if(arcade)arcade.Leaderboard=scores.Display(lastRank);
            }
            if(Run.Assisted){bestAssisted=Mathf.Max(bestAssisted,Run.Score);PlayerPrefs.SetInt(ScoreKey+".Assisted",bestAssisted);}else{best=Mathf.Max(best,Run.Score);PlayerPrefs.SetInt(ScoreKey,best);}PlayerPrefs.Save();
            Debug.Log($"HANDFLEET_RESULT won={Run.Won} score={Run.Score} hits={Run.Hits} assisted={Run.Assisted} seconds={Run.Clock:F1}");
        }
        void OnFeedback(FleetCombatEvent cue){effects.Cue(cue,Run);}
        void OnApplicationFocus(bool value){focused=value;if(!value&&Run!=null)Run.HoldTracking();}
        void OnApplicationPause(bool value){focused=!value;if(value&&Run!=null)Run.HoldTracking();}
        void OnDestroy()
        {
            hands?.Restore();
            if(Run!=null)Run.Feedback-=OnFeedback;
            foreach(var m in ownedMaterials)if(m)Destroy(m);
            presentation?.Dispose();
        }
    }
    public enum ControlKind { Press, Hold, Slider }
    public sealed class FleetControl : MonoBehaviour
    {
        public TextMesh Label;public Action Action,HoldAction;public Func<bool> Visible;
        public float Width=.39f;public ControlKind Kind;public bool Pinned;
        // Pointer state from MetaMenuButton feedback (XR) or DesktopMenu (mouse).
        public bool Hovered,Held;
        public void Press(){if(isActiveAndEnabled&&(Visible==null||Visible()))Action?.Invoke();}
    }
}
