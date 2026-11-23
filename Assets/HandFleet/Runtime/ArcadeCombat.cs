using UnityEngine;

namespace HandFleet
{
    // Arcade rules: the ship is held 1:1, fires on its own only while an enemy is locked in its aim cone, dies to one hit on
    // the core and upgrades its guns from pickups. Waves are authored (Settings.ArcadeWaveTable): each is a list of small
    // formations that warp in, after a visible warning, at one point of a comfortable band in front of the player and fly to
    // their slots. A wave caps how many enemies are alive and how many shoot at once; every wave opens with a short break
    // and a WAVE banner. The boss follows the last wave; after it (EndlessAfterBoss) the late waves loop, harder each lap.
    // Everything spawns and moves inside a fixed play window (ViewHorizontalFov x ViewVerticalFov from the launch head pose).
    public sealed partial class HandFleetRun
    {
        public bool ArcadeRules => Mission.Arcade;
        public const int MaxWeaponLevel=3;
        public int WeaponLevel { get; private set; }
        public int Wave { get; private set; }
        public int Kills { get; private set; }
        // Authored waves played before the boss (the first ArcadeWaves rows of the table).
        public int WaveCount => Mathf.Clamp(Settings.ArcadeWaves, 1, Table.Length);
        public bool BossWave => ArcadeRules && Wave==WaveCount+1;
        public bool Endless => ArcadeRules && Wave>WaveCount+1;
        // A formation is announced at WarpPoint and appears once WarpProgress reaches 1.
        public bool WarpPending { get; private set; }
        public Vector3 WarpPoint { get; private set; }
        public float WarpProgress => WarpPending ? Mathf.Clamp01(1-(warpAt-Clock)/Mathf.Max(.01f,Settings.WarpWarning)) : 0;
        // The pause (with its WAVE banner) that opens every wave.
        public bool InWaveBreak => ArcadeRules && State==RunState.Running && Clock<breakUntil;
        ArcadeWave[] Table => Settings.ArcadeWaveTable!=null&&Settings.ArcadeWaveTable.Length>0 ? Settings.ArcadeWaveTable : defaultTable ??= HandFleetSettings.DefaultArcadeWaves();
        static ArcadeWave[] defaultTable;
        int groupCursor; float nextGroupAt,breakUntil,warpAt,reinforceAt; ArcadeGroup pendingGroup; Vector3 lastWarp; uint arcadeSeed;

        void ResetArcade(Vector3 head)
        {
            WeaponLevel=0; Wave=0; Kills=0; groupCursor=0; nextGroupAt=breakUntil=warpAt=reinforceAt=0; WarpPending=false;
            arcadeSeed=0x9E3779B9u; ViewEye=head; lastWarp=head;
        }
        // Apex of the play window in arena space; the window looks along the arena's forward (+z), which faces where the player looked when the arena was placed.
        public Vector3 ViewEye { get; private set; }
        float TanHalf(float fov)=>Mathf.Tan(Mathf.Clamp(fov,1,170)*.5f*Mathf.Deg2Rad);
        // True when p (shrunk by margin metres on every side) lies inside the play window.
        public bool InView(Vector3 p,float margin=0)
        {
            var d=p-ViewEye; if(d.z<Settings.ViewMinDepth) return false;
            return Mathf.Abs(d.x)<=d.z*TanHalf(Settings.ViewHorizontalFov)-margin && Mathf.Abs(d.y)<=d.z*TanHalf(Settings.ViewVerticalFov)-margin;
        }
        // The nearest point of the play window, keeping a body of radius margin fully inside it.
        public Vector3 KeepInView(Vector3 p,float margin)
        {
            var d=p-ViewEye; float z=Mathf.Max(d.z,Settings.ViewMinDepth);
            float x=Mathf.Max(0,z*TanHalf(Settings.ViewHorizontalFov)-margin), y=Mathf.Max(0,z*TanHalf(Settings.ViewVerticalFov)-margin);
            return ViewEye+new Vector3(Mathf.Clamp(d.x,-x,x),Mathf.Clamp(d.y,-y,y),z);
        }
        // Deterministic so a run replays identically from the same inputs.
        float Random01() { arcadeSeed=arcadeSeed*1664525u+1013904223u; return (arcadeSeed>>8)/16777216f; }

        void StepArcade(float dt)
        {
            float interval=Settings.ArcadeFireInterval*(WeaponLevel>=MaxWeaponLevel?.7f:1);
            // No lock, no fire: the guns wait and shoot the moment an enemy enters the aim cone.
            if(Clock>=nextFire && FireArcade()) nextFire=Mathf.Max(nextFire+interval,Clock);
            StepWaves();
            StepEnemies(dt); if(State==RunState.Results) return;
            StepBullets(dt); if(State==RunState.Results) return;
            StepPickups(dt);
        }
        bool FireArcade()
        {
            int count=WeaponLevel switch { 0=>1, 1=>2, 2=>3, _=>5 };
            var forward=Motor.Rotation*Vector3.forward; var up=Motor.Rotation*Vector3.up; var right=Motor.Rotation*Vector3.right;
            var muzzle=Motor.Position+forward*GunMuzzle;
            // Fire only at a locked enemy; the whole gun pattern bends onto it.
            AimAt(Settings.ArenaRadius,out int locked,out var aim);
            if(locked<0 || FreeSlots(false)<count) return false;
            var bend=Quaternion.FromToRotation(forward,aim);forward=aim;up=bend*up;right=bend*right;
            Cue(FleetCue.PlayerShot,Motor.Position);
            for(int i=0;i<count;i++)
            {
                float centred=i-(count-1)*.5f;
                // Twin guns fire parallel; three and five guns fan out.
                float angle=count==2?0:centred*(count==5?7f:9f);
                var lateral=count==2?right*(centred*.03f):Vector3.zero;
                var direction=Quaternion.AngleAxis(angle,up)*forward;
                Emit(false,-1,muzzle+lateral,direction*Settings.BoltSpeed,.008f,Settings.BoltDamage);
            }
            return true;
        }
        // The rules of the wave being played: authored rows, then the boss, then endless laps over the last four rows
        // with one more enemy alive per lap and one more shooter every second lap.
        public ArcadeWave CurrentWave => WaveSpec(Mathf.Max(1,Wave));
        ArcadeWave WaveSpec(int wave)
        {
            var table=Table; int count=WaveCount; var last=table[count-1];
            if(wave<=count) return table[wave-1];
            if(wave==count+1) return new ArcadeWave(7,last.MaxShooters);
            int lap=wave-count-2, loop=Mathf.Min(4,count); var row=table[count-loop+lap%loop];
            return new ArcadeWave(Mathf.Min(row.MaxAlive+1+lap/loop,Enemies.Length-4),Mathf.Min(row.MaxShooters+1+lap/(2*loop),8),row.Groups);
        }
        void StepWaves()
        {
            if(WarpPending)
            {
                if(Clock>=warpAt) { WarpPending=false; SpawnGroup(pendingGroup,WarpPoint); nextGroupAt=Clock+Settings.GroupSpacing; }
                return;
            }
            if(Clock<breakUntil) return;
            if(BossWave) { Reinforce(); return; }
            var spec=CurrentWave; var groups=spec.Groups??System.Array.Empty<ArcadeGroup>();
            if(Wave==0 || (groupCursor>=groups.Length && ActiveEnemies==0)) { StartWave(); return; }
            if(groupCursor>=groups.Length || Clock<nextGroupAt) return;
            var group=groups[groupCursor];
            // A formation waits for room under the wave's alive cap (but never waits on an empty field).
            if(ActiveEnemies>0 && ActiveEnemies+Mathf.Max(1,group.Count)>spec.MaxAlive) return;
            QueueWarp(group); groupCursor++;
        }
        void StartWave()
        {
            Wave++; groupCursor=0; Cue(FleetCue.Wave);
            breakUntil=Clock+(Wave==1?1:Settings.WaveBreak); nextGroupAt=breakUntil;
            if(BossWave) { Spawn(new FleetSpawn{Family=EnemyFamily.Gatekeeper,Position=ViewEye+Vector3.forward*Mathf.Max(Settings.SpawnDepthMax,Settings.ViewMinDepth)}); reinforceAt=breakUntil+6; }
        }
        void Reinforce()
        {
            if(Clock<reinforceAt) return; reinforceAt=Clock+5;
            int followers=0; foreach(var e in Enemies) if(e.Active&&e.Family==EnemyFamily.Follower) followers++;
            if(followers<6) QueueWarp(new ArcadeGroup(EnemyFamily.Follower,3));
        }
        void QueueWarp(ArcadeGroup group)
        {
            pendingGroup=group; WarpPoint=WarpPointInBand(); WarpPending=true; warpAt=Clock+Mathf.Max(0,Settings.WarpWarning);
            Cue(FleetCue.Charge,WarpPoint);
        }
        // A point in the spawn band, preferring (best of three) one far from the previous warp so formations come from varied places.
        Vector3 WarpPointInBand()
        {
            float widen=1+.05f*Mathf.Clamp(Wave-1,0,8); var best=ViewEye+Vector3.forward*Settings.SpawnDepthMax; float bestScore=-1;
            for(int n=0;n<3;n++)
            {
                float yaw=(Random01()*2-1)*Settings.SpawnBandYaw*widen, pitch=Settings.SpawnBandCentrePitch+(Random01()*2-1)*Settings.SpawnBandPitch*widen;
                float depth=Mathf.Max(Settings.ViewMinDepth,Mathf.Lerp(Settings.SpawnDepthMin,Settings.SpawnDepthMax,Random01()));
                var p=KeepInView(ViewEye+Quaternion.Euler(-pitch,yaw,0)*Vector3.forward*depth,.2f);
                float score=(p-lastWarp).sqrMagnitude; if(score>bestScore) { bestScore=score; best=p; }
            }
            lastWarp=best; return best;
        }
        // The formation flies out of the warp point into a shallow, staggered line facing the player; drones bunch up tighter.
        void SpawnGroup(ArcadeGroup group,Vector3 warp)
        {
            if(ActiveEnemies>=Enemies.Length) return;
            Cue(FleetCue.WarpArrival,warp,family:group.Family);
            int count=Mathf.Max(1,group.Count); bool swarm=group.Family==EnemyFamily.Follower;
            var facing=warp-ViewEye; facing.y=0; if(facing.sqrMagnitude<1e-4f) facing=Vector3.forward;
            var right=Vector3.Cross(Vector3.up,facing.normalized);
            float spacing=swarm?.11f:EnemyRadius(new FleetEnemy{Family=group.Family})*2+.12f;
            for(int n=0;n<count&&ActiveEnemies<Enemies.Length;n++)
            {
                float centred=n-(count-1)*.5f;
                var slot=warp+right*(centred*spacing)+Vector3.up*(count>1?((n%2)*2-1)*spacing*.2f:0);
                var spawn=new FleetSpawn{Family=group.Family,Position=slot,EntryOffset=warp-slot};
                if(!swarm)
                {
                    var axis=new Vector3(Random01()*2-1,(Random01()*2-1)*.5f,Random01()*2-1);
                    spawn.PathAxis=axis.sqrMagnitude>.01f?axis:Vector3.right; spawn.PathAmplitude=.06f+Random01()*.08f; spawn.PathSpeed=.6f+Random01()*.6f;
                }
                Spawn(spawn);
            }
        }
        // Arcade enemies take turns: at most the wave's MaxShooters may be telegraphing or have fired within ShooterWindow.
        bool ShooterSlotFree(int id)
        {
            int busy=0;
            foreach(var e in Enemies)
                if(e.Active&&e.Id!=id&&e.Family!=EnemyFamily.Follower&&(e.State==EnemyState.Telegraph||e.State==EnemyState.Attacking||(e.FiredAt>0&&Clock-e.FiredAt<Settings.ShooterWindow))) busy++;
            return busy<Mathf.Max(1,CurrentWave.MaxShooters);
        }
        void ArcadeKill(FleetEnemy e)
        {
            Kills++;
            if(e.Family==EnemyFamily.Gatekeeper)
            {
                Won=true; Cue(FleetCue.Victory);
                if(Settings.EndlessAfterBoss) { StartWave(); return; }
                State=RunState.Results; ClearCombat(); return;
            }
            bool big=e.Family==EnemyFamily.Frigate||e.Family==EnemyFamily.Carrier||e.Family==EnemyFamily.Battleship;
            if(big || Kills%Mathf.Max(1,Settings.UpgradeKills)==0)
                for(int i=0;i<Pickups.Length;i++) if(!Pickups[i].Active) { Pickups[i]=new FleetPickup{Active=true,Position=e.Position,Life=10}; break; }
        }
        public const float GunMuzzle=.1f;
        // Where the aim first meets an enemy, measured from the muzzle; max when it hits nothing. target is the enemy index or -1.
        public float AimRange(float max,out int target)=>AimAt(max,out target,out _);
        // Aim cone: an enemy counts as aimed at when any part of its body lies within AimConeAngle of the line of fire.
        // The nearest such enemy wins; direction points from the muzzle at its centre (the ship's forward when nothing is locked).
        public float AimAt(float max,out int target,out Vector3 direction)
        {
            target=-1; var forward=Motor.Rotation*Vector3.forward; var muzzle=Motor.Position+forward*GunMuzzle; float best=max; direction=forward;
            float cone=Mathf.Max(0,Settings.AimConeAngle);
            for(int i=0;i<Enemies.Length;i++)
            {
                var e=Enemies[i]; if(!e.Active) continue;
                var offset=e.Position-muzzle; float distance=offset.magnitude; if(distance<1e-4f||Vector3.Dot(offset,forward)<=0) continue;
                float radius=EnemyRadius(e);
                float body=distance>radius?Mathf.Asin(radius/distance)*Mathf.Rad2Deg:90;
                if(Vector3.Angle(forward,offset)>cone+body) continue;
                float hit=Mathf.Max(0,distance-radius);
                if(hit<best) { best=hit; target=i; direction=offset/distance; }
            }
            return best;
        }
        void UpgradeGuns() { if(WeaponLevel<MaxWeaponLevel) WeaponLevel++; else Score+=500; }
    }
}
