using UnityEngine;
namespace HandFleet
{
    public struct FleetHand
    {
        public bool Valid, Pinching;
        public Pose Palm;
        public Vector3 Forward, Normal;
        public Vector4 Curls;
        public float ThumbCurl;
        public int ConfidentFingers;
        public float Curl(int finger) => (ConfidentFingers & (1<<finger))==0 ? 0 : finger==4?ThumbCurl:Curls[finger];
    }
    public struct FleetInput
    {
        public Vector3 HeadPosition;
        public FleetMode DesktopMode;
        public double Timestamp;
        public FleetHand Left, Right;
        public bool Focused, LaserPressed, ToggleAssist;
        public FleetHand Hand(int index) => index==0?Left:Right;
    }
    public interface IHandFleetInputProvider { FleetInput Read(); }

    public sealed class FleetCalibration
    {
        public bool Ready, FusionReady;
        public int Dominant=1;
        public float Sensitivity=1, Reach=.55f, ContactDistance=.06f;
        public readonly Vector3[] Neutral=new Vector3[2];
        public readonly Quaternion[] AimCorrection={Quaternion.identity,Quaternion.identity};
        public bool Calibrate(FleetInput input,Vector3 forward)
        {
            if(!input.Hand(Dominant).Valid) return false;
            var desired=Quaternion.LookRotation(forward,Vector3.up);
            for(int i=0;i<2;i++) if(input.Hand(i).Valid)
            {
                var h=input.Hand(i); Neutral[i]=h.Palm.position;
                AimCorrection[i]=Quaternion.Inverse(h.Palm.rotation)*desired;
            }
            Ready=true; return true;
        }
        public bool CalibrateContact(FleetInput input)
        {
            if(!input.Left.Valid || !input.Right.Valid || Vector3.Dot(input.Left.Normal,input.Right.Normal)>-.5f || Vector3.Dot(input.Left.Forward,input.Right.Forward)<.7f) return false;
            float distance=Vector3.Distance(input.Left.Palm.position,input.Right.Palm.position);
            if(distance>.16f) return false;
            ContactDistance=distance;
            var desired=Aim(input,Dominant);
            for(int i=0;i<2;i++) AimCorrection[i]=Quaternion.Inverse(input.Hand(i).Palm.rotation)*desired;
            FusionReady=true; return true;
        }
        public Quaternion Aim(FleetInput input,int hand) => input.Hand(hand).Palm.rotation*AimCorrection[hand];
    }

    public sealed class FleetMotionEstimator
    {
        public Vector3 Velocity { get; private set; }
        public Vector3 Acceleration { get; private set; }
        Vector3 previous; double timestamp; bool ready;
        public void Reset() { ready=false; Velocity=Acceleration=Vector3.zero; }
        public void Sample(Vector3 position,double time,float discontinuity)
        {
            float dt=(float)(time-timestamp);
            if(!ready || dt<=0 || dt>.15f || Vector3.Distance(position,previous)>discontinuity)
            { previous=position; timestamp=time; ready=true; Velocity=Acceleration=Vector3.zero; return; }
            float blend=1-Mathf.Exp(-dt*18);
            var velocity=Vector3.Lerp(Velocity,(position-previous)/dt,blend);
            Acceleration=Vector3.Lerp(Acceleration,(velocity-Velocity)/dt,blend);
            Velocity=velocity; previous=position; timestamp=time;
        }
    }
    public static class FleetCollision
    {
        // Transform two moving spheres into a stationary origin and one segment.
        public static bool Sweep(Vector3 a0,Vector3 a1,float ar,Vector3 b0,Vector3 b1,float br)
        {
            Vector3 start=a0-b0, delta=(a1-b1)-start;
            float t=delta.sqrMagnitude>1e-12f?Mathf.Clamp01(-Vector3.Dot(start,delta)/delta.sqrMagnitude):0;
            return (start+delta*t).sqrMagnitude<=(ar+br)*(ar+br);
        }
    }
}
