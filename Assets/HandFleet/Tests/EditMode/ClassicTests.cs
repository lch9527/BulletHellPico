using NUnit.Framework;
using UnityEngine;
namespace HandFleet.Tests
{
    public sealed class ClassicTests
    {
        HandFleetSettings settings;HandFleetMission mission;HandFleetPattern pattern;FleetInput input;
        [SetUp]public void Setup()
        {
            settings=ScriptableObject.CreateInstance<HandFleetSettings>();mission=ScriptableObject.CreateInstance<HandFleetMission>();pattern=ScriptableObject.CreateInstance<HandFleetPattern>();
            mission.Patterns=new[]{pattern};mission.Stages=new[]{new FleetStage{Kind=StageKind.Combat,Duration=100,MaxEnemies=0,AllowFusion=true}};
            var hand=new FleetHand{Valid=true,Palm=new Pose(Vector3.zero,Quaternion.identity),Forward=Vector3.forward,Normal=Vector3.left};input=new FleetInput{Focused=true,Left=hand,Right=hand};input.Left.Normal=Vector3.right;
        }
        [TearDown]public void Cleanup(){Object.DestroyImmediate(settings);Object.DestroyImmediate(mission);Object.DestroyImmediate(pattern);}
        HandFleetRun Run(){var r=new HandFleetRun(settings,mission);r.Start(input);return r;}
        void Tick(HandFleetRun r,int count=1){for(int i=0;i<count;i++){input.Timestamp+=.02;r.Tick(input,.02f);}}
        [Test]public void LaserUsesAimAndDoesNotClearUnrelatedThreatsOrRecharge()
        {
            var r=Run();r.Enemies[0]=new FleetEnemy{Active=true,Id=1,Health=10,State=EnemyState.Recovering,Position=Vector3.forward,Previous=Vector3.forward,Home=Vector3.forward,Timer=20};
            r.Enemies[1]=new FleetEnemy{Active=true,Id=2,Health=10,State=EnemyState.Recovering,Position=Vector3.right,Previous=Vector3.right,Home=Vector3.right,Timer=20};
            r.Emit(true,9,Vector3.up,Vector3.zero,.01f,1);Assert.IsTrue(r.TryLaser());Tick(r,12);
            Assert.IsFalse(r.Enemies[0].Active);Assert.IsTrue(r.Enemies[1].Active);Assert.IsTrue(r.Bullets[0].Active);Assert.AreEqual(0,r.LaserCharge);
        }
        [Test]public void LaserGestureRequiresReleaseAndFreezesWithTracking()
        {
            var r=Run();input.Right.Pinching=true;Tick(r);Assert.AreEqual(0,r.LaserRemaining);
            input.Right.Pinching=false;Tick(r);input.Right.Pinching=true;Tick(r);Assert.Greater(r.LaserRemaining,0);
            float remaining=r.LaserRemaining;input.Right.Valid=false;Tick(r,100);Assert.AreEqual(remaining,r.LaserRemaining);
            input.Right.Valid=true;input.Right.Pinching=false;Tick(r,180);Assert.AreEqual(RunState.Running,r.State);
            Assert.Less(r.LaserRemaining,remaining);r.Start(input);Assert.AreEqual(0,r.LaserRemaining);Assert.IsTrue(r.LaserReady);
        }
        [Test]public void LaserRechargeCountsExactlyTenOrdinaryKills()
        {
            var r=Run();r.TryLaser();Tick(r,110);
            for(int i=0;i<10;i++){r.Enemies[0]=new FleetEnemy{Active=true,Id=10+i,Health=1};r.DamageEnemy(0,1,false);Assert.AreEqual(i==9,r.LaserReady);}
        }
        [Test]public void AuthoredEventsWaitForCapacityAndClearCondition()
        {
            mission.Stages=new[]{new FleetStage{Kind=StageKind.Combat,Duration=.1f,MaxEnemies=1,Completion=EncounterEnd.ClearEnemies,Spawns=new[]{new FleetSpawn{At=0,Position=Vector3.forward},new FleetSpawn{At=.1f,Position=Vector3.right}}}};
            var r=Run();Tick(r,10);Assert.AreEqual(1,r.EncounterCursor);Assert.AreEqual(RunState.Running,r.State);
            r.DamageEnemy(0,100,false);Tick(r);Assert.AreEqual(2,r.EncounterCursor);Assert.AreEqual(Vector3.right,r.Enemies[0].Home);
            r.DamageEnemy(0,100,false);Tick(r);Assert.IsTrue(r.Won);Assert.AreEqual(0,r.ActiveBullets);
        }
        [Test]public void BossTransitionsRetireOnlyOwnedAttacksAndPreserveLaser()
        {
            var r=Run();r.Enemies[0]=new FleetEnemy{Active=true,Id=8,Family=EnemyFamily.Gatekeeper,Health=180,MaxHealth=180};
            r.Emit(true,8,Vector3.forward,Vector3.zero,.01f,1);r.Emit(true,9,Vector3.right,Vector3.zero,.01f,1);r.TryLaser();
            r.DamageEnemy(0,70,true);Assert.AreEqual(1,r.Enemies[0].Phase);Assert.IsFalse(r.Bullets[0].Active);Assert.IsTrue(r.Bullets[1].Active);Assert.Greater(r.LaserRemaining,0);
            r.DamageEnemy(0,60,true);Assert.AreEqual(2,r.Enemies[0].Phase);Assert.AreEqual(3,r.Shields);Assert.AreEqual(0,r.LaserCharge);
        }
        [Test]public void CompletePatternDefersWhenPoolCannotFit()
        {
            pattern.Family=EnemyFamily.Gatekeeper;settings.BulletCapacity=32;var r=Run();
            for(int i=0;i<22;i++)r.Emit(true,9,Vector3.up,Vector3.zero,.01f,1);
            r.Enemies[0]=new FleetEnemy{Active=true,Id=1,Family=EnemyFamily.Gatekeeper,Phase=2,Health=10,State=EnemyState.Attacking,Home=Vector3.forward*2,Position=Vector3.forward*2,Aim=Vector3.zero};
            Tick(r);Assert.AreEqual(1,r.DeferredPatterns);int hostile=0;foreach(var b in r.Bullets)if(b.Active&&b.Hostile)hostile++;Assert.AreEqual(22,hostile);
        }
        [Test]public void SegmentCollisionWorksBehindAndBesidePlayer()
        {
            Assert.Less(FleetSegments.DistanceSquared(Vector3.back*2,Vector3.forward,Vector3.left,Vector3.right),1e-6f);
            Assert.Greater(FleetSegments.DistanceSquared(Vector3.left,Vector3.right,Vector3.up,Vector3.up),.9f);
            var r=Run();r.Emit(true,1,Vector3.back*.05f,Vector3.forward*5,.01f,1);Tick(r);Assert.AreEqual(2,r.Shields);
        }
        [Test]public void FlightTrainingRestoresChargeBeforeMissionButCombatDoesNot()
        {
            mission.Stages=new[]{new FleetStage{Kind=StageKind.Practice,Duration=.1f,MaxEnemies=0},new FleetStage{Kind=StageKind.Combat,Duration=.1f,MaxEnemies=0},new FleetStage{Kind=StageKind.Combat,Duration=20,MaxEnemies=0}};
            var r=Run();Assert.IsTrue(r.TryLaser());Tick(r,6);Assert.AreEqual(1,r.StageIndex);Assert.IsTrue(r.LaserReady);
            Assert.IsTrue(r.TryLaser());Tick(r,6);Assert.AreEqual(2,r.StageIndex);Assert.AreEqual(0,r.LaserCharge);
        }
    }
}
