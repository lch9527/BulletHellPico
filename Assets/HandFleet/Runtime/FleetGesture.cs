using UnityEngine;

namespace HandFleet
{
    public enum FleetMode { Neutral, Attack, Defense, Ultimate }

    // Pure simulation state: no Unity input or wall-clock dependency.
    public sealed class FleetGesture
    {
        public FleetMode Mode { get; private set; }
        FleetMode candidate;
        float dwell;
        public void Reset() { Mode=candidate=FleetMode.Neutral; dwell=0; }
        public void Step(FleetInput input, int dominant, float dt)
        {
            var hand=input.Hand(dominant);
            if(!input.Focused || !hand.Valid) { Reset(); return; }
            var outward=(hand.Palm.position-input.HeadPosition).normalized;
            float threshold=Mathf.Cos(45*Mathf.Deg2Rad), retain=Mathf.Cos(55*Mathf.Deg2Rad);
            float fingers=Vector3.Dot(hand.Forward,outward), palm=Vector3.Dot(hand.Normal,outward);
            var next=FleetMode.Neutral;
            if(input.DesktopMode!=FleetMode.Neutral) next=input.DesktopMode;
            else if(palm>(Mode==FleetMode.Ultimate?retain:threshold)) next=FleetMode.Ultimate;
            else if(-palm>(Mode==FleetMode.Defense?retain:threshold)) next=FleetMode.Defense;
            else if(fingers>(Mode==FleetMode.Attack?retain:threshold)) next=FleetMode.Attack;
            if(next!=candidate) {candidate=next;dwell=0;}
            dwell+=dt;
            if(dwell>=(next==FleetMode.Ultimate?.2f:.12f)) Mode=next;
        }
    }
}
