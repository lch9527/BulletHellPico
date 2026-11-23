using NUnit.Framework;
using UnityEngine;

namespace HandFleet.Tests
{
    public sealed class FlightTests
    {
        HandFleetSettings s;HandFleetMission mission;HandFleetPattern pattern;
        [SetUp]public void Setup()
        {
            s=ScriptableObject.CreateInstance<HandFleetSettings>();pattern=ScriptableObject.CreateInstance<HandFleetPattern>();mission=ScriptableObject.CreateInstance<HandFleetMission>();
            mission.Patterns=new[]{pattern};mission.Stages=new[]{new FleetStage{Title="Test",Kind=StageKind.Combat,Duration=100,MaxEnemies=0,Families=new[]{EnemyFamily.Needle}}};
        }
        [TearDown]public void Cleanup(){Object.DestroyImmediate(s);Object.DestroyImmediate(pattern);Object.DestroyImmediate(mission);}
        static FleetInput Input(float t=0)
        {
            var a=new FleetHand{Valid=true,Palm=new Pose(new Vector3(-.03f,0,0),Quaternion.identity),Normal=Vector3.right,Forward=Vector3.forward,ConfidentFingers=31};
            var b=a;b.Palm.position=new Vector3(.03f,0,0);b.Normal=Vector3.left;
            return new FleetInput{Focused=true,Timestamp=t,Left=a,Right=b};
        }
        [Test]public void FusionRequiresDwellAndDeliberateSeparation()
        {
            var f=new FleetFusion();var c=new FleetCalibration();var input=Input();Assert.IsTrue(c.CalibrateContact(input));
            for(int i=0;i<20;i++)f.Step(input,c,s,.02f,true,false);Assert.AreEqual(FusionState.Preview,f.State);
            for(int i=0;i<25;i++)f.Step(input,c,s,.02f,true,false);Assert.AreEqual(FusionState.Fused,f.State);
            input.Left.Valid=false;f.Step(input,c,s,1,true,false);Assert.AreEqual(FusionState.Fused,f.State);
            input.Left.Valid=true;input.Left.Palm.position=Vector3.left*.3f;
            for(int i=0;i<8;i++)f.Step(input,c,s,.02f,true,false);Assert.AreEqual(FusionState.Fused,f.State);
            for(int i=0;i<30;i++)f.Step(input,c,s,.02f,true,false);Assert.AreEqual(FusionState.Solo,f.State);
        }
        [Test]public void MirroredPalmCalibrationMapsBothHandsToForward()
        {
            var input=Input();input.Left.Palm.rotation=Quaternion.Euler(0,45,0);input.Right.Palm.rotation=Quaternion.Euler(0,-35,0);
            var c=new FleetCalibration{Dominant=0};Assert.IsTrue(c.Calibrate(input,Vector3.forward));
            Assert.Less(Quaternion.Angle(c.Aim(input,0),Quaternion.identity),.01f);Assert.Less(Quaternion.Angle(c.Aim(input,1),Quaternion.identity),.01f);
        }
        [Test]public void FollowerBoundsErrorAndSettlesWithoutOvershoot()
        {
            var input=Input();var c=new FleetCalibration();c.Calibrate(input,Vector3.forward);var f=new FleetFusion();var m=new FleetMotor();m.Rebase(input,c,f);
            input.Right.Palm.position+=Vector3.right*.3f;
            for(int i=0;i<150;i++){m.Step(input,c,f,s,.01f);Assert.LessOrEqual(Vector3.Distance(m.Position,m.Target),s.SoloError+.0001f);Assert.LessOrEqual(m.Position.x,.3001f);}
            Assert.Less(Vector3.Distance(m.Position,m.Target),.001f);
        }
        [Test]public void MotionDiscontinuitiesDoNotGenerateAnimationSpikes()
        {
            var m=new FleetMotionEstimator();m.Sample(Vector3.zero,0,.35f);m.Sample(Vector3.right*.01f,.02,.35f);Assert.Greater(m.Velocity.magnitude,0);
            m.Sample(Vector3.right*2,.04,.35f);Assert.AreEqual(Vector3.zero,m.Acceleration);m.Reset();Assert.AreEqual(Vector3.zero,m.Velocity);
        }
        [Test]public void RelativeSweepCatchesCrossingAndPreservesNearMiss()
        {
            Assert.IsTrue(FleetCollision.Sweep(Vector3.left,Vector3.right,.01f,Vector3.right,Vector3.left,.02f));
            Assert.IsFalse(FleetCollision.Sweep(Vector3.left+Vector3.up*.031f,Vector3.right+Vector3.up*.031f,.01f,Vector3.zero,Vector3.zero,.02f));
        }
        [Test]public void TrackingFreezesEveryCombatClockAndRebasesBeforeResume()
        {
            var run=new HandFleetRun(s,mission);var input=Input();run.Start(input);var initial=run.Motor.Position;run.Emit(true,1,Vector3.forward,Vector3.back,.01f,1);
            input.Right.Valid=false;run.Tick(input,.02f);float clock=run.Clock;var bullet=run.Bullets[0].Position;
            for(int i=0;i<100;i++){input.Timestamp+=.02;run.Tick(input,.02f);}
            Assert.AreEqual(clock,run.Clock);Assert.AreEqual(bullet,run.Bullets[0].Position);Assert.AreEqual(3,run.Shields);
            input.Right.Valid=true;input.Right.Palm.position=Vector3.right*.5f;
            for(int i=0;i<175;i++){input.Timestamp+=.02;run.Tick(input,.02f);}Assert.AreNotEqual(RunState.Running,run.State);
            for(int i=0;i<5;i++){input.Timestamp+=.02;run.Tick(input,.02f);}Assert.AreEqual(RunState.Running,run.State);Assert.Less(Vector3.Distance(initial,run.Motor.Position),.001f);
        }
        [Test]public void InvulnerabilityScoringAndLaserNeverRefillThroughLaserKills()
        {
            var run=new HandFleetRun(s,mission);run.Start(Input());run.Hit();run.Hit();Assert.AreEqual(2,run.Shields);Assert.AreEqual(1,run.Hits);
            run.Enemies[0]=new FleetEnemy{Active=true,Id=1,Health=1,Family=EnemyFamily.Needle};Assert.IsTrue(run.TryLaser());run.DamageEnemy(0,2,true);
            Assert.AreEqual(100,run.Score);Assert.AreEqual(0,run.LaserCharge);
            run.Enemies[0]=new FleetEnemy{Active=true,Id=2,Health=1,Family=EnemyFamily.Needle};run.DamageEnemy(0,2,false);Assert.AreEqual(1,run.LaserCharge);Assert.AreEqual(200,run.Score);
        }
        [Test]public void FusionPreviewSuppressesLaserAndDoesNotGrantResources()
        {
            var run=new HandFleetRun(s,mission);var input=Input();run.Start(input);run.Calibration.CalibrateContact(input);
            run.Fusion.Step(input,run.Calibration,s,.02f,true,false);Assert.IsFalse(run.TryLaser());Assert.AreEqual(3,run.Shields);Assert.AreEqual(0,run.Score);
        }
        [Test]public void PatternGapsIncludeFusedControlError()
        {Assert.GreaterOrEqual(pattern.SafeFanSpacing(s),2*(pattern.Radius+s.CoreRadius+s.FusedError)+.02f);}
        [Test]public void RetryClearsAllTransientState()
        {
            var run=new HandFleetRun(s,mission);run.Start(Input());run.Hit();run.Emit(true,1,Vector3.zero,Vector3.zero,.01f,1);run.Pause();run.Start(Input());
            Assert.AreEqual(3,run.Shields);Assert.AreEqual(0,run.ActiveBullets);Assert.AreEqual(0,run.Clock);Assert.AreEqual(0,run.Score);Assert.IsTrue(run.LaserReady);
        }
        [Test]public void ProjectileHitEndsRunAndRetiresAllAttacks()
        {
            var run=new HandFleetRun(s,mission);var input=Input();run.Start(input);
            for(int hit=0;hit<3;hit++)
            {
                for(int frame=0;frame<60;frame++){input.Timestamp+=.02;run.Tick(input,.02f);}
                run.Emit(true,1,run.Motor.Position+Vector3.forward*.05f,Vector3.back*5,.01f,1);
                input.Timestamp+=.02;run.Tick(input,.02f);
            }
            Assert.AreEqual(0,run.Shields);Assert.AreEqual(RunState.Results,run.State);Assert.IsFalse(run.Won);Assert.AreEqual(0,run.ActiveBullets);
        }
        [Test]public void EnemyThreatBudgetAndPoolsRemainBounded()
        {
            mission.Stages[0].Kind=StageKind.Practice;mission.Stages[0].MaxEnemies=6;mission.Stages[0].SpawnInterval=.02f;
            var run=new HandFleetRun(s,mission);var input=Input();run.Start(input);int max=0;
            for(int frame=0;frame<1000;frame++)
            {
                input.Timestamp+=.02;run.Tick(input,.02f);int attackers=0;
                foreach(var e in run.Enemies)if(e.Active&&(e.State==EnemyState.Telegraph||e.State==EnemyState.Attacking))attackers++;
                max=Mathf.Max(max,run.ActiveBullets);Assert.LessOrEqual(attackers,2);Assert.LessOrEqual(run.ActiveEnemies,6);Assert.LessOrEqual(run.ActiveBullets,s.BulletCapacity);
            }
            Assert.Greater(max,3);
        }
    }
}
