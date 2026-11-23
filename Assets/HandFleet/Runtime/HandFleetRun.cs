using System;
using UnityEngine;

namespace HandFleet
{
    public enum RunState { Calibration, Running, Paused, TrackingHold, Recovering, Results }
    public enum EnemyState { Arriving, Telegraph, Attacking, Recovering }
    public struct FleetBullet { public bool Active, Hostile, Shard, NearMissed; public FleetShot Style; public float Age; public int Owner; public Vector3 Position,Previous,Velocity; public float Radius,Damage,Life; }
    public struct FleetEnemy { public bool Active; public int ParentId,Id,Shots,Phase,Group; public EnemyFamily Family; public EnemyState State; public Vector3 Position,Previous,Home,Aim,Entry,PathAxis,Velocity; public float Health,MaxHealth,Timer,Age,PathAmplitude,PathSpeed,FiredAt,RollDegrees; public bool Beam; }
    public struct FleetPickup { public bool Active; public Vector3 Position; public float Life,Energy; }

    // All combat changes happen here, on one explicit clock. No coroutines or projectile physics.
    public sealed partial class HandFleetRun
    {
        public readonly HandFleetSettings Settings;
        public readonly HandFleetMission Mission;
        public readonly FleetCalibration Calibration=new();
        public readonly FleetFusion Fusion=new();
        public readonly FleetMotor Motor=new();
        public readonly FleetMotionEstimator Motion=new();
        public readonly FleetBullet[] Bullets;
        public readonly FleetEnemy[] Enemies;
        public readonly FleetPickup[] Pickups;
        public RunState State { get; private set; }=RunState.Calibration;
        public int Shields { get; private set; }=3;
        public int Score { get; private set; }
        public int Hits { get; private set; }
        public int Multiplier { get; private set; }=1;
        public int LaserCharge { get; private set; }
        public bool LaserReady=>LaserCharge>=Settings.LaserKills && LaserRemaining<=0;
        public float LaserRemaining { get; private set; }
        public int EncounterCursor { get; private set; }
        public int DeferredPatterns { get; private set; }
        public bool SpecialAllowed=>Fusion.State==FusionState.Solo || Fusion.State==FusionState.Fused;
        public bool Assisted { get; private set; }
        public bool AssistEnabled;
        public bool Won { get; private set; }
        public bool RecoveryChoiceRequired { get; private set; }
        public float Clock { get; private set; }
        public float StageTime { get; private set; }
        public float RecoveryTime { get; private set; }
        public float HoldTime { get; private set; }
        public float InvulnerableUntil { get; private set; }
        public int StageIndex { get; private set; }
        public FleetStage Stage=>Mission.Stages[Mathf.Clamp(StageIndex,0,Mission.Stages.Length-1)];
        public event Action<FleetCombatEvent> Feedback;
        void Cue(FleetCue cue,Vector3? position=null,int source=0,float intensity=1,EnemyFamily? family=null)
        { Feedback?.Invoke(new FleetCombatEvent(cue,position??Motor.Position,Motor.Rotation*Vector3.forward,source,intensity,family)); }
        public int ActiveBullets { get { int n=0; foreach(var b in Bullets) if(b.Active)n++; return n; } }
        public int ActiveEnemies { get { int n=0; foreach(var e in Enemies) if(e.Active)n++; return n; } }
        float nextFire,nextSpawn,stable; int sequence,enemyId; bool laserArmed,haveInput;
        FleetInput previousInput;

        public HandFleetRun(HandFleetSettings settings,HandFleetMission mission)
        {
            Settings=settings; Mission=mission;
            Bullets=new FleetBullet[Mathf.Max(32,settings.BulletCapacity)];
            Enemies=new FleetEnemy[Mathf.Clamp(settings.EnemyCapacity,1,32)]; Pickups=new FleetPickup[Mathf.Max(1,settings.PickupCapacity)];
            LaserCharge=settings.LaserKills;
        }
        public bool Start(FleetInput input)
        {
            if(!Calibration.Calibrate(input,Vector3.forward)) return false;
            ClearCombat(); Fusion.Reset(); Motion.Reset(); Motor.Release();
            // Arcade launches from wherever the parked drone was grabbed and stays locked to that grip.
            if(!ArcadeRules) Motor.Position=GestureRules?Vector3.zero:Vector3.ClampMagnitude(input.Hand(Calibration.Dominant).Palm.position,Calibration.Reach);
            Motor.Previous=Motor.Position; Motor.Velocity=Vector3.zero; Motor.Rotation=Quaternion.identity;
            if(ArcadeRules) Motor.Grip(input,Calibration.Dominant); else Motor.Rebase(input,Calibration,Fusion);
            State=RunState.Running; Shields=ArcadeRules?1:3; Score=Hits=0; Multiplier=1; Clock=StageTime=0; if(ArcadeRules)ResetArcade(input.HeadPosition);
            StageIndex=sequence=enemyId=0; nextFire=nextSpawn=0; LaserCharge=Settings.LaserKills; EncounterCursor=DeferredPatterns=0; LaserRemaining=0; Assisted=AssistEnabled; Won=false;
            if(GestureRules)ResetGestures(); InvulnerableUntil=0; RecoveryChoiceRequired=false; laserArmed=false; haveInput=false; Cue(FleetCue.Start); return true;
        }
        // Returns the drone to its launch cradle at the arena origin so it can be grabbed again.
        public void ParkDrone()
        { Motor.Release(); Motor.Position=Motor.Previous=Motor.Target=Motor.Velocity=Vector3.zero; Motor.Rotation=Quaternion.identity; Motion.Reset(); Fusion.Reset(); }
        // Ends the current flight without a result and waits for the next grab.
        public void Abort() { if(GestureRules)SuspendGestures(); ClearCombat(); ParkDrone(); State=RunState.Calibration; }
        public void Pause() { if(State==RunState.Running || State==RunState.Recovering || State==RunState.TrackingHold) { State=RunState.Paused; ResetRecovery(); } }
        public void Resume() { if(State==RunState.Paused) { State=RunState.TrackingHold; HoldTime=0; } }
        void ResetRecovery() { if(GestureRules)SuspendGestures(); stable=RecoveryTime=0; laserArmed=false; Motion.Reset(); Fusion.CancelPreview(); haveInput=false; }
        public void HoldTracking()
        {
            if(State==RunState.Results || State==RunState.Calibration) return;
            if(State!=RunState.TrackingHold) { RecoveryChoiceRequired=Fusion.NeedsBoth; HoldTime=0; Cue(FleetCue.Tracking); }
            State=RunState.TrackingHold; ResetRecovery();
        }
        public bool RecoverSolo(FleetInput input,int hand)
        {
            if(State!=RunState.TrackingHold && State!=RunState.Paused) return false;
            if(!input.Hand(hand).Valid) return false;
            Calibration.Dominant=hand; Calibration.Calibrate(input,Vector3.forward); Fusion.Reset(); RecoveryChoiceRequired=false;
            State=RunState.TrackingHold; ResetRecovery(); Motor.Rebase(input,Calibration,Fusion); return true;
        }
        public bool RestoreFusion(FleetInput input)
        {
            if(State!=RunState.TrackingHold || !input.Left.Valid || !input.Right.Valid) return false;
            if(Vector3.Distance(input.Left.Palm.position,input.Right.Palm.position)>Calibration.ContactDistance+Settings.EntryTolerance+Settings.ExitExtra) return false;
            RecoveryChoiceRequired=false; ResetRecovery(); return true;
        }
        public void Recalibrate(FleetInput input)
        {
            if(!Calibration.Calibrate(input,Vector3.forward)) return;
            if(State!=RunState.Calibration && State!=RunState.Results) { Pause(); Motor.Rebase(input,Calibration,Fusion); }
            Motion.Reset();
        }
        public void Tick(FleetInput input,float dt)
        {
            if(dt>1 || dt<0 || float.IsNaN(dt)) { HoldTracking(); return; }
            if(dt>.02f) { int steps=Mathf.CeilToInt(dt/.02f); for(int n=0;n<steps;n++) Tick(input,dt/steps); return; }
            if(State==RunState.Calibration || State==RunState.Results || State==RunState.Paused) return;
            bool valid=input.Focused&&input.Hand(Calibration.Dominant).Valid&&(!Fusion.NeedsBoth||(input.Left.Valid&&input.Right.Valid));
            bool discontinuity=haveInput && (input.Timestamp-previousInput.Timestamp>.15 || input.Timestamp<previousInput.Timestamp ||
                Vector3.Distance(input.Hand(Calibration.Dominant).Palm.position,previousInput.Hand(Calibration.Dominant).Palm.position)>Settings.DiscontinuityDistance ||
                (Fusion.NeedsBoth&&Vector3.Distance(input.Hand(1-Calibration.Dominant).Palm.position,previousInput.Hand(1-Calibration.Dominant).Palm.position)>Settings.DiscontinuityDistance));
            previousInput=input; haveInput=true;
            if(!valid || (discontinuity&&State==RunState.Running))
            {
                if(State!=RunState.TrackingHold) HoldTracking();
                HoldTime+=dt; stable=RecoveryTime=0; return;
            }
            if(State==RunState.TrackingHold)
            {
                HoldTime+=dt;
                if(RecoveryChoiceRequired) return;
                stable+=dt;
                if(stable>=Settings.StableTime) { State=RunState.Recovering; RecoveryTime=Settings.Countdown; Motor.Rebase(input,Calibration,Fusion); }
                return;
            }
            if(State==RunState.Recovering)
            {
                Motor.Rebase(input,Calibration,Fusion); RecoveryTime-=dt;
                if(RecoveryTime<=0) { State=RunState.Running; Motion.Reset(); haveInput=false; laserArmed=false; if(ArcadeRules)InvulnerableUntil=Clock+Settings.ResumeGrace; Cue(FleetCue.Resume); }
                return;
            }
            bool allowFusion=!GestureRules&&!ArcadeRules&&(Stage.AllowFusion || Stage.Kind==StageKind.FusionPractice);
            bool changed=Fusion.Step(input,Calibration,Settings,dt,allowFusion,AssistEnabled);
            if(changed) { Motor.Rebase(input,Calibration,Fusion); Motion.Reset(); Cue(FleetCue.Fusion); }
            if(Fusion.Assisted) Assisted=true;
            Clock+=dt; StageTime+=dt;
            Motor.Step(input,Calibration,Fusion,Settings,dt);
            Motion.Sample(Motor.Position,input.Timestamp,Settings.DiscontinuityDistance);
            var primary=input.Hand(Calibration.Dominant);
            if(!SpecialAllowed || !primary.Valid) laserArmed=false;
            else if(!primary.Pinching) laserArmed=true;
            if(ArcadeRules) { StepArcade(dt); return; }
            if(!GestureRules && SpecialAllowed && ((primary.Pinching&&laserArmed)||input.LaserPressed)) { TryLaser(); laserArmed=false; }
            if(GestureRules)StepGestureCombat(input,dt);
            if(State==RunState.Results)return;
            if(Stage.Kind!=StageKind.Rest)
            {
                if(Clock>=nextFire) { if(GestureRules)FireAcquiredTarget();else FirePlayer(); nextFire+=Settings.FireInterval; }
                if(Stage.Kind==StageKind.Combat || Stage.Kind==StageKind.Practice || Stage.Kind==StageKind.FusionPractice)
                {
                    if(GestureRules)StepBudget();
                    else if(Stage.Spawns!=null&&Stage.Spawns.Length>0) StepEncounter();
                    else if(Stage.Completion==EncounterEnd.Timed&&Clock>=nextSpawn && ActiveEnemies<Mathf.Min(Enemies.Length,Stage.MaxEnemies)) { SpawnEnemy(); nextSpawn=Clock+Stage.SpawnInterval; }
                    StepEnemies(dt);
                }
                if(!GestureRules)StepLaser(dt);
                StepBullets(dt);
                if(State==RunState.Results) return;
                StepPickups(dt);
            }
            if(State==RunState.Running && StageTime>=Stage.Duration &&
                (Stage.Completion==EncounterEnd.Timed || (EncounterCursor>=Stage.Spawns.Length&&ActiveEnemies==0))) NextStage();
        }
        public void SkipFusionPractice() { if(State==RunState.Running && Stage.Kind==StageKind.FusionPractice) NextStage(); }
        void NextStage()
        {
            if(Stage.Kind==StageKind.Practice){LaserCharge=Settings.LaserKills;if(GestureRules)Energy=0;}
            ClearCombat(); currentGroup=0;groupSpawnAt=Clock;SuspendGestures(); StageTime=0; EncounterCursor=0; laserArmed=false; StageIndex++; nextSpawn=Clock+1; nextFire=Clock;
            if(StageIndex>=Mission.Stages.Length) { Won=true; State=RunState.Results; Cue(FleetCue.Victory); }
            else Cue(FleetCue.Wave);
        }
        public void ClearCombat() { LaserRemaining=0; Array.Clear(Bullets,0,Bullets.Length); Array.Clear(Enemies,0,Enemies.Length); Array.Clear(Pickups,0,Pickups.Length); }
        HandFleetPattern Pattern(EnemyFamily f) { foreach(var p in Mission.Patterns) if(p&&p.Family==f) return p; return Mission.Patterns[0]; }
        public bool Emit(bool hostile,int owner,Vector3 position,Vector3 velocity,float radius,float damage,bool shard=false,FleetShot style=FleetShot.Pulse)
        {
            // Reserve a quarter of the pool for player fire; patterns remain bounded.
            int begin=hostile?0:Bullets.Length*3/4, end=hostile?Bullets.Length*3/4:Bullets.Length;
            for(int i=begin;i<end;i++) if(!Bullets[i].Active)
            { Bullets[i]=new FleetBullet{Active=true,Hostile=hostile,Style=hostile?(shard?FleetShot.Sliver:style):FleetShot.Bolt,Shard=shard,Owner=owner,Position=position,Previous=position,Velocity=velocity,Radius=radius,Damage=damage,Life=style==FleetShot.Tile?FleetOrdnance.TileDelay+FleetOrdnance.TileDuration:8}; return true; }
            return false;
        }
        void FirePlayer()
        {
            var forward=Motor.Rotation*Vector3.forward; bool heavy=Fusion.Heavy;
            if(FreeSlots(false)<(heavy?2:1)) return;
            Cue(FleetCue.PlayerShot,Motor.Position);
            for(int i=0;i<(heavy?2:1);i++)
            {
                var offset=Motor.Rotation*new Vector3(heavy?(i==0?-.025f:.025f):0,0,.14f);
                Emit(false,-1,Motor.Position+offset,forward*Settings.BoltSpeed,.008f,Settings.BoltDamage*(heavy?Settings.FusedDamage*.5f:1));
            }
        }
        int FreeSlots(bool hostile)
        { int n=0; for(int i=hostile?0:Bullets.Length*3/4;i<(hostile?Bullets.Length*3/4:Bullets.Length);i++)if(!Bullets[i].Active)n++;return n; }
        void StepEncounter()
        {
            while(EncounterCursor<Stage.Spawns.Length && Stage.Spawns[EncounterCursor].At<=StageTime)
            {
                if(ActiveEnemies>=Mathf.Min(Enemies.Length,Stage.MaxEnemies))return;
                Spawn(Stage.Spawns[EncounterCursor++]);
            }
        }
        void SpawnEnemy()
        {
            var family=Stage.Families[sequence%Stage.Families.Length];
            var home=new Vector3(Mathf.Sin(sequence*2.4f)*.55f,.15f+Mathf.Sin(sequence*1.7f)*.28f,1.4f+(sequence%2)*.18f);sequence++;
            Spawn(new FleetSpawn{Family=family,Position=home,PathAmplitude=family==EnemyFamily.Tumbler?.12f:0});
        }
        void Spawn(FleetSpawn spawn)
        {
            for(int i=0;i<Enemies.Length;i++)if(!Enemies[i].Active)
            {
                float health=spawn.Family==EnemyFamily.Gatekeeper?Settings.BossHealth:Pattern(spawn.Family).Health;
                var home=spawn.Position;var entry=spawn.Position+spawn.EntryOffset;
                // Arcade: the entry swoop starts and ends inside the play window, so (the window being convex) it never leaves it.
                if(ArcadeRules){float r=EnemyRadius(new FleetEnemy{Family=spawn.Family});home=KeepInView(home,r);entry=KeepInView(entry,r);}
                Enemies[i]=new FleetEnemy{Active=true,Id=++enemyId,Family=spawn.Family,State=EnemyState.Arriving,
                    Home=home,Entry=entry,Position=entry,Previous=entry,
                    Health=health,MaxHealth=health,Timer=1,Group=spawn.Group,PathAxis=spawn.PathAxis.normalized,PathAmplitude=spawn.PathAmplitude,PathSpeed=spawn.PathSpeed};return;
            }
        }
        void StepEnemies(float dt)
        {
            int attacking=0;
            foreach(var e in Enemies)if(e.Active&&(e.State==EnemyState.Telegraph||e.State==EnemyState.Attacking))attacking++;
            for(int i=0;i<Enemies.Length;i++)
            {
                if(!Enemies[i].Active)continue;
                if((GestureRules||ArcadeRules) && Enemies[i].Family>=EnemyFamily.Sentry){StepNewEnemy(i,dt);if(State==RunState.Results)return;continue;}
                var e=Enemies[i];var p=Pattern(e.Family);e.Previous=e.Position;e.Age+=dt;e.Timer-=dt;
                if(e.State==EnemyState.Arriving)
                {
                    e.Position=Vector3.Lerp(e.Entry,e.Home,Mathf.SmoothStep(0,1,1-e.Timer));
                    if(e.Timer<=0){e.Position=e.Home;e.State=EnemyState.Recovering;e.Timer=.5f+e.Group*.15f;}
                }
                else
                {
                    if(!(e.Family==EnemyFamily.Comet&&e.State==EnemyState.Attacking))e.Position=e.Home+e.PathAxis*Mathf.Sin(e.Age*e.PathSpeed)*e.PathAmplitude;
                    if(e.State==EnemyState.Recovering&&e.Timer<=0&&attacking<Settings.MaxAttackers&&(!ArcadeRules||ShooterSlotFree(e.Id))&&Stage.Kind!=StageKind.FusionPractice)
                    {e.State=EnemyState.Telegraph;e.Timer=p.Telegraph;e.Aim=Motor.Position;e.Shots=0;e.Beam=false;attacking++;Cue(FleetCue.Charge,e.Position,e.Id,family:e.Family);}
                    else if(e.State==EnemyState.Telegraph&&e.Timer<=0){e.State=EnemyState.Attacking;e.Timer=0;}
                    if(e.State==EnemyState.Attacking)
                    {
                        if(e.Family==EnemyFamily.Lancer)
                        {
                            if(!e.Beam){e.FiredAt=Clock;e.Beam=true;e.Timer=Settings.EnemyBeamDuration;Cue(FleetCue.EnemyShot,e.Position,e.Id,family:e.Family);}
                            var end=e.Position+(e.Aim-e.Position).normalized*Settings.ArenaRadius*2;
                            if(FleetSegments.DistanceSquared(e.Position,end,Motor.Previous,Motor.Position)<=Mathf.Pow(Settings.EnemyBeamRadius+Settings.CoreRadius,2))Hit();
                            if(e.Timer<=0){e.Beam=false;e.State=EnemyState.Recovering;e.Timer=p.Recovery;attacking--;}
                        }
                        else if(e.Family==EnemyFamily.Comet)
                        {
                            if(e.Shots==0){e.FiredAt=Clock;e.Shots=1;e.Timer=1.6f;Cue(FleetCue.EnemyShot,e.Position,e.Id,family:e.Family);}
                            e.Position+= (e.Aim-e.Home).normalized*2*dt;
                            if(FleetCollision.Sweep(e.Previous,e.Position,.055f,Motor.Previous,Motor.Position,Settings.CoreRadius))Hit();
                            if(e.Timer<=0){e.State=EnemyState.Arriving;e.Entry=e.Position;e.Timer=1;attacking--;}
                        }
                        else if(e.Timer<=0)
                        {
                            if(FireEnemy(e,p))
                            {
                                e.Shots++;e.FiredAt=Clock;
                                if(e.Family==EnemyFamily.Tumbler)e.RollDegrees+=90;
                                int burst=e.Family==EnemyFamily.Tumbler || e.Family==EnemyFamily.Needle || (e.Family==EnemyFamily.Gatekeeper&&e.Phase==0)?3:1;
                                if(e.Shots>=burst){e.State=EnemyState.Recovering;e.Timer=p.Recovery*(e.Family==EnemyFamily.Gatekeeper?1-e.Phase*.2f:1);attacking--;}
                                else e.Timer=e.Family==EnemyFamily.Tumbler?.32f:.22f;
                            }
                            else {e.Timer=.05f;DeferredPatterns++;}
                        }
                    }
                }
                if(State==RunState.Results)return;
                if(ArcadeRules)
                {
                    var limit=KeepInView(e.Position,EnemyRadius(e));
                    if(e.Family==EnemyFamily.Comet&&e.State==EnemyState.Attacking&&(limit-e.Position).sqrMagnitude>.000001f)
                    { e.State=EnemyState.Arriving;e.Entry=limit;e.Timer=1;attacking--; }
                    e.Position=limit;
                }
                Enemies[i]=e;
            }
        }
        bool FireEnemy(FleetEnemy e,HandFleetPattern p)
        {
            if(e.Family==EnemyFamily.Tumbler)
            {
                var across=FleetOrdnance.Facing(e.Aim-e.Position)*Vector3.right;
                var marked=e.Aim+across*((e.Shots-1)*.16f);
                if(!Emit(true,e.Id,marked,Vector3.zero,FleetOrdnance.TileRadius,1,false,FleetShot.Tile))return false;
                Cue(FleetCue.EnemyShot,e.Position,e.Id,family:e.Family);return true;
            }
            bool ring=e.Family==EnemyFamily.Spindle||(e.Family==EnemyFamily.Gatekeeper&&e.Phase==1);
            bool fan=e.Family==EnemyFamily.Gatekeeper&&e.Phase==2;
            int count=ring?7:fan?5:1;
            if(FreeSlots(true)<count)return false;
            float speed=Stage.Kind==StageKind.Practice?p.Speed*.5f:p.Speed;
            var forward=(e.Aim-e.Position).normalized;
            var right=Vector3.Cross(Mathf.Abs(Vector3.Dot(forward,Vector3.up))>.95f?Vector3.right:Vector3.up,forward).normalized;
            var up=Vector3.Cross(forward,right).normalized;
            // Establish safe gaps at the nearest point in the entire calibrated flight sphere.
            var plane=e.Position+forward*Mathf.Max(.25f,e.Position.magnitude-Calibration.Reach);
            for(int i=0;i<count;i++)
            {
                var target=e.Aim;
                if(fan)target=plane+right*((i-2+.5f)*p.SafeFanSpacing(Settings));
                if(ring){float a=(i+1)*Mathf.PI/4;target=plane+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*Mathf.Max(p.RingRadius,p.MinimumGap(Settings)/(.765f));}
                Emit(true,e.Id,e.Position,(target-e.Position).normalized*speed,p.Radius,1,fan,FleetOrdnance.Style(e));
            }
            Cue(FleetCue.EnemyShot,e.Position,e.Id,family:e.Family);return true;
        }
        public bool TryLaser()
        {
            if(GestureRules||ArcadeRules)return false;
            if(State!=RunState.Running || !LaserReady || !SpecialAllowed || Stage.Kind==StageKind.Rest || Stage.Kind==StageKind.FusionPractice)return false;
            LaserCharge=0;LaserRemaining=Settings.LaserDuration;Cue(FleetCue.Laser);return true;
        }
        void StepLaser(float dt)
        {
            if(LaserRemaining<=0)return;
            float active=Mathf.Min(dt,LaserRemaining);LaserRemaining=Mathf.Max(0,LaserRemaining-dt);
            var end=Motor.Position+Motor.Rotation*Vector3.forward*Settings.LaserRange;
            float damage=Settings.BoltDamage/Settings.FireInterval*Settings.LaserDpsMultiplier*active;
            for(int i=0;i<Enemies.Length;i++)if(Enemies[i].Active && FleetSegments.DistanceSquared(Motor.Position,end,Enemies[i].Previous,Enemies[i].Position)<=Mathf.Pow(Settings.LaserRadius+EnemyRadius(Enemies[i]),2))DamageEnemy(i,damage,true);
        }
        public static float EnemyRadius(FleetEnemy e)=>e.Family switch { EnemyFamily.Sentry=>.06f,EnemyFamily.Grenadier=>.09f,EnemyFamily.Sniper=>.06f,EnemyFamily.Frigate=>.09f,EnemyFamily.Battleship=>.13f,EnemyFamily.Carrier=>.18f,EnemyFamily.Follower=>.04f,EnemyFamily.Gatekeeper=>.24f,_=>.09f };
        void StepBullets(float dt)
        {
            for(int i=0;i<Bullets.Length;i++)
            {
                var b=Bullets[i]; if(!b.Active) continue;
                float previousAge=b.Age;
                b.Age+=dt; b.Previous=b.Position; b.Position+=b.Velocity*dt; b.Life-=dt;
                if(b.Life<=0 || b.Position.sqrMagnitude>Settings.ArenaRadius*Settings.ArenaRadius) b.Active=false;
                if(b.Active && b.Hostile && b.Style==FleetShot.Tile && previousAge<FleetOrdnance.TileDelay && b.Age>=FleetOrdnance.TileDelay)
                    Cue(FleetCue.TilePulse,b.Position,b.Owner,family:EnemyFamily.Tumbler);
                if(GestureRules&&b.Active&&b.Hostile&&Absorb(b))b.Active=false;
                if(b.Active&&b.Hostile&&FleetOrdnance.Touches(b,Motor.Previous,Motor.Position,Settings.CoreRadius))
                {
                    b.Active=false; Hit();
                    if(State==RunState.Results) return; // Hit clears every pool; do not write stale entries back.
                }
                else if(b.Active&&!b.Hostile)
                {
                    for(int j=0;j<Enemies.Length;j++) if(Enemies[j].Active&&FleetCollision.Sweep(b.Previous,b.Position,b.Radius,Enemies[j].Previous,Enemies[j].Position,EnemyRadius(Enemies[j])))
                    { DamageEnemy(j,b.Damage,false); b.Active=false; break; }
                }
                if(b.Active&&b.Hostile&&!b.NearMissed&&FleetOrdnance.Touches(b,Motor.Previous,Motor.Position,Settings.CoreRadius+.06f))
                {b.NearMissed=true;Cue(FleetCue.NearMiss,b.Position,b.Owner,.3f);}
                Bullets[i]=b;
            }
        }
        public void Hit()
        {
            if(State!=RunState.Running || Clock<InvulnerableUntil || Stage.Kind==StageKind.Practice || Stage.Kind==StageKind.FusionPractice) return;
            Shields--; Hits++; Multiplier=1; InvulnerableUntil=Clock+Settings.Invulnerability; Cue(FleetCue.Hit);
            if(Shields<=0) { State=RunState.Results; if(!Endless) Won=false; ClearCombat(); Cue(FleetCue.Defeat); } // Dying in endless mode still counts the boss as beaten.
        }
        public void DamageEnemy(int index,float amount,bool specialWeapon)
        {
            if(State!=RunState.Running || !Enemies[index].Active) return;
            var e=Enemies[index]; e.Health-=amount;
            if(e.Family==EnemyFamily.Gatekeeper&&e.Health>0)
            {
                int phase=e.Health/e.MaxHealth<=.3f?2:e.Health/e.MaxHealth<=.65f?1:0;
                if(phase>e.Phase){e.Phase=phase;e.State=EnemyState.Recovering;e.Timer=1.5f;e.Beam=false;RetireAttacks(e.Id);Cue(FleetCue.BossPhase,e.Position,e.Id,phase+1);}
            }
            Enemies[index]=e;
            if(e.Health>0) { Cue(FleetCue.EnemyHit,e.Position,e.Id,.5f); return; }
            Enemies[index].Active=false;
            for(int i=0;i<Bullets.Length;i++) if(!GestureRules&&!ArcadeRules&&Bullets[i].Hostile&&Bullets[i].Owner==e.Id) Bullets[i].Active=false;
            if(Stage.Kind==StageKind.Combat)
            {
                Score+=Pattern(e.Family).KillPoints*Multiplier;
                if(ArcadeRules) { Cue(FleetCue.Kill,e.Position,e.Id,e.Family==EnemyFamily.Gatekeeper?3:1); ArcadeKill(e); return; }
                if(GestureRules) {if(!specialWeapon)DropEnergy(e.Position);}
                else if(!specialWeapon){bool ready=LaserReady;LaserCharge=Mathf.Min(Settings.LaserKills,LaserCharge+1);if(!ready&&LaserReady)Cue(FleetCue.LaserReady);}
                if(!GestureRules&&e.Id%2==0) for(int i=0;i<Pickups.Length;i++) if(!Pickups[i].Active)
                { Pickups[i]=new FleetPickup{Active=true,Position=e.Position,Life=8}; break; }
            }
            Cue(FleetCue.Kill,e.Position,e.Id,e.Family==EnemyFamily.Gatekeeper?3:1);
        }
        void RetireAttacks(int owner)
        {for(int i=0;i<Bullets.Length;i++)if(Bullets[i].Hostile&&Bullets[i].Owner==owner)Bullets[i].Active=false;}
        void StepPickups(float dt)
        {
            for(int i=0;i<Pickups.Length;i++) if(Pickups[i].Active)
            {
                var p=Pickups[i]; var old=p.Position; p.Position=Vector3.MoveTowards(p.Position,Motor.Position,dt*.32f); p.Life-=dt;
                if(FleetCollision.Sweep(old,p.Position,.04f,Motor.Previous,Motor.Position,Settings.CoreRadius))
                { if(ArcadeRules)UpgradeGuns();else if(GestureRules)Energy=Mathf.Min(100,Energy+p.Energy);else Multiplier=Mathf.Min(5,Multiplier+1); p.Active=false; Cue(FleetCue.Pickup); }
                if(p.Life<=0) p.Active=false; Pickups[i]=p;
            }
        }
    }
}
