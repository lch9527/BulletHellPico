using UnityEngine;
namespace HandFleet
{
    [CreateAssetMenu(menuName="HandFleet/Art library")]
    public sealed class FleetArtAssets : ScriptableObject
    {
        [System.Serializable]
        public struct EnemyWeaponSounds
        {
            public EnemyFamily Family;
            public AudioClip Charge, Fire;
            [Tooltip("Playback pitch for the fire clip. Zero uses the original pitch.")]
            public float FirePitch;
        }

        public GameObject Kestrel, Arena;
        public GameObject[] Enemies;
        public AudioClip[] Sounds;
        public AudioClip Ambience;
        public AudioClip JetEngineLoop, FollowerEngineLoop, HeavyEngineLoop;
        public EnemyWeaponSounds[] EnemyWeapons;

        public AudioClip SoundFor(FleetCombatEvent cue, out float pitch)
        {
            pitch = 1f;
            if (cue.Family.HasValue && EnemyWeapons != null && (cue.Kind == FleetCue.EnemyShot || cue.Kind == FleetCue.Charge))
            {
                foreach (var weapon in EnemyWeapons)
                {
                    if (weapon.Family != cue.Family.Value) continue;
                    var clip = cue.Kind == FleetCue.Charge ? weapon.Charge : weapon.Fire;
                    if (clip)
                    {
                        if (cue.Kind == FleetCue.EnemyShot && weapon.FirePitch > 0)
                            pitch = Mathf.Clamp(weapon.FirePitch, .25f, 3f);
                        return clip;
                    }
                    break;
                }
            }
            int index = (int)cue.Kind;
            return Sounds != null && index < Sounds.Length ? Sounds[index] : null;
        }
    }
}
