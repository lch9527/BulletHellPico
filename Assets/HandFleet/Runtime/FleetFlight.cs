using UnityEngine;
namespace HandFleet
{
    public enum FusionState { Solo, Preview, Joining, Fused, Separating }
    public sealed class FleetFusion
    {
        public FusionState State { get; private set; }
        public float Progress { get; private set; }
        public float Blend { get; private set; }
        public bool Assisted { get; private set; }
        public bool NeedsBoth => !Assisted && (State==FusionState.Joining || State==FusionState.Fused || State==FusionState.Separating);
        public bool Heavy => State==FusionState.Fused || State==FusionState.Joining;
        float dwell,exit;
        public void Reset() { State=FusionState.Solo; Progress=Blend=dwell=exit=0; Assisted=false; }
        public void CancelPreview() { if(State==FusionState.Preview) Reset(); }
        public bool Step(FleetInput input,FleetCalibration calibration,HandFleetSettings s,float dt,bool allowed,bool assistAllowed)
        {
            var before=State;
            if(input.ToggleAssist && assistAllowed && allowed)
            {
                if(State==FusionState.Solo || State==FusionState.Preview) { Assisted=true; State=FusionState.Joining; dwell=0; }
                else if(State==FusionState.Fused) { State=FusionState.Separating; dwell=0; }
            }
            bool both=input.Left.Valid&&input.Right.Valid;
            float distance=both?Vector3.Distance(input.Left.Palm.position,input.Right.Palm.position):float.PositiveInfinity;
            bool aligned=both&&Vector3.Dot(input.Left.Normal,input.Right.Normal)<-s.FacingDot&&Vector3.Dot(input.Left.Forward,input.Right.Forward)>s.AlignedDot;
            bool candidate=allowed&&calibration.FusionReady&&aligned&&Mathf.Abs(distance-calibration.ContactDistance)<=s.EntryTolerance;
            switch(State)
            {
                case FusionState.Solo:
                case FusionState.Preview:
                    if(candidate) { State=FusionState.Preview; dwell+=dt; Progress=Mathf.Clamp01(dwell/s.JoinDwell); if(dwell>=s.JoinDwell) { State=FusionState.Joining; dwell=0; } }
                    else { State=FusionState.Solo; dwell=Progress=0; }
                    break;
                case FusionState.Joining:
                    dwell+=dt; Blend=Mathf.Clamp01(dwell/s.JoinDuration);
                    if(dwell>=s.JoinDuration) { State=FusionState.Fused; Blend=1; dwell=0; }
                    break;
                case FusionState.Fused:
                    // Invalid palms are handled by recovery, never interpreted as a split.
                    if(!Assisted && both && distance>calibration.ContactDistance+s.EntryTolerance+s.ExitExtra) exit+=dt; else exit=0;
                    if(exit>=s.ExitDwell) { State=FusionState.Separating; dwell=exit=0; }
                    break;
                case FusionState.Separating:
                    dwell+=dt; Blend=1-Mathf.Clamp01(dwell/s.JoinDuration);
                    if(dwell>=s.JoinDuration) Reset();
                    break;
            }
            return State!=before && State!=FusionState.Preview && !(before==FusionState.Preview&&State==FusionState.Solo);
        }
    }
    public sealed class FleetMotor
    {
        public Vector3 Position, Previous, Velocity, Target;
        public Quaternion Rotation=Quaternion.identity;
        Vector3 offset, positionIntegral, rotationIntegral, angularVelocity, gripVelocity;
        public event System.Action<Vector3,Vector3> NozzleCommand;

        void ClearFollow()
        {
            positionIntegral=rotationIntegral=angularVelocity=gripVelocity=Vector3.zero;
            NozzleCommand?.Invoke(Vector3.zero,Vector3.zero);
        }

        static Vector3 AngleError(Quaternion current,Quaternion aim)
        {
            var q=aim*Quaternion.Inverse(current);
            if(q.w<0) q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
            q.ToAngleAxis(out var degrees,out var axis);
            return degrees<.001f ? Vector3.zero : axis.normalized*degrees;
        }

        void Command(Quaternion aim)
        {
            var local=Quaternion.Inverse(Rotation);
            NozzleCommand?.Invoke(local*(Target-Position),local*AngleError(Rotation,aim));
        }

        static Vector3 PID(Vector3 error,Vector3 velocity,ref Vector3 integral,float response,float integralLimit,float dt)
        {
            float omega=2f/Mathf.Max(.02f,response);
            integral=Vector3.ClampMagnitude(integral+error*dt,integralLimit);
            return omega*omega*(error+.25f*integral)-2f*omega*velocity;
        }

        void AdvancePosition(ref Vector3 speed,float dt)
        {
            var error=Target-Position;
            var step=speed*dt;
            if(Vector3.Dot(error,error-step)<=0)
            {
                Position=Target;
                speed=positionIntegral=Vector3.zero;
            }
            else Position+=step;
        }

        void Turn(Quaternion aim,float response,float maxRate,float dt)
        {
            if(response<=0){Rotation=aim;angularVelocity=rotationIntegral=Vector3.zero;return;}
            var error=AngleError(Rotation,aim);
            angularVelocity=Vector3.ClampMagnitude(angularVelocity+PID(error,angularVelocity,ref rotationIntegral,response,2f,dt)*dt,maxRate);
            float speed=angularVelocity.magnitude;
            if(speed>0) Rotation=Quaternion.AngleAxis(speed*dt,angularVelocity/speed)*Rotation;
        }
        public bool Tethered { get; private set; }
        // Arcade hold: on grab the ship snaps onto the palm and stays exactly there (no hand-to-ship offset); only the
        // ship's orientation relative to the hand is captured, so it keeps pointing the way it faced when grabbed.
        public bool Rigid { get; private set; }
        Vector3 gripPosition; Quaternion gripRotation=Quaternion.identity;
        public void Grip(FleetInput input,int hand)
        {
            var palm=input.Hand(hand).Palm;
            gripPosition=Vector3.zero; gripRotation=Quaternion.Inverse(palm.rotation)*Rotation; Rigid=true; ClearFollow();
            Position=palm.position; Previous=Target=Position; Velocity=Vector3.zero; Tethered=false;
        }
        public void Release() { ClearFollow(); Rigid=false; gripPosition=Vector3.zero; gripRotation=Quaternion.identity; }
        void Held(FleetInput input,int hand)
        { var palm=input.Hand(hand).Palm; Position=palm.position+palm.rotation*gripPosition; Rotation=palm.rotation*gripRotation; Target=Position; }
        void Follow(FleetInput input,int hand,HandFleetSettings s,float dt)
        {
            var palm=input.Hand(hand).Palm;
            Target=palm.position+palm.rotation*gripPosition;
            var aim=palm.rotation*gripRotation;
            Command(aim);
            // Substep the virtual pose for consistent damping at different headset frame rates.
            int steps=Mathf.Max(1,Mathf.CeilToInt(dt/.005f));
            float h=dt/steps;
            for(int n=0;n<steps;n++)
            {
                if(s.HoldPositionLag<=0){Position=Target;gripVelocity=positionIntegral=Vector3.zero;}
                else
                {
                    gripVelocity+=PID(Target-Position,gripVelocity,ref positionIntegral,s.HoldPositionLag,.02f,h)*h;
                    AdvancePosition(ref gripVelocity,h);
                    if(s.HoldMaxLag>0) Position=Target+Vector3.ClampMagnitude(Position-Target,s.HoldMaxLag);
                }
                Turn(aim,s.HoldRotationLag,10000,h);
            }
        }
        public Vector3 RawTarget(FleetInput input,FleetCalibration c,FleetFusion fusion)
        {
            if(fusion.Heavy && !fusion.Assisted) return (input.Left.Palm.position+input.Right.Palm.position)*.5f*c.Sensitivity;
            return input.Hand(c.Dominant).Palm.position*c.Sensitivity;
        }
        public void Rebase(FleetInput input,FleetCalibration c,FleetFusion fusion)
        {
            // A rigid hold snaps back onto the hand without sweeping through the gap, so returning tracking cannot hit bullets on the way.
            ClearFollow();
            if(Rigid) { Held(input,c.Dominant); Previous=Position; Velocity=Vector3.zero; return; }
            offset=Position-RawTarget(input,c,fusion); Previous=Target=Position; Velocity=Vector3.zero;
        }
        public void Step(FleetInput input,FleetCalibration c,FleetFusion fusion,HandFleetSettings s,float dt)
        {
            Previous=Position;
            if(Rigid) { Follow(input,c.Dominant,s,dt); Velocity=dt>0?(Position-Previous)/dt:Vector3.zero; Tethered=false; return; }
            Target=RawTarget(input,c,fusion)+offset;
            if(!s.GestureCampaign)Target=Vector3.ClampMagnitude(Target,c.Reach);
            bool heavy=fusion.Heavy;
            var aim=c.Aim(input,c.Dominant);
            if(heavy&&!fusion.Assisted) aim=Quaternion.Slerp(c.Aim(input,0),c.Aim(input,1),.5f);
            Command(aim);
            float response=heavy?s.FusedResponse:s.SoloResponse;
            int steps=Mathf.Max(1,Mathf.CeilToInt(dt/.005f));
            float h=dt/steps;
            for(int n=0;n<steps;n++)
            {
                var acceleration=Vector3.ClampMagnitude(PID(Target-Position,Velocity,ref positionIntegral,response,.02f,h),
                    heavy?s.FusedAcceleration:s.SoloAcceleration);
                Velocity=Vector3.ClampMagnitude(Velocity+acceleration*h,s.MaxSpeed);
                AdvancePosition(ref Velocity,h);
                Turn(aim,response,heavy?s.FusedTurnRate:s.SoloTurnRate,h);
            }
            float error=heavy?s.FusedError:s.SoloError;
            Tethered=Vector3.Distance(Position,Target)>error;
            if(Tethered) Position=Target+Vector3.ClampMagnitude(Position-Target,error);
        }
    }
}
