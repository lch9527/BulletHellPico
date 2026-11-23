using System.Linq;
using HandFleet;
using Oculus.Interaction.Input;
using HandFleet.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using Hand=Oculus.Interaction.Input.Hand;
using HandFinger=Oculus.Interaction.Input.HandFinger;
using CommonUsages=UnityEngine.XR.CommonUsages;

namespace HandFleet.Integration
{
    public sealed class MetaFleetInput : IHandFleetInputProvider
    {
        readonly MetaPlayerRig player; readonly Transform arena; readonly Hand[] hands; readonly OVRHand[] systemHands;
        public bool Focused=true;
        static readonly HandJointId[] roots={HandJointId.HandIndex1,HandJointId.HandMiddle1,HandJointId.HandRing1,HandJointId.HandPinky1,HandJointId.HandThumb1};
        static readonly HandJointId[] mids={HandJointId.HandIndex2,HandJointId.HandMiddle2,HandJointId.HandRing2,HandJointId.HandPinky2,HandJointId.HandThumb2};
        static readonly HandJointId[] tips={HandJointId.HandIndexTip,HandJointId.HandMiddleTip,HandJointId.HandRingTip,HandJointId.HandPinkyTip,HandJointId.HandThumbTip};
        public MetaFleetInput(MetaPlayerRig rig,Transform frame)
        {
            player=rig; arena=frame; systemHands=rig.XRRig.GetComponentsInChildren<OVRHand>(true);
            // The UnityXR rig (PICO) also carries a static prefab hand (HandSphereMap) that always reads as tracked;
            // taking it as the "first valid hand" would fly the ship from a frozen pose. Same filter as MetaShadowPlayer.Hands.
            hands=rig.XRRig.GetComponentsInChildren<Hand>(true).Where(h=>!h.GetComponent<FromHandPrefabDataSource>()).ToArray();
        }
        public FleetInput Read()
        {
            var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool tracked=head.isValid&&head.TryGetFeatureValue(CommonUsages.isTracked,out bool value)&&value;
            bool present=!head.TryGetFeatureValue(CommonUsages.userPresence,out bool wearing)||wearing;
            return new FleetInput{Timestamp=Time.unscaledTimeAsDouble,HeadPosition=arena.InverseTransformPoint(player.ViewCamera.transform.position),Focused=Focused&&tracked&&present,Left=Sample(0),Right=Sample(1)};
        }
        FleetHand Sample(int index)
        {
            var side=index==0?Handedness.Left:Handedness.Right;
            if(systemHands.Any(h=>h&&h.GetHand()==(index==0?OVRPlugin.Hand.HandLeft:OVRPlugin.Hand.HandRight)&&h.IsSystemGestureInProgress))return default;
            var h=hands.FirstOrDefault(x=>x&&x.isActiveAndEnabled&&x.Handedness==side&&x.IsConnected&&x.IsTrackedDataValid);
            if(!h || !h.GetRootPose(out var wrist) || !h.GetJointPose(HandJointId.HandMiddle1,out var middle) ||
                !h.GetJointPose(HandJointId.HandIndex1,out var knuckle) || !h.GetJointPose(HandJointId.HandPinky1,out var pinky)) return default;
            var forward=(middle.position-wrist.position).normalized;
            var across=(knuckle.position-pinky.position).normalized;
            var normal=Vector3.Cross(forward,across).normalized*(index==0?-1:1);
            if(forward.sqrMagnitude<.5f||normal.sqrMagnitude<.5f)return default;
            var rotation=Quaternion.LookRotation(forward,across);
            var result=new FleetHand{Valid=true,Palm=new Pose(arena.InverseTransformPoint(Vector3.Lerp(wrist.position,middle.position,.6f)),Quaternion.Inverse(arena.rotation)*rotation),Forward=arena.InverseTransformDirection(forward),Normal=arena.InverseTransformDirection(normal)};
            if(h.GetJointPose(HandJointId.HandIndexTip,out var tip)&&h.GetJointPose(HandJointId.HandThumbTip,out var thumb))
                result.Pinching=Vector3.Distance(tip.position,thumb.position)/Mathf.Max(.1f,h.Scale)<.025f;
            for(int f=0;f<5;f++)
            {
                var finger=f==4?HandFinger.Thumb:(HandFinger)(f+1);
                if(!h.GetFingerIsHighConfidence(finger)||!h.GetJointPose(roots[f],out var a)||!h.GetJointPose(mids[f],out var b)||!h.GetJointPose(tips[f],out var c))continue;
                result.ConfidentFingers|=1<<f;
                float curl=Mathf.Clamp01((1-Vector3.Dot((b.position-a.position).normalized,(c.position-b.position).normalized))/.9f);
                if(f==4)result.ThumbCurl=curl; else result.Curls[f]=curl;
            }
            return result;
        }
    }
    public sealed class DesktopFleetInput : IHandFleetInputProvider
    {
        public bool Focused=true, SuppressPointer;
        Vector3 position; float yaw,pitch; FleetMode mode=FleetMode.Attack;
        public FleetInput Read()
        {
            var mouse=Mouse.current; var keys=Keyboard.current;
            if(mouse!=null&&!SuppressPointer)
            {
                if(mouse.rightButton.isPressed) { var delta=mouse.delta.ReadValue(); yaw=Mathf.Clamp(yaw+delta.x*.12f,-55,55); pitch=Mathf.Clamp(pitch-delta.y*.12f,-40,40); }
                else { var p=mouse.position.ReadValue(); position.x=(p.x/Mathf.Max(1,Screen.width)-.5f)*1.1f; position.y=(p.y/Mathf.Max(1,Screen.height)-.5f)*.65f; }
                position.z=Mathf.Clamp(position.z+mouse.scroll.ReadValue().y*.0005f,-.25f,.3f);
            }
            if(keys!=null){if(keys.digit1Key.wasPressedThisFrame)mode=FleetMode.Attack;if(keys.digit2Key.wasPressedThisFrame)mode=FleetMode.Defense;if(keys.digit3Key.wasPressedThisFrame)mode=FleetMode.Ultimate;}
            var rotation=Quaternion.Euler(pitch,yaw,0);
            var left=new FleetHand{Valid=Focused,Palm=new Pose(position-Vector3.right*.03f,rotation),Forward=rotation*Vector3.forward,Normal=rotation*Vector3.right,ConfidentFingers=31};
            var right=left; right.Palm.position=position+Vector3.right*.03f; right.Normal=-left.Normal;
            return new FleetInput{Timestamp=Time.unscaledTimeAsDouble,DesktopMode=mode,HeadPosition=Vector3.back,Focused=Focused,Left=left,Right=right,LaserPressed=keys!=null&&keys.bKey.wasPressedThisFrame,ToggleAssist=keys!=null&&keys.fKey.wasPressedThisFrame};
        }
    }
}
