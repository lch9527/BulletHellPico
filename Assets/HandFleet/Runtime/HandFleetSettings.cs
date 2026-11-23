using System;
using UnityEngine;

namespace HandFleet
{
    // One formation: Count enemies of one family that warp in together at a single point.
    [Serializable] public struct ArcadeGroup { public EnemyFamily Family; [Min(1)] public int Count; public ArcadeGroup(EnemyFamily family,int count){Family=family;Count=count;} }
    // One authored arcade wave: its formations in order, how many enemies may be alive at once, and how many may be shooting at once.
    [Serializable] public struct ArcadeWave
    {
        public ArcadeGroup[] Groups; [Min(1)] public int MaxAlive; [Min(1)] public int MaxShooters;
        public ArcadeWave(int maxAlive,int maxShooters,params ArcadeGroup[] groups){Groups=groups;MaxAlive=maxAlive;MaxShooters=maxShooters;}
    }
    [CreateAssetMenu(menuName="HandFleet/Ship settings")]
    public sealed class HandFleetSettings : ScriptableObject
    {
        public bool GestureCampaign;
        [Header("Gesture combat")]
        public float AcquireAngle=8, RetainAngle=12, TargetRange=6;
        public float EnergyPerKill=10, EnergyPerAbsorb=1, AbsorbPerSecond=10;
        public float UltimateThreshold=80, UltimateDrain=25, UltimateBossDps=160;
        public float ShieldDuration=3, ShieldCooling=2, ShieldLockout=4, ShieldRadius=.12f;
        public float SpawnSpacing=.6f, NextGroupFraction=.25f;
        [Header("Core and follower (metres / seconds)")]
        public float CoreRadius=.02f, SoloResponse=.06f, FusedResponse=.18f;
        public float SoloError=.06f, FusedError=.12f, SoloAcceleration=35, FusedAcceleration=8, MaxSpeed=4;
        public float SoloTurnRate=900, FusedTurnRate=220, DiscontinuityDistance=.35f;
        [Header("Fusion")]
        public float JoinDwell=.45f, JoinDuration=.4f, EntryTolerance=.025f, ExitExtra=.06f, ExitDwell=.2f;
        public float FacingDot=.70f, AlignedDot=.80f, FusedDamage=1.5f;
        [Header("Recovery")]
        public float PromptDelay=.2f, StableTime=.5f, Countdown=3, Invulnerability=1;
        [Header("Combat")]
        public float FireInterval=.14f, BoltSpeed=3.5f, BoltDamage=1;
        public int BulletCapacity=256, EnemyCapacity=6, PickupCapacity=12, MaxAttackers=2;
        [Header("Classic laser and arena")]
        public float LaserDuration=2, LaserDpsMultiplier=8, LaserRadius=.035f, LaserRange=8, ArenaRadius=6;
        public int LaserKills=10;
        public float EnemyBeamDuration=.8f, EnemyBeamRadius=.012f, BossHealth=600;
        [Header("Arcade")]
        public int ArcadeWaves=8, UpgradeKills=12;
        public float ArcadeFireInterval=.1f, ResumeGrace=.75f;
        [Tooltip("Half-angle (degrees) of the aim cone: an enemy whose body is inside it is locked, boxed, and the guns fire and bend onto it. Nothing locked = no fire.")]
        public float AimConeAngle=3;
        [Header("Play window (arcade)")]
        [Tooltip("Full horizontal and vertical angles (degrees) of the window in front of the player, fixed at launch from the head pose. Enemies spawn and move only inside it.")]
        public float ViewHorizontalFov=70, ViewVerticalFov=66;
        [Tooltip("Spawn distance range from the head (metres), and the closest any enemy may come along the view direction.")]
        public float SpawnDepthMin=2f, SpawnDepthMax=2.6f, ViewMinDepth=.7f;
        [Header("Arcade waves")]
        [Tooltip("Authored waves, played in order (the first ArcadeWaves of them), then the boss. Each formation warps in at one point once the wave's MaxAlive leaves room for it.")]
        public ArcadeWave[] ArcadeWaveTable=DefaultArcadeWaves();
        [Tooltip("Half-angles (degrees) of the band in front of the player where formations warp in, and the band's centre pitch (negative = below eye level). The band widens 5% per wave.")]
        public float SpawnBandYaw=25, SpawnBandPitch=15, SpawnBandCentrePitch=-5;
        [Tooltip("Seconds of warp flash before a formation appears; minimum seconds between formations; pause (with the WAVE banner) before each wave.")]
        public float WarpWarning=.6f, GroupSpacing=1.2f, WaveBreak=2.5f;
        [Tooltip("Seconds an enemy holds fire after settling into its slot. An enemy counts as shooting while it telegraphs and for ShooterWindow seconds after it fires.")]
        public float ArrivalGrace=1f, ShooterWindow=1f;
        [Tooltip("After the boss, keep going: the last four waves loop with rising MaxAlive and MaxShooters until the ship is lost.")]
        public bool EndlessAfterBoss=true;
        public static ArcadeWave[] DefaultArcadeWaves()
        {
            ArcadeGroup G(EnemyFamily f,int n)=>new ArcadeGroup(f,n);
            const EnemyFamily S=EnemyFamily.Sentry,D=EnemyFamily.Follower,N=EnemyFamily.Sniper,R=EnemyFamily.Grenadier,F=EnemyFamily.Frigate,B=EnemyFamily.Battleship,C=EnemyFamily.Carrier;
            return new[]
            {
                new ArcadeWave(3,2,G(S,3)),
                new ArcadeWave(4,2,G(S,3),G(EnemyFamily.Tumbler,2)),
                new ArcadeWave(6,2,G(S,3),G(D,3),G(EnemyFamily.Skipper,2)),
                new ArcadeWave(6,3,G(S,3),G(N,2),G(D,3),G(EnemyFamily.Needle,2)),
                new ArcadeWave(7,3,G(R,2),G(S,3),G(D,3),G(R,2),G(EnemyFamily.Shutter,2)),
                new ArcadeWave(7,4,G(F,2),G(S,3),G(D,4),G(EnemyFamily.Spindle,2),G(EnemyFamily.Comet,2)),
                new ArcadeWave(8,4,G(B,1),G(S,3),G(C,1),G(D,4),G(EnemyFamily.Lancer,2)),
                new ArcadeWave(8,5,G(F,2),G(N,2),G(B,1),G(D,4),G(C,1),G(R,2),G(EnemyFamily.Bell,3)),
            };
        }
        [Header("Suicide drones (followers)")]
        [Tooltip("Cruise speed (m/s), acceleration (m/s^2) and turn rate (deg/s). The limited turn rate means a late sidestep makes them overshoot.")]
        public float FollowerSpeed=1.3f, FollowerAcceleration=2.5f, FollowerTurnRate=140;
        [Tooltip("Ship speed (m/s) that counts as a maneuver; while the ship moves this fast, drones lose lock and coast straight for FollowerLockLoss seconds.")]
        public float ManeuverSpeed=1f, FollowerLockLoss=.7f;
        [Tooltip("A drone has missed once the ship is this many degrees or more off its heading (it would have to turn back). It never turns around: it self-destructs at once.")]
        public float FollowerMissAngle=90;
        [Tooltip("Seconds for the held ship to close ~63% of the gap to the hand. 0 = rigid.")]
        public float HoldPositionLag=0, HoldRotationLag=0;
        [Tooltip("The smoothed ship never trails the hand by more than this (metres). 0 = no limit.")]
        public float HoldMaxLag=0;
        [Header("Cosmetics")]
        public float MaxBank=15, MaxPitch=8, FinOpening=12;
    }
}
