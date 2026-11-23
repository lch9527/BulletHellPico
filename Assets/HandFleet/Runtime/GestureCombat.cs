using UnityEngine;

namespace HandFleet
{
    public sealed partial class HandFleetRun
    {
        public bool GestureRules => Mission.GestureCampaign;
        public readonly FleetGesture Gesture=new();
        public FleetMode Mode=>Gesture.Mode;
        public float Energy {get; private set;}
        public float ShieldHeat {get; private set;}
        public float ShieldCooldown {get; private set;}
        public bool ShieldActive=>GestureRules&&State==RunState.Running&&Mode==FleetMode.Defense&&ShieldCooldown<=0&&Stage.Kind!=StageKind.Rest;
        public int TargetId {get; private set;}=-1;
        public Vector3 ShotEnd {get; private set;}
        public float ShotUntil {get; private set;}
        float absorbCredit,groupSpawnAt;
        int currentGroup;
        bool ultimate;
        public float ActiveCost { get {float cost=0;foreach(var e in Enemies)if(e.Active)cost+=e.MaxHealth/20;return cost;} }

        void ResetGestures()
        { Gesture.Reset();Energy=Stage.Kind==StageKind.Practice?90:0;ShieldHeat=ShieldCooldown=absorbCredit=groupSpawnAt=0;currentGroup=0;ultimate=false;TargetId=-1;ShotUntil=0; }
        void SuspendGestures() {Gesture.Reset();ultimate=false;LaserRemaining=0;TargetId=-1;}
        void StepGestureCombat(FleetInput input,float dt)
        {
            Gesture.Step(input,Calibration.Dominant,dt);
            ShieldCooldown=Mathf.Max(0,ShieldCooldown-dt);
            absorbCredit=Mathf.Min(1,absorbCredit+dt*Settings.AbsorbPerSecond);
            if(Stage.Kind==StageKind.Rest){ShieldHeat=Mathf.Max(0,ShieldHeat-dt/Settings.ShieldCooling);ultimate=false;LaserRemaining=0;return;}
            if(ShieldActive)
            {
                ShieldHeat+=dt/Settings.ShieldDuration;
                if(ShieldHeat>=1){ShieldHeat=1;ShieldCooldown=Settings.ShieldLockout;Cue(FleetCue.ShieldBreak);}
            }
            else ShieldHeat=Mathf.Max(0,ShieldHeat-dt/Settings.ShieldCooling);
            if(Mode!=FleetMode.Ultimate)ultimate=false;
            else if(!ultimate&&Energy>Settings.UltimateThreshold){ultimate=true;Cue(FleetCue.Laser);}
            if(ultimate)
            {
                var forward=input.DesktopMode!=FleetMode.Neutral?Motor.Rotation*Vector3.forward:input.Hand(Calibration.Dominant).Normal;
                if(forward.sqrMagnitude>.5f)Motor.Rotation=Quaternion.LookRotation(forward,Vector3.up);
                float active=Mathf.Min(dt,Energy/Settings.UltimateDrain);Energy=Mathf.Max(0,Energy-Settings.UltimateDrain*active);
                var end=Motor.Position+forward*Settings.LaserRange;
                for(int i=0;i<Enemies.Length;i++)if(Enemies[i].Active&&FleetSegments.DistanceSquared(Motor.Position,end,Enemies[i].Previous,Enemies[i].Position)<=Mathf.Pow(Settings.LaserRadius+EnemyRadius(Enemies[i]),2))
                    DamageEnemy(i,Enemies[i].Family==EnemyFamily.Gatekeeper?Settings.UltimateBossDps*active:Enemies[i].Health,true);
                for(int i=0;i<Bullets.Length;i++)if(Bullets[i].Active&&Bullets[i].Hostile&&FleetSegments.DistanceSquared(Motor.Position,end,Bullets[i].Position,Bullets[i].Position+Bullets[i].Velocity*dt)<=Mathf.Pow(Settings.LaserRadius+Bullets[i].Radius,2))Bullets[i].Active=false;
                if(Energy<=0)ultimate=false;
            }
            LaserRemaining=ultimate?.01f:0;
            if(Stage.Kind==StageKind.Combat&&Motor.Position.magnitude+Settings.CoreRadius>=Calibration.Reach)
            {Shields=0;Won=false;State=RunState.Results;ClearCombat();Cue(FleetCue.Defeat);}
        }
        void FireAcquiredTarget()
        {
            if(Mode!=FleetMode.Attack){TargetId=-1;return;}
            var forward=Motor.Rotation*Vector3.forward;int chosen=-1;float best=-1;
            for(int i=0;i<Enemies.Length;i++)
            {
                var e=Enemies[i];if(!e.Active)continue;var offset=e.Position-Motor.Position;
                if(offset.magnitude>Settings.TargetRange||offset.sqrMagnitude<.0001f)continue;
                float alignment=Vector3.Dot(forward,offset.normalized);
                if(e.Id==TargetId&&alignment>=Mathf.Cos(Settings.RetainAngle*Mathf.Deg2Rad)){chosen=i;break;}
                if(alignment>=Mathf.Cos(Settings.AcquireAngle*Mathf.Deg2Rad)&&alignment>best){chosen=i;best=alignment;}
            }
            if(chosen<0){TargetId=-1;return;}
            var direction=(Enemies[chosen].Position-Motor.Position).normalized;
            float nearest=Vector3.Distance(Enemies[chosen].Position,Motor.Position);
            for(int i=0;i<Enemies.Length;i++)if(Enemies[i].Active&&i!=chosen)
            {
                float along=Vector3.Dot(Enemies[i].Position-Motor.Position,direction);
                if(along>0&&along<nearest&&Vector3.Distance(Motor.Position+direction*along,Enemies[i].Position)<=EnemyRadius(Enemies[i])){chosen=i;nearest=along;}
            }
            TargetId=Enemies[chosen].Id;ShotEnd=Enemies[chosen].Position;ShotUntil=Clock+.035f;
            Cue(FleetCue.PlayerShot);DamageEnemy(chosen,Settings.BoltDamage,false);
        }
        bool Absorb(FleetBullet bullet)
        {
            if(!ShieldActive||!FleetOrdnance.Touches(bullet,Motor.Previous,Motor.Position,Settings.ShieldRadius))return false;
            if(absorbCredit>=1){absorbCredit-=1;Energy=Mathf.Min(100,Energy+Settings.EnergyPerAbsorb);}
            Cue(FleetCue.Absorb,bullet.Position);return true;
        }
        void DropEnergy(Vector3 position)
        {
            for(int i=0;i<Pickups.Length;i++)if(!Pickups[i].Active){Pickups[i]=new FleetPickup{Active=true,Position=position,Life=8,Energy=Settings.EnergyPerKill};return;}
            if(Pickups.Length>0){Pickups[0].Energy+=Settings.EnergyPerKill;Pickups[0].Life=8;}
        }
        void StepBudget()
        {
            if(EncounterCursor>=Stage.Spawns.Length||Clock<groupSpawnAt)return;
            var spawn=Stage.Spawns[EncounterCursor];
            if(spawn.Group>currentGroup&&ActiveCost>Stage.HpBudget*Settings.NextGroupFraction)return;
            float cost=spawn.Family==EnemyFamily.Gatekeeper?Settings.BossHealth/20:Pattern(spawn.Family).Health/20;
            if(ActiveEnemies>=Enemies.Length||ActiveCost+cost>Stage.HpBudget+.0001f)return;
            currentGroup=spawn.Group;Spawn(spawn);EncounterCursor++;groupSpawnAt=Clock+Settings.SpawnSpacing;
        }
        void StepNewEnemy(int index,float dt)
        {
            var e=Enemies[index];var p=Pattern(e.Family);e.Previous=e.Position;e.Age+=dt;e.Timer-=dt;
            if(e.State==EnemyState.Arriving)
            {e.Position=Vector3.Lerp(e.Entry,e.Home,Mathf.SmoothStep(0,1,1-e.Timer));if(e.Timer<=0){e.State=EnemyState.Recovering;e.Timer=ArcadeRules?Settings.ArrivalGrace:1;}Enemies[index]=e;return;}
            if(e.Family==EnemyFamily.Follower)
            {
                if(StepSuicideDrone(ref e,dt)){Enemies[index]=e;return;}
                if(FleetCollision.Sweep(e.Previous,e.Position,EnemyRadius(e),Motor.Previous,Motor.Position,Settings.CoreRadius)){Hit();if(State==RunState.Results)return;e.Active=false;Cue(FleetCue.Kill,e.Position,e.Id);}
                Enemies[index]=e;return;
            }
            // Arcade frigates sway like the rest instead of orbiting the player, and nothing leaves the play window.
            if(e.Family==EnemyFamily.Frigate&&!ArcadeRules)e.Position=Quaternion.Euler(0,e.Age*35,0)*e.Home;
            else if(e.PathAmplitude>0)e.Position=e.Home+e.PathAxis*Mathf.Sin(e.Age*e.PathSpeed)*e.PathAmplitude;
            if(ArcadeRules)e.Position=KeepInView(e.Position,EnemyRadius(e));
            if(e.State==EnemyState.Recovering&&e.Timer<=0)
            {
                // Arcade: wait (with a little jitter so shooters don't sync up) while the wave's shooter slots are taken.
                if(ArcadeRules&&!ShooterSlotFree(e.Id))e.Timer=.1f+.2f*Random01();
                else {e.State=EnemyState.Telegraph;e.Shots=0;e.Timer=p.Telegraph;Cue(FleetCue.Charge,e.Position,e.Id,family:e.Family);}
            }
            if(e.State==EnemyState.Telegraph&&e.Timer<=0)
            {
                bool emitted=e.Family==EnemyFamily.Carrier?Summon(e):NewPattern(e,p);
                if(emitted){e.FiredAt=Clock;if(e.Family==EnemyFamily.Frigate&&e.Shots++==0)e.Timer=.15f;else {e.State=EnemyState.Recovering;e.Timer=p.Recovery-(e.Family==EnemyFamily.Frigate?.15f:0);}}else{e.Timer=.05f;DeferredPatterns++;}
            }
            Enemies[index]=e;
        }
        // Suicide drones fly with momentum and a limited turn rate instead of sliding straight at the ship, so a late sidestep makes them overshoot.
        // A fast maneuver (ship speed above ManeuverSpeed) breaks their lock: they coast straight on for FollowerLockLoss seconds before re-acquiring.
        // They never turn back: once the ship is FollowerMissAngle (90) degrees or more off their heading they have missed and blow themselves up,
        // as they also do on leaving the arcade play window. e.Timer is the lock-loss countdown (it ticks down every step); e.Phase marks the drone
        // as armed so the post-arrival timer is cleared once. Returns true when the drone blew itself up (no score).
        bool StepSuicideDrone(ref FleetEnemy e,float dt)
        {
            if(e.Phase==0){e.Phase=1;e.Timer=0;}
            var toShip=Motor.Position-e.Position;
            if(Motion.Velocity.magnitude>=Settings.ManeuverSpeed)
            {
                if(e.Timer<=0&&toShip.sqrMagnitude<.36f)Cue(FleetCue.NearMiss,e.Position,e.Id);
                e.Timer=Settings.FollowerLockLoss;
            }
            bool missed=e.Velocity.sqrMagnitude>.0025f&&toShip.sqrMagnitude>1e-8f&&Vector3.Angle(e.Velocity,toShip)>=Settings.FollowerMissAngle;
            if(missed||(ArcadeRules&&!InView(e.Position))){e.Active=false;Cue(FleetCue.Kill,e.Position,e.Id);return true;}
            if(e.Timer<=0&&toShip.sqrMagnitude>1e-8f)
            {
                var want=toShip.normalized;float speed=e.Velocity.magnitude;
                var heading=speed>1e-4f?Vector3.RotateTowards(e.Velocity/speed,want,Settings.FollowerTurnRate*Mathf.Deg2Rad*dt,0):want;
                speed=Mathf.MoveTowards(speed,Settings.FollowerSpeed,Settings.FollowerAcceleration*dt);
                e.Velocity=heading*speed;
            }
            e.Position+=e.Velocity*dt;
            return false;
        }
        bool Summon(FleetEnemy carrier)
        {
            int owned=0,total=0;foreach(var e in Enemies)if(e.Active&&e.Family==EnemyFamily.Follower){total++;if(e.ParentId==carrier.Id)owned++;}
            if(owned+3>6||total+3>12||ActiveEnemies+3>Enemies.Length||(!ArcadeRules&&ActiveCost+.75f>Stage.HpBudget))return false;
            for(int n=0;n<3;n++)
            {
                var position=carrier.Position+new Vector3((n-1)*.12f,-.15f,0);if(ArcadeRules)position=KeepInView(position,.05f);
                Spawn(new FleetSpawn{Family=EnemyFamily.Follower,Position=position,EntryOffset=Vector3.zero});
                for(int i=0;i<Enemies.Length;i++)if(Enemies[i].Active&&Enemies[i].Id==enemyId){var e=Enemies[i];e.ParentId=carrier.Id;Enemies[i]=e;break;}
            }
            Cue(FleetCue.CarrierLaunch,carrier.Position,carrier.Id,family:carrier.Family);
            return true;
        }
        bool NewPattern(FleetEnemy e,HandFleetPattern p)
        {
            int count=e.Family==EnemyFamily.Grenadier?6:e.Family==EnemyFamily.Battleship?7:e.Family==EnemyFamily.Shutter?3:1;
            if(FreeSlots(true)<count)return false;
            var aim=Motor.Position;var forward=(aim-e.Position).normalized;
            var right=Vector3.Cross(forward,Vector3.up).normalized;if(right.sqrMagnitude<.1f)right=Vector3.right;
            float reaction=e.Family==EnemyFamily.Sniper?1:e.Family==EnemyFamily.Frigate||e.Family==EnemyFamily.Battleship?1.5f:3;
            float speed=Vector3.Distance(e.Position,aim)/reaction;
            for(int n=0;n<count;n++)
            {
                var target=aim;
                if(e.Family==EnemyFamily.Grenadier){float angle=(e.Id*1.37f+n*2.39996f);target+=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0)*(.12f+.08f*n);}
                else if(count>1)target+=right*((n-(count-1)*.5f)*.16f);
                var origin=e.Family==EnemyFamily.Battleship?e.Position+right*((n-3)*.048f):e.Position;
                var direction=e.Family==EnemyFamily.Battleship?forward:(target-e.Position).normalized;
                Emit(true,e.Id,origin,direction*speed,p.Radius,1,false,FleetOrdnance.Style(e));
            }
            Cue(FleetCue.EnemyShot,e.Position,e.Id,family:e.Family);return true;
        }
    }
}
