using UnityEngine;

namespace HandFleet.Integration
{
    public sealed partial class HandFleetEntry
    {
        public GameObject FlightDeckPrefab;
        Transform housing, launchCue, settingsDrawer;
        TextMesh bestValue, gunValue, resultSummary;
        FleetControl settingsKey, paletteKey, flashesKey, volumeKey, retryKey;
        bool settingsOpen;
        RunState previousDeckState;

        void BuildFlightDeck()
        {
            housing = Instantiate(FlightDeckPrefab, console, false).transform;
            housing.name = "Needle Flight Deck";
            housing.localPosition = new Vector3(-.44f, -.14f, 0);
            if (!Player.IsXR) console.localPosition = new Vector3(0, .08f, -.27f);
            launchCue = housing.Find("Launch cue");
            var ivory = new Color(1f,.94f,.82f);
            bestValue = Label(housing, "000000", new Vector3(-.031f,.300f,-.009f), .033f, ivory);
            gunValue = Label(housing, "1", new Vector3(.004f,.252f,-.009f), .034f, ivory);

            settingsKey = DeckKey(housing, "SETTINGS", new Vector3(-.012f,.169f,-.023f), .24f,
                () => { settingsOpen = !settingsOpen; UpdateFlightDeck(); }, SettingsVisible, true);
            quit = DeckKey(housing, "MAIN MENU", new Vector3(-.024f,.094f,-.023f), .24f,
                Application.Quit, SettingsVisible, true, ControlKind.Hold);
            quit.Label.text = "HOLD TO QUIT";
            quitFill = FleetGeometry.Shape(quit.transform,"Hold progress",PrimitiveType.Cube,Vector3.zero,Vector3.one,Amber);
            quitFill.gameObject.SetActive(false);

            settingsDrawer = FleetGeometry.Root(console,"Settings drawer");
            settingsDrawer.localPosition = new Vector3(.20f,.09f,-.018f);
            FleetGeometry.Shape(settingsDrawer,"Settings face",PrimitiveType.Cube,Vector3.zero,
                new Vector3(.60f,.39f,.016f),PanelMaterial);
            Label(settingsDrawer,"SETTINGS",new Vector3(-.27f,.157f,-.018f),.031f,ivory);
            paletteKey = DeckKey(settingsDrawer,"PALETTE",new Vector3(0,.092f,-.022f),.54f,
                () => { alternate = !alternate; ApplyPalette(); SavePreferences(); }, () => settingsOpen);
            flashesKey = DeckKey(settingsDrawer,"FLASHES",new Vector3(0,.029f,-.022f),.54f,
                () => { reduced = !reduced; SavePreferences(); }, () => settingsOpen);
            volumeKey = DeckKey(settingsDrawer,"VOLUME",new Vector3(0,-.034f,-.022f),.54f,
                null, () => settingsOpen, false, ControlKind.Slider);
            BindVolume(volumeKey);
            var brief = Label(settingsDrawer,ArcadeBrief,new Vector3(-.27f,-.074f,-.02f),.018f,Color.white);
            brief.anchor = TextAnchor.UpperLeft;
            brief.text = Wrap(brief,ArcadeBrief,.54f,out _);

            primary = DeckKey(console,"START",new Vector3(.13f,-.17f,-.016f),.23f,Primary,
                () => !settingsOpen && (!Player.IsXR || Run.State==RunState.Paused));
            retryKey = DeckKey(console,"RETRY",new Vector3(.38f,-.17f,-.016f),.23f,
                () => { Run.Abort(); parked=true; }, () => !settingsOpen && Run.State==RunState.Paused);
            resultSummary = Label(console,"",new Vector3(-.012f,.13f,-.018f),.029f,ivory);
            previousDeckState = Run.State;
            UpdateFlightDeck();
        }

        FleetControl DeckKey(Transform parent,string caption,Vector3 position,float width,System.Action action,
            System.Func<bool> visible,bool modeled=false,ControlKind kind=ControlKind.Press)
        {
            var control = Control(parent,controls.Count,caption,action,visible,width,kind);
            control.transform.localPosition = position;
            control.Label.anchor = TextAnchor.MiddleCenter;
            control.Label.transform.localPosition = new Vector3(0,0,-.013f);
            if (kind==ControlKind.Slider)
            {
                control.Label.anchor = TextAnchor.MiddleLeft;
                control.Label.transform.localPosition = new Vector3(-width/2+.015f,0,-.013f);
            }
            if (modeled) control.GetComponent<Renderer>().enabled=false;
            return control;
        }

        void UpdateFlightDeck()
        {
            if (Run.State!=previousDeckState) settingsOpen=false;
            previousDeckState=Run.State;
            bool paused=Run.State==RunState.Paused, results=Run.State==RunState.Results;
            settingsDrawer.gameObject.SetActive(settingsOpen);
            settingsKey.Label.text=settingsOpen?"BACK":"SETTINGS";
            paletteKey.Label.text=alternate?"PALETTE: ALTERNATE":"PALETTE: STANDARD";
            flashesKey.Label.text=reduced?"FLASHES: REDUCED":"FLASHES: NORMAL";
            if (!volumeSlider.Dragging) volumeSlider.SetValue(volume);
            volumeKey.Label.text=VolumeText;
            bestValue.text=Mathf.Max(best,Run.Score).ToString("000000");
            gunValue.text=(Run.WeaponLevel+1).ToString();
            primary.Label.text=paused?"RESUME":results?"NEW FLIGHT":"START";
            primary.gameObject.SetActive(primary.Visible());
            retryKey.gameObject.SetActive(retryKey.Visible());
            launchCue.gameObject.SetActive(!settingsOpen && Run.State==RunState.Calibration);
            if(launchCue.gameObject.activeSelf)
            {
                // Project the parked ship onto the panel plane, so the authored arrow lands by it in either rig.
                var eye=housing.InverseTransformPoint(Player.ViewCamera.transform.position);
                var ship=housing.InverseTransformPoint(Arena.TransformPoint(Run.Motor.Position));
                var ray=ship-eye;
                if(ray.z>.001f)
                {
                    var target=eye+ray*(-eye.z/ray.z);
                    launchCue.localPosition=target+new Vector3(-.479f,-.31f+.045f,0);
                }
            }
            resultSummary.gameObject.SetActive(!settingsOpen && (results||paused));
            if (results) resultSummary.text=$"{(Run.Won?(Run.Endless?"BOSS DEFEATED, SHIP DOWN":"FLEET DESTROYED"):"SHIP DOWN")}\nSCORE {Run.Score:000000}\n{(lastRank>0?"High score #"+lastRank:"Wave "+Run.Wave)}";
            else if (paused) resultSummary.text="PAUSED";
        }
    }
}
