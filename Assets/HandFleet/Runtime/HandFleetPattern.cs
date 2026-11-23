using UnityEngine;
namespace HandFleet
{
    public enum EnemyFamily { Needle, Tumbler, Spindle, Lancer, Comet, Gatekeeper, Sentry, Grenadier, Sniper, Frigate, Battleship, Carrier, Follower, Skipper, Shutter, Bell }
    [CreateAssetMenu(menuName="HandFleet/Attack pattern")]
    public sealed class HandFleetPattern : ScriptableObject
    {
        public EnemyFamily Family;
        public float Health=4, Telegraph=.9f, Recovery=1.8f, Speed=.65f, Radius=.012f;
        public int KillPoints=100;
        public float FanSpacing=.23f, RingRadius=.45f;
        // Space at the player's plane, including the full fused follower error.
        public float MinimumGap(HandFleetSettings s) => 2*(Radius+s.CoreRadius+s.FusedError)+.02f;
        public float SafeFanSpacing(HandFleetSettings s) => Mathf.Max(FanSpacing,MinimumGap(s));
    }
}
