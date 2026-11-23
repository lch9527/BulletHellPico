using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace HandFleet.Integration
{
    // Hides the player's tracked hand meshes and pinch-ray visuals while the ship is flying, so the ship reads as the thing in your hand.
    // Only Renderer.forceRenderingOff is touched (the SDK owns renderer.enabled), and every renderer gets back exactly the flag it had,
    // because the rig is shared with the main menu and other modes.
    public sealed class FleetHandHider
    {
        readonly Transform rig;
        readonly Dictionary<Renderer,bool> saved=new();
        float scanAt;
        public bool Hidden { get; private set; }
        public FleetHandHider(Transform rig) { this.rig=rig; }
        public void Apply(bool hide)
        {
            if(!rig) return;
            if(!hide) { if(Hidden) Restore(); return; }
            // Rescan now and then: the SDK can create hand or ray visuals after the scene starts.
            if(!Hidden || Time.unscaledTime>=scanAt) { scanAt=Time.unscaledTime+1; Collect<HandVisual>(); Collect<RayInteractor>(); }
            foreach(var renderer in saved.Keys) if(renderer) renderer.forceRenderingOff=true;
            Hidden=true;
        }
        void Collect<T>() where T:Component
        {
            foreach(var owner in rig.GetComponentsInChildren<T>(true))
                foreach(var renderer in owner.GetComponentsInChildren<Renderer>(true))
                    if(!saved.ContainsKey(renderer)) saved.Add(renderer,renderer.forceRenderingOff);
        }
        public void Restore()
        {
            foreach(var pair in saved) if(pair.Key) pair.Key.forceRenderingOff=pair.Value;
            saved.Clear(); Hidden=false;
        }
    }
}
