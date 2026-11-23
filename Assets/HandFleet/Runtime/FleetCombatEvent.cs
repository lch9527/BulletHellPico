using UnityEngine;
namespace HandFleet
{
    // Values index the serialized sound library. Append new cues after the existing entries.
    public enum FleetCue { Start, Charge, EnemyShot, PlayerShot, Hit, EnemyHit, Kill, Pickup, Laser, LaserReady, Fusion, Wave, Victory, Defeat, Resume, Tracking, NearMiss, BossPhase, ShieldBreak, Absorb, WarpArrival, TilePulse, CarrierLaunch }
    public readonly struct FleetCombatEvent
    {
        public readonly FleetCue Kind;
        public readonly Vector3 Position, Direction;
        public readonly int Source;
        public readonly float Intensity;
        public readonly EnemyFamily? Family;
        public FleetCombatEvent(FleetCue kind,Vector3 position,Vector3 direction,int source=0,float intensity=1,EnemyFamily? family=null)
        {Kind=kind;Position=position;Direction=direction;Source=source;Intensity=intensity;Family=family;}
    }
}
