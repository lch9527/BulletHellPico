using System;
using System.Linq;
using HandFleet.XR;
using UnityEngine;
namespace HandFleet.Integration
{
    // A scene-scoped lease: never modify the shared player prefab or other modes' settings.
    public sealed class FleetVRPresentation : IDisposable
    {
        readonly Camera[] cameras;readonly Color[] colors;readonly CameraClearFlags[] flags;readonly float[] fovs;
        readonly OVRPassthroughLayer[] layers;readonly bool[] enabled;
        readonly OVRManager manager;readonly bool passthrough;bool disposed;
        readonly PicoPassthrough pico;readonly bool picoWanted;
        public FleetVRPresentation(GameObject player)
        {
            cameras=player.GetComponentsInChildren<Camera>(true);colors=cameras.Select(c=>c.backgroundColor).ToArray();flags=cameras.Select(c=>c.clearFlags).ToArray();fovs=cameras.Select(c=>c.fieldOfView).ToArray();
            layers=player.GetComponentsInChildren<OVRPassthroughLayer>(true);enabled=layers.Select(l=>l.enabled).ToArray();
            manager=player.GetComponentInChildren<OVRManager>(true);if(manager){passthrough=manager.isInsightPassthroughEnabled;manager.isInsightPassthroughEnabled=false;}
            foreach(var l in layers)l.enabled=false;
            // PICO's portable player runs see-through behind the opaque chamber; pause it for the lease.
            pico=player.GetComponentInChildren<PicoPassthrough>(true);if(pico){picoWanted=pico.Wanted;pico.Wanted=false;}
            foreach(var c in cameras){c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.012f,.021f,.045f,1);}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            for(int i=0;i<cameras.Length;i++)if(cameras[i]){cameras[i].backgroundColor=colors[i];cameras[i].clearFlags=flags[i];cameras[i].fieldOfView=fovs[i];}
            if(manager)manager.isInsightPassthroughEnabled=passthrough;
            for(int i=0;i<layers.Length;i++)if(layers[i])layers[i].enabled=enabled[i];
            if(pico)pico.Wanted=picoWanted;
        }
    }
}
