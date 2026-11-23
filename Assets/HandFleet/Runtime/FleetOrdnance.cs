using UnityEngine;

namespace HandFleet
{
    public enum FleetShot { Pulse, Bolt, Dart, Bead, Sliver, Seed, Sight, Bar, Skip, Petal, Tile, Ring }

    public static class FleetOrdnance
    {
        public static FleetShot Style(FleetEnemy e) => e.Family switch
        {
            EnemyFamily.Needle => FleetShot.Dart,
            EnemyFamily.Spindle => FleetShot.Bead,
            EnemyFamily.Gatekeeper => e.Phase==0 ? FleetShot.Dart : e.Phase==1 ? FleetShot.Bead : FleetShot.Sliver,
            EnemyFamily.Grenadier => FleetShot.Seed,
            EnemyFamily.Sniper => FleetShot.Sight,
            EnemyFamily.Battleship => FleetShot.Bar,
            EnemyFamily.Skipper => FleetShot.Skip,
            EnemyFamily.Shutter => FleetShot.Petal,
            EnemyFamily.Bell => FleetShot.Ring,
            _ => FleetShot.Pulse
        };

        public const float TileDelay=.72f, TileDuration=.14f, TileRadius=.075f;
        public static float RingRadius(float age) => Mathf.Min(.48f,.047f+.16f*age);
        public static Quaternion Facing(Vector3 direction) => direction.sqrMagnitude>.000001f ? Quaternion.LookRotation(direction) : Quaternion.identity;

        // Test the same plane, outline and opening used to draw the expanding ring.
        public static bool Touches(FleetBullet b,Vector3 previous,Vector3 current,float core)
        {
            if(b.Style==FleetShot.Tile)
            {
                if(b.Age<TileDelay)return false;
                float side=(TileRadius*.707107f+core)*2;
                var tile=new Bounds(b.Position,new Vector3(side,side,2*(.005f+core)));
                if(tile.Contains(previous)||tile.Contains(current))return true;
                var step=current-previous;
                return step.sqrMagnitude>.0000001f && tile.IntersectRay(new Ray(previous,step.normalized),out float distance) && distance<=step.magnitude;
            }
            if(b.Style!=FleetShot.Ring)return FleetCollision.Sweep(b.Previous,b.Position,b.Radius,previous,current,core);
            var inv=Quaternion.Inverse(Facing(b.Velocity));
            var a=inv*(previous-b.Previous);var c=inv*(current-b.Position);
            var t=Mathf.Abs(c.z-a.z)>.00001f?Mathf.Clamp01(-a.z/(c.z-a.z)):1;
            var cross=Vector3.Lerp(a,c,t);var radius=RingRadius(b.Age);
            float angle=Mathf.Abs(Mathf.DeltaAngle(0,Mathf.Atan2(cross.y,cross.x)*Mathf.Rad2Deg));
            float thickness=core+radius*.055f;
            return Mathf.Abs(cross.z)<=thickness && Mathf.Abs(new Vector2(cross.x,cross.y).magnitude-radius)<=thickness
                && angle>=40-Mathf.Atan2(thickness,radius)*Mathf.Rad2Deg;
        }
    }
}
