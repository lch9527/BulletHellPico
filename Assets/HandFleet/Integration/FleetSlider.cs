using System;
using System.Collections.Generic;
using Oculus.Interaction;
using HandFleet.XR;
using UnityEngine;

namespace HandFleet.Integration
{
    // Pinch on the track and sweep the hand sideways to set a 0..1 value. Each frame the pinching ray is intersected with
    // the slider's own plane, so the drag keeps working after the ray slips off the thin track; releasing the pinch commits.
    public sealed class FleetSlider : MonoBehaviour
    {
        public float Value { get; private set; }
        public bool Dragging=>dragId!=0||mouseDrag;
        public Func<bool> CanStart;
        public Action<float> Changed,Committed;
        public const float Step=.05f;
        Func<IEnumerable<RayInteractor>> rays;
        Transform root,fill,knob; Renderer knobRenderer; Material idle,hover,drag;
        float start,end; FleetControl control;
        int dragId; RayInteractor dragRay; Vector3? dragPoint; bool mouseDrag;

        // The host is a unit cube scaled to the control size; children live in an unscaled root, in metres from its centre.
        public void Build(float trackStart,float trackEnd,Material track,Material filled,Material knobIdle,Material knobHover,Material knobDrag,Func<IEnumerable<RayInteractor>> interactors)
        {
            start=trackStart;end=trackEnd;idle=knobIdle;hover=knobHover;drag=knobDrag;rays=interactors;control=GetComponent<FleetControl>();
            var s=transform.localScale;root=FleetGeometry.Root(transform,"Slider");root.localScale=new Vector3(1/s.x,1/s.y,1/s.z);
            FleetGeometry.Shape(root,"Track",PrimitiveType.Cube,new Vector3((start+end)/2,0,-.010f),new Vector3(end-start,.006f,.003f),track);
            fill=FleetGeometry.Shape(root,"Fill",PrimitiveType.Cube,Vector3.zero,Vector3.one,filled);
            knob=FleetGeometry.Shape(root,"Knob",PrimitiveType.Cube,Vector3.zero,new Vector3(.016f,.04f,.012f),knobIdle);knobRenderer=knob.GetComponent<Renderer>();
            var button=GetComponent<MetaMenuButton>();
            if(button){button.Ray.WhenPointerEventRaised+=OnPointer;button.Poke.WhenPointerEventRaised+=OnPointer;}
            Show();
        }
        public void SetValue(float value){Value=Mathf.Clamp01(Mathf.Round(Mathf.Clamp01(value)/Step)*Step);Show();}
        // Maps a world point on (or near) the slider plane to a value; returns true when the value changed.
        public bool SetFromWorld(Vector3 world)
        {
            float before=Value;SetValue(Mathf.InverseLerp(start,end,root.InverseTransformPoint(world).x));
            if(Mathf.Approximately(before,Value))return false;
            Changed?.Invoke(Value);return true;
        }
        public bool SetFromRay(Ray ray)
        {
            var plane=new Plane(transform.forward,transform.position);
            return plane.Raycast(ray,out float distance)&&SetFromWorld(ray.GetPoint(distance));
        }
        // Desktop preview: press on the slider to grab it, hold to drag, release to commit.
        public void Mouse(Ray ray,bool pressedOnSlider,bool held)
        {
            if(pressedOnSlider&&(CanStart==null||CanStart()))mouseDrag=true;
            if(!mouseDrag)return;
            if(held)SetFromRay(ray);else{mouseDrag=false;Committed?.Invoke(Value);}
        }
        void OnPointer(PointerEvent evt)
        {
            switch(evt.Type)
            {
                case PointerEventType.Select:
                    if(dragId!=0||(CanStart!=null&&!CanStart()))return;
                    dragId=evt.Identifier;dragPoint=evt.Pose.position;dragRay=null;
                    if(rays!=null)foreach(var ray in rays())if(ray&&ray.Identifier==evt.Identifier){dragRay=ray;break;}
                    break;
                case PointerEventType.Move: if(evt.Identifier==dragId)dragPoint=evt.Pose.position; break;
                case PointerEventType.Unselect: case PointerEventType.Cancel: if(evt.Identifier==dragId)EndDrag(); break;
            }
        }
        void EndDrag(){dragId=0;dragRay=null;dragPoint=null;Committed?.Invoke(Value);}
        void Update()
        {
            if(dragId!=0)
            {
                // The ray's own line follows the hand even off the clipped track; a poke (no ray) uses its touch point.
                if(dragRay&&dragRay.isActiveAndEnabled)SetFromRay(dragRay.Ray);
                else if(dragPoint.HasValue)SetFromWorld(dragPoint.Value);
            }
            var material=Dragging?drag:control&&control.Hovered?hover:idle;
            if(knobRenderer.sharedMaterial!=material)knobRenderer.sharedMaterial=material;
        }
        void OnDisable(){if(dragId!=0)EndDrag();mouseDrag=false;}
        void Show()
        {
            if(!root)return;float x=Mathf.Lerp(start,end,Value);
            fill.localPosition=new Vector3((start+x)/2,0,-.012f);fill.localScale=new Vector3(Mathf.Max(.0005f,x-start),.01f,.004f);
            knob.localPosition=new Vector3(x,0,-.014f);
        }
        void OnDestroy()
        {
            var button=GetComponent<MetaMenuButton>();
            if(button&&button.Ray)button.Ray.WhenPointerEventRaised-=OnPointer;
            if(button&&button.Poke)button.Poke.WhenPointerEventRaised-=OnPointer;
        }
    }
}
