using System;
using UnityEngine;
namespace HandFleet
{
    public enum StageKind { Practice, Combat, Rest, FusionPractice }
    public enum EncounterEnd { Timed, ClearEnemies }
    [Serializable] public sealed class FleetSpawn
    {
        public float At;
        public EnemyFamily Family;
        public Vector3 Position, EntryOffset=Vector3.up*.6f, PathAxis=Vector3.right;
        public float PathAmplitude, PathSpeed=.6f;
        public int Group;
    }
    [Serializable] public sealed class FleetStage
    {
        public string Title;
        public StageKind Kind;
        public float HpBudget=10;
        public float Duration=20, SpawnInterval=3;
        public int MaxEnemies=3;
        public EnemyFamily[] Families={EnemyFamily.Needle};
        public bool AllowFusion;
        public EncounterEnd Completion;
        public FleetSpawn[] Spawns=Array.Empty<FleetSpawn>();
        [TextArea] public string Instruction;
    }
    [CreateAssetMenu(menuName="HandFleet/Mission")]
    public sealed class HandFleetMission : ScriptableObject
    {
        public bool GestureCampaign;
        // Arcade: rigid 1:1 hold, one-hit core, forward gun stream, gun upgrades, endless escalating waves into a boss.
        public bool Arcade;
        public HandFleetPattern[] Patterns;
        public FleetStage[] Stages;
        public string ScoreVersion="Classic.v1";
    }
}
