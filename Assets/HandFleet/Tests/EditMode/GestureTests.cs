using NUnit.Framework;
using UnityEngine;
namespace HandFleet.Tests
{
    public class GestureTests
    {
        HandFleetSettings settings;HandFleetMission mission;HandFleetPattern pattern;HandFleetRun run;FleetInput input;
        [SetUp]public void Setup()
        {
            settings=ScriptableObject.CreateInstance<HandFleetSettings>();settings.GestureCampaign=true;settings.FireInterval=.05f;
            mission=ScriptableObject.CreateInstance<HandFleetMission>();mission.GestureCampaign=true;
            pattern=ScriptableObject.CreateInstance<HandFleetPattern>();pattern.Family=EnemyFamily.Sentry;pattern.Health=10;
            mission.Patterns=new[]{pattern};mission.Stages=new[]{new FleetStage{Kind=StageKind.Combat,Duration=1000,HpBudget=10}};
            var hand=new FleetHand{Valid=true,Palm=new Pose(Vector3.zero,Quaternion.identity),Forward=Vector3.forward,Normal=Vector3.right};
            input=new FleetInput{Focused=true,HeadPosition=Vector3.back,Left=hand,Right=hand,DesktopMode=FleetMode.Attack};run=new HandFleetRun(settings,mission);run.Start(input);
        }
        [TearDown]public void Cleanup(){Object.DestroyImmediate(settings);Object.DestroyImmediate(mission);Object.DestroyImmediate(pattern);}
        void Tick(int count){for(int n=0;n<count;n++){input.Timestamp+=.01;run.Tick(input,.01f);}}
        void Enemy(Vector3 position){run.Enemies[0]=new FleetEnemy{Active=true,Id=99,Family=EnemyFamily.Sentry,Position=position,Previous=position,Home=position,Health=10,MaxHealth=10,Timer=100,State=EnemyState.Recovering};}
        [Test]public void AcquisitionRequiresConeAndKillsAtBaselineDps()
        {Enemy(Vector3.right*2);Tick(100);Assert.AreEqual(10,run.Enemies[0].Health);Enemy(Vector3.forward*2);Tick(60);Assert.IsFalse(run.Enemies[0].Active);Assert.AreEqual(0,run.Energy);Assert.IsTrue(run.Pickups[0].Active);}
        [Test]public void ShieldBreaksWithoutHullDamageAndLocksOut()
        {input.DesktopMode=FleetMode.Defense;Tick(330);Assert.Greater(run.ShieldCooldown,3);Assert.IsFalse(run.ShieldActive);Assert.AreEqual(3,run.Shields);}
        [Test]public void DefenseAbsorbsBeforeCoreCollision()
        {input.DesktopMode=FleetMode.Defense;Tick(20);run.Emit(true,88,Vector3.forward*.2f,Vector3.back*2,.01f,1);Tick(20);Assert.AreEqual(3,run.Shields);Assert.Greater(run.Energy,0);}
        [Test]public void DeathDoesNotEraseEnemyProjectiles()
        {Enemy(Vector3.forward*2);run.Emit(true,99,Vector3.up,Vector3.zero,.01f,1);run.DamageEnemy(0,100,false);Assert.IsTrue(run.Bullets[0].Active);}
        [Test]public void WallContactIsFatalAndTrackingLossIsNot()
        {input.Right.Valid=false;Tick(5);Assert.AreEqual(RunState.TrackingHold,run.State);Assert.AreEqual(3,run.Shields);input.Right.Valid=true;Tick(400);run.Motor.Position=Vector3.right;run.Motor.Rebase(input,run.Calibration,run.Fusion);Tick(1);Assert.AreEqual(RunState.Results,run.State);Assert.AreEqual(0,run.Shields);}
        [Test]public void GestureDwellAndMirroring()
        {var g=new FleetGesture();input.DesktopMode=FleetMode.Neutral;input.Right.Normal=Vector3.forward;g.Step(input,1,.1f);Assert.AreEqual(FleetMode.Neutral,g.Mode);g.Step(input,1,.11f);Assert.AreEqual(FleetMode.Ultimate,g.Mode);input.Left.Normal=Vector3.back;g.Reset();g.Step(input,0,.13f);Assert.AreEqual(FleetMode.Defense,g.Mode);}
        [Test]public void UltimateNeedsMoreThanEightyAndClearsOnlyIntersectingBullets()
        {
            input.DesktopMode=FleetMode.Ultimate;
            run.Pickups[0]=new FleetPickup{Active=true,Position=Vector3.zero,Life=8,Energy=80};Tick(30);
            Assert.AreEqual(80,run.Energy);Assert.AreEqual(0,run.LaserRemaining);
            run.Pickups[0]=new FleetPickup{Active=true,Position=Vector3.zero,Life=8,Energy=10};Tick(1);
            run.Emit(true,2,Vector3.forward,Vector3.zero,.01f,1);run.Emit(true,3,Vector3.right,Vector3.zero,.01f,1);Tick(2);
            Assert.Greater(run.LaserRemaining,0);Assert.IsFalse(run.Bullets[0].Active);Assert.IsTrue(run.Bullets[1].Active);Assert.Less(run.Energy,90);
            float energy=run.Energy;input.Right.Valid=false;Tick(100);Assert.AreEqual(energy,run.Energy);Assert.AreEqual(0,run.LaserRemaining);
        }
        [Test]public void BudgetDefersGroupsWithoutSkipping()
        {
            input.DesktopMode=FleetMode.Defense;mission.Stages[0].HpBudget=.5f;
            mission.Stages[0].Spawns=new[]{new FleetSpawn{Family=EnemyFamily.Sentry,Position=Vector3.forward*2},new FleetSpawn{Family=EnemyFamily.Sentry,Position=Vector3.right*2,Group=1}};
            Tick(100);Assert.AreEqual(1,run.EncounterCursor);Assert.AreEqual(.5f,run.ActiveCost);
            run.DamageEnemy(0,100,false);Tick(1);Assert.AreEqual(2,run.EncounterCursor);Assert.AreEqual(1,run.ActiveEnemies);
        }
    }
}
