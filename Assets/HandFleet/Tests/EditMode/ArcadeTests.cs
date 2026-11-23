using NUnit.Framework;
using UnityEngine;
namespace HandFleet.Tests
{
    public class ArcadeTests
    {
        HandFleetSettings settings;HandFleetMission mission;HandFleetPattern pattern;HandFleetRun run;FleetInput input;
        [SetUp]public void Setup()
        {
            settings=ScriptableObject.CreateInstance<HandFleetSettings>();settings.EnemyCapacity=32;settings.BulletCapacity=683;settings.PickupCapacity=64;settings.Countdown=0;settings.StableTime=.15f;settings.BoltSpeed=5;
            mission=ScriptableObject.CreateInstance<HandFleetMission>();mission.Arcade=true;
            pattern=ScriptableObject.CreateInstance<HandFleetPattern>();pattern.Family=EnemyFamily.Sentry;pattern.Health=10;
            mission.Patterns=new[]{pattern};mission.Stages=new[]{new FleetStage{Kind=StageKind.Combat,Completion=EncounterEnd.ClearEnemies}};
            var hand=new FleetHand{Valid=true,Palm=new Pose(Vector3.down*.05f,Quaternion.identity),Forward=Vector3.forward,Normal=Vector3.right};
            input=new FleetInput{Focused=true,HeadPosition=Vector3.back,Left=hand,Right=hand};run=new HandFleetRun(settings,mission);run.Start(input);
        }
        [TearDown]public void Cleanup(){Object.DestroyImmediate(settings);Object.DestroyImmediate(mission);Object.DestroyImmediate(pattern);}
        void Tick(int count){for(int n=0;n<count;n++){input.Timestamp+=.01;run.Tick(input,.01f);}}
        int PlayerBullets(){int n=0;foreach(var b in run.Bullets)if(b.Active&&!b.Hostile)n++;return n;}

        [Test]public void ShipSnapsOntoThePalmAndHoldsItOneToOne()
        {
            // Grabbed from the cradle with the palm 5 cm below it: the ship jumps onto the palm, no offset kept.
            Assert.That(Vector3.Distance(run.Motor.Position,Vector3.down*.05f),Is.LessThan(1e-4f));
            var rotation=Quaternion.Euler(0,0,90);
            input.Right.Palm=new Pose(new Vector3(.3f,0,0),rotation);Tick(1);
            Assert.That(Vector3.Distance(run.Motor.Position,new Vector3(.3f,0,0)),Is.LessThan(1e-4f),"Exact 1:1 position, no lag.");
            Assert.That(Quaternion.Angle(run.Motor.Rotation,rotation),Is.LessThan(.01f),"Exact 1:1 rotation, no lag.");
        }
        [Test]public void FlightIsNotLimitedToAReachSphere()
        {
            for(int n=1;n<=10;n++){input.Right.Palm.position=new Vector3(n*.12f,0,0);Tick(1);}
            Assert.AreEqual(RunState.Running,run.State);Assert.That(run.Motor.Position.x,Is.GreaterThan(1.1f));
        }
        [Test]public void OneHitEndsTheRun()
        {
            Assert.AreEqual(1,run.Shields);
            run.Emit(true,7,run.Motor.Position+Vector3.forward*.05f,Vector3.back*2,.01f,1);Tick(5);
            Assert.AreEqual(RunState.Results,run.State);Assert.IsFalse(run.Won);
        }
        void PutSentry(int i,Vector3 p){run.Enemies[i]=new FleetEnemy{Active=true,Id=i+1,Family=EnemyFamily.Sentry,State=EnemyState.Recovering,Timer=100,Position=p,Previous=p,Home=p,Health=1000,MaxHealth=1000};}
        [Test]public void GunsHoldFireUntilAnEnemyIsLocked()
        {
            Tick(30);Assert.AreEqual(0,PlayerBullets(),"Nothing in the aim cone: no shots.");
            PutSentry(0,run.Motor.Position+Vector3.forward*2);Tick(1);
            Assert.AreEqual(1,PlayerBullets(),"Fires the moment an enemy is locked.");
            run.Enemies[0].Active=false;for(int i=0;i<run.Bullets.Length;i++)run.Bullets[i].Active=false;
            Tick(30);Assert.AreEqual(0,PlayerBullets(),"Stops again once the lock is lost.");
        }
        [Test]public void GunsStreamForwardAndUpgradeFromPickups()
        {
            PutSentry(0,run.Motor.Position+Vector3.forward*2);
            Tick(1);Assert.AreEqual(1,PlayerBullets());
            foreach(var b in run.Bullets)if(b.Active&&!b.Hostile)Assert.That(Vector3.Angle(b.Velocity,Vector3.forward),Is.LessThan(.01f));
            run.Pickups[0]=new FleetPickup{Active=true,Position=run.Motor.Position,Life=8};Tick(1);
            Assert.AreEqual(1,run.WeaponLevel);
            for(int i=0;i<run.Bullets.Length;i++)run.Bullets[i].Active=false;
            Tick(10);Assert.AreEqual(2,PlayerBullets());
            Assert.IsFalse(run.TryLaser());Assert.AreEqual(FusionState.Solo,run.Fusion.State);
        }
        [Test]public void TrackingLossResumesWithoutCountdown()
        {
            input.Right.Valid=false;Tick(5);Assert.AreEqual(RunState.TrackingHold,run.State);
            input.Right.Valid=true;input.Right.Palm.position=new Vector3(.2f,0,.05f);Tick(30);
            Assert.AreEqual(RunState.Running,run.State);Assert.That(run.Motor.Position.x,Is.EqualTo(.2f).Within(1e-4f));
            Assert.Greater(run.InvulnerableUntil,run.Clock);
        }
        // Keeps the ship alive in long simulations: no enemy fire reaches it and drones about to hit are removed.
        void Shelter()
        {
            for(int i=0;i<run.Bullets.Length;i++)if(run.Bullets[i].Hostile)run.Bullets[i].Active=false;
            for(int i=0;i<run.Enemies.Length;i++)
            {
                ref var e=ref run.Enemies[i];
                if(!e.Active)continue;
                if((e.Family==EnemyFamily.Follower||e.Family==EnemyFamily.Comet)&&Vector3.Distance(e.Position,run.Motor.Position)<.3f)e.Active=false;
                if(e.Family==EnemyFamily.Lancer&&e.State==EnemyState.Telegraph)e.Aim=e.Position+Vector3.right;
            }
        }
        int FindBoss(){for(int i=0;i<run.Enemies.Length;i++)if(run.Enemies[i].Active&&run.Enemies[i].Family==EnemyFamily.Gatekeeper)return i;return -1;}
        [Test]public void AuthoredWavesWithBreaksLeadToABossThenEndlessLaps()
        {
            int lastWave=0,breaks=0;
            for(int n=0;n<60000&&!run.BossWave;n++)
            {
                Tick(1);Assert.AreEqual(RunState.Running,run.State);
                if(run.Wave!=lastWave)
                {
                    Assert.AreEqual(lastWave+1,run.Wave,"Waves advance one at a time.");
                    Assert.IsTrue(run.InWaveBreak,"Every wave opens with a break.");breaks++;
                }
                if(run.InWaveBreak&&run.Wave>0)Assert.IsFalse(run.WarpPending,"Nothing warps in during the break.");
                lastWave=run.Wave;
                for(int i=0;i<run.Enemies.Length;i++)if(run.Enemies[i].Active&&run.Enemies[i].Family!=EnemyFamily.Gatekeeper)run.DamageEnemy(i,1000,false);
            }
            Assert.IsTrue(run.BossWave);Assert.AreEqual(settings.ArcadeWaves+1,run.Wave);Assert.AreEqual(settings.ArcadeWaves+1,breaks);
            int boss=FindBoss();Assert.GreaterOrEqual(boss,0);Assert.Greater(run.Score,0);
            run.DamageEnemy(boss,100000,false);
            Assert.AreEqual(RunState.Running,run.State,"Beating the boss starts endless mode.");Assert.IsTrue(run.Won);
            Assert.IsTrue(run.Endless);Assert.IsTrue(run.InWaveBreak);
            var lap=settings.ArcadeWaveTable[settings.ArcadeWaves-4];
            Assert.AreEqual(lap.MaxAlive+1,run.CurrentWave.MaxAlive,"Each endless lap allows one more enemy alive.");
            Assert.AreEqual(lap.MaxShooters+1,run.CurrentWave.MaxShooters);
        }
        [Test]public void WithoutEndlessTheBossEndsTheRun()
        {
            settings.EndlessAfterBoss=false;
            for(int n=0;n<60000&&!run.BossWave;n++){Tick(1);for(int i=0;i<run.Enemies.Length;i++)if(run.Enemies[i].Active&&run.Enemies[i].Family!=EnemyFamily.Gatekeeper)run.DamageEnemy(i,1000,false);}
            Tick(1);run.DamageEnemy(FindBoss(),100000,false);
            Assert.AreEqual(RunState.Results,run.State);Assert.IsTrue(run.Won);
        }
        [Test]public void FirstWaveIsThreeSentriesThatWarpInAfterAWarningInsideTheBand()
        {
            int arrivals=0;
            Vector3 heardAt=Vector3.zero;
            run.Feedback+=cue=>{if(cue.Kind==FleetCue.WarpArrival){arrivals++;heardAt=cue.Position;}};
            int n=0;while(!run.WarpPending&&n++<1000)Tick(1);
            Assert.IsTrue(run.WarpPending);Assert.AreEqual(1,run.Wave);Assert.AreEqual(0,run.ActiveEnemies,"The warning comes first.");
            Assert.AreEqual(0,arrivals);
            var warp=run.WarpPoint;var d=warp-run.ViewEye;
            Assert.LessOrEqual(Mathf.Atan2(Mathf.Abs(d.x),d.z)*Mathf.Rad2Deg,settings.SpawnBandYaw+.5f,"Inside the band horizontally.");
            float pitch=Mathf.Atan2(d.y,new Vector2(d.x,d.z).magnitude)*Mathf.Rad2Deg;
            Assert.That(pitch,Is.InRange(settings.SpawnBandCentrePitch-settings.SpawnBandPitch-.5f,settings.SpawnBandCentrePitch+settings.SpawnBandPitch+.5f),"Inside the band vertically.");
            Assert.That(d.magnitude,Is.InRange(settings.SpawnDepthMin-.01f,settings.SpawnDepthMax+.01f));
            float warned=run.Clock;while(run.WarpPending)Tick(1);
            Assert.That(run.Clock-warned,Is.EqualTo(settings.WarpWarning).Within(.02f));
            Assert.AreEqual(3,run.ActiveEnemies);
            Assert.AreEqual(1,arrivals);
            Assert.AreEqual(warp,heardAt);
            foreach(var e in run.Enemies)if(e.Active){Assert.AreEqual(EnemyFamily.Sentry,e.Family);Assert.Less(Vector3.Distance(e.Entry,warp),.01f,"Flies out of the warp point.");Assert.Less(Vector3.Distance(e.Home,warp),.5f,"Into a tight formation.");}
            // One second to settle plus the arrival grace: no shots before then.
            float arrived=run.Clock;
            while(run.Clock<arrived+1+settings.ArrivalGrace-.02f){Tick(1);foreach(var b in run.Bullets)Assert.IsFalse(b.Active&&b.Hostile,"No fire before the arrival grace ends.");}
        }
        [Test]public void TilePulseSoundsOnceAtTheMarkedPositionWhenTheTileArms()
        {
            var position=run.Motor.Position+Vector3.forward;
            run.Bullets[0]=new FleetBullet{Active=true,Hostile=true,Style=FleetShot.Tile,Position=position,Life=1,Owner=42};
            int pulses=0;
            run.Feedback+=cue=>{
                if(cue.Kind!=FleetCue.TilePulse)return;
                pulses++;
                Assert.AreEqual(position,cue.Position);
                Assert.AreEqual(42,cue.Source);
            };
            Tick(71);
            Assert.AreEqual(0,pulses);
            Tick(2);
            Assert.AreEqual(1,pulses);
            Tick(27);
            Assert.AreEqual(1,pulses);
        }
        [Test]public void WavesCapEnemiesAliveAndShootingAtOnce()
        {
            int maxShooting=0;
            for(int n=0;n<40000&&run.Wave<=settings.ArcadeWaves&&!run.BossWave;n++)
            {
                Tick(1);Shelter();Assert.AreEqual(RunState.Running,run.State);
                var spec=run.CurrentWave;int alive=0,shooting=0;
                foreach(var e in run.Enemies)
                {
                    if(!e.Active)continue;
                    if(e.ParentId==0)alive++;
                    if(e.Family!=EnemyFamily.Follower&&(e.State==EnemyState.Telegraph||(e.FiredAt>0&&run.Clock-e.FiredAt<settings.ShooterWindow)))shooting++;
                }
                Assert.LessOrEqual(alive,spec.MaxAlive,"Wave "+run.Wave+" alive cap.");
                Assert.LessOrEqual(shooting,spec.MaxShooters,"Wave "+run.Wave+" shooter cap.");
                maxShooting=Mathf.Max(maxShooting,shooting);
                // A slow player: each enemy dies four seconds after it arrives, so caps are actually reached.
                for(int i=0;i<run.Enemies.Length;i++)if(run.Enemies[i].Active&&run.Enemies[i].Family!=EnemyFamily.Gatekeeper&&run.Enemies[i].Age>4)run.DamageEnemy(i,1000,false);
            }
            Assert.IsTrue(run.BossWave,"Reached the boss.");Assert.GreaterOrEqual(maxShooting,3,"The caps were exercised.");
        }
        [Test]public void AimLineStopsOnTheFirstEnemyInTheLineOfFire()
        {
            run.Motor.Position=Vector3.zero;run.Motor.Rotation=Quaternion.identity;
            Assert.AreEqual(3,run.AimRange(3,out int none));Assert.AreEqual(-1,none);
            void Put(int i,Vector3 p){run.Enemies[i]=new FleetEnemy{Active=true,Id=i+1,Family=EnemyFamily.Sentry,Position=p,Previous=p,Home=p,Health=10,MaxHealth=10};}
            Put(0,new Vector3(0,0,2));Put(1,new Vector3(0,0,1.2f));Put(2,new Vector3(.5f,0,.5f));
            float range=run.AimRange(3,out int target);float r=HandFleetRun.EnemyRadius(run.Enemies[1]);
            Assert.AreEqual(1,target,"Nearest enemy on the line wins; the one off to the side is ignored.");
            Assert.That(range,Is.EqualTo(1.2f-HandFleetRun.GunMuzzle-r).Within(1e-4f));
            Put(3,new Vector3(0,0,-1));run.Enemies[1].Active=false;run.AimRange(3,out target);Assert.AreEqual(0,target,"Enemies behind the ship are ignored.");
        }
        [Test]public void AimConeLocksEnemiesJustOffTheLineOfFire()
        {
            run.Motor.Position=Vector3.zero;run.Motor.Rotation=Quaternion.identity;settings.AimConeAngle=3;
            var muzzle=Vector3.forward*HandFleetRun.GunMuzzle;
            void Put(float degrees){var p=muzzle+Quaternion.Euler(0,degrees,0)*Vector3.forward*2;run.Enemies[0]=new FleetEnemy{Active=true,Id=1,Family=EnemyFamily.Sentry,Position=p,Previous=p,Home=p,Health=10,MaxHealth=10};}
            // A sentry 2 m out subtends ~1.7 degrees, so with a 3 degree cone it locks up to ~4.7 degrees off axis.
            Put(4);run.AimAt(3,out int target,out var direction);
            Assert.AreEqual(0,target,"Inside the cone tolerance.");
            Assert.That(Vector3.Angle(direction,run.Enemies[0].Position-muzzle),Is.LessThan(.01f),"The aim bends onto the locked enemy.");
            Put(6);run.AimAt(3,out target,out direction);
            Assert.AreEqual(-1,target,"Outside the cone.");Assert.That(Vector3.Angle(direction,Vector3.forward),Is.LessThan(.01f));
            settings.AimConeAngle=0;Put(4);run.AimAt(3,out target,out _);Assert.AreEqual(-1,target,"A zero cone is the old exact line.");
        }
        void PutDrone(Vector3 p){run.Enemies[0]=new FleetEnemy{Active=true,Id=99,Family=EnemyFamily.Follower,State=EnemyState.Recovering,Position=p,Previous=p,Home=p,Health=1,MaxHealth=1};}
        [Test]public void SuicideDroneHitsAStillShip()
        {
            Tick(5);PutDrone(run.Motor.Position+Vector3.up*.6f);
            for(int n=0;n<150&&run.State==RunState.Running;n++)Tick(1);
            Assert.AreEqual(RunState.Results,run.State);
        }
        [Test]public void ManeuveringBreaksSuicideDroneLock()
        {
            Tick(5);PutDrone(run.Motor.Position+Vector3.up*.55f);
            Tick(20);var before=run.Enemies[0].Velocity;Assert.Greater(before.magnitude,.1f,"The drone has picked up speed toward the ship.");
            // A fast sideways swerve: the drone loses lock and keeps flying straight past.
            for(int n=0;n<25;n++){input.Left.Palm.position+=Vector3.right*.03f;input.Right.Palm.position+=Vector3.right*.03f;Tick(1);}
            Assert.AreEqual(RunState.Running,run.State,"The swerve dodged the drone.");
            Assert.That(Vector3.Angle(run.Enemies[0].Velocity,before),Is.LessThan(30),"No hard turn while the lock is broken.");
            Assert.Greater(run.Enemies[0].Timer,0,"Lock-loss countdown is running.");
        }
        [Test]public void DroneThatFliesPastTheShipSelfDestructsInsteadOfTurningBack()
        {
            Tick(5);int kills=run.Kills;
            // Just below the ship and still diving away from it: the ship is 180 degrees off its heading, so it has missed.
            PutDrone(run.Motor.Position+Vector3.down*.2f);run.Enemies[0].Phase=1;run.Enemies[0].Velocity=Vector3.down*1.2f;
            Tick(1);
            Assert.IsFalse(run.Enemies[0].Active,"A drone never turns back: it blows itself up at once.");
            Assert.AreEqual(kills,run.Kills,"Self-destruct is not a player kill.");Assert.AreEqual(RunState.Running,run.State);
        }
        [Test]public void DroneStillChasesWhileTheShipIsLessThan90DegreesOffItsHeading()
        {
            Tick(5);
            // Flying straight ahead with the ship 60 degrees off to the side: a turn under 90 degrees, so it keeps hunting.
            var heading=Vector3.forward;var p=run.Motor.Position-Quaternion.Euler(0,60,0)*heading*.5f;
            PutDrone(p);run.Enemies[0].Phase=1;run.Enemies[0].Velocity=heading*1f;
            Tick(1);Assert.IsTrue(run.Enemies[0].Active);
            Assert.Less(Vector3.Angle(run.Enemies[0].Velocity,run.Motor.Position-run.Enemies[0].Position),60,"It turns toward the ship.");
        }
        [Test]public void DroneThatLeavesThePlayWindowSelfDestructs()
        {
            Tick(5);
            // The ship is flown out past the right edge of the window; a drone just inside that edge chases it straight out.
            for(int n=0;n<16;n++){input.Left.Palm.position+=Vector3.right*.1f;input.Right.Palm.position+=Vector3.right*.1f;Tick(1);}
            Assert.AreEqual(RunState.Running,run.State);Assert.IsFalse(run.InView(run.Motor.Position));
            float edge=Mathf.Tan(settings.ViewHorizontalFov*.5f*Mathf.Deg2Rad);float depth=run.Motor.Position.z-run.ViewEye.z;
            var p=run.ViewEye+new Vector3(edge*depth-.005f,run.Motor.Position.y-run.ViewEye.y,depth);
            PutDrone(p);run.Enemies[0].Phase=1;run.Enemies[0].Velocity=(run.Motor.Position-p).normalized*1.3f;
            Tick(1);Assert.IsTrue(run.Enemies[0].Active,"Still inside the window, heading straight at the ship.");
            Tick(2);
            Assert.IsFalse(run.Enemies[0].Active,"Drones stay inside the 70 x 66 degree window or blow up.");
        }
        [Test]public void EnemiesSpawnAndMoveOnlyInsideThe70By66DegreeWindow()
        {
            float h=settings.ViewHorizontalFov*.5f+.01f,v=settings.ViewVerticalFov*.5f+.01f;int seen=0;
            for(int n=0;n<6000&&run.State==RunState.Running;n++)
            {
                Tick(1);
                // Keep the ship alive: clear enemy fire and drones about to hit, so the run lasts long enough to see every type.
                for(int i=0;i<run.Bullets.Length;i++)if(run.Bullets[i].Hostile)run.Bullets[i].Active=false;
                for(int i=0;i<run.Enemies.Length;i++)if(run.Enemies[i].Active&&run.Enemies[i].Family==EnemyFamily.Follower&&Vector3.Distance(run.Enemies[i].Position,run.Motor.Position)<.3f)run.Enemies[i].Active=false;
                foreach(var e in run.Enemies)
                {
                    if(!e.Active)continue;seen++;var d=e.Position-run.ViewEye;
                    Assert.Greater(d.z,0,"In front of the player.");
                    Assert.LessOrEqual(Mathf.Atan2(Mathf.Abs(d.x),d.z)*Mathf.Rad2Deg,h,e.Family+" left the window horizontally.");
                    Assert.LessOrEqual(Mathf.Atan2(Mathf.Abs(d.y),d.z)*Mathf.Rad2Deg,v,e.Family+" left the window vertically.");
                }
                // Thin the field so later waves (and every enemy type) keep arriving.
                if(n%50==0)for(int i=0;i<run.Enemies.Length;i++)if(run.Enemies[i].Active&&run.Enemies[i].Family!=EnemyFamily.Follower)run.DamageEnemy(i,1000,false);
            }
            Assert.Greater(seen,1000);Assert.Greater(run.Wave,3);
        }
        [Test]public void LeaderboardKeepsTopTenInOrder()
        {
            var board=ArcadeLeaderboard.Parse("500|a;garbage;1500|b");
            Assert.AreEqual(1500,board.Best);
            Assert.AreEqual(2,board.Insert(900,"c"));
            for(int i=0;i<12;i++)board.Insert(100+i,"d");
            Assert.AreEqual(ArcadeLeaderboard.Size,board.Entries.Count);Assert.AreEqual(0,board.Insert(1,"e"));
            var copy=ArcadeLeaderboard.Parse(board.Serialize());
            Assert.AreEqual(board.Entries.Count,copy.Entries.Count);Assert.AreEqual(900,copy.Entries[1].Score);
            StringAssert.Contains("> ",board.Display(2));
        }
    }
}
