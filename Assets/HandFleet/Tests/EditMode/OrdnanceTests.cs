using NUnit.Framework;
using UnityEngine;

namespace HandFleet.Tests
{
    public sealed class OrdnanceTests
    {
        [Test]
        public void BellRingHasAnOpenCenterAndAnOpenWedge()
        {
            var ring=new FleetBullet{Style=FleetShot.Ring,Velocity=Vector3.forward,Age=1};
            float radius=FleetOrdnance.RingRadius(ring.Age);
            bool Cross(Vector3 p)=>FleetOrdnance.Touches(ring,p+Vector3.back*.1f,p+Vector3.forward*.1f,.01f);
            Assert.IsFalse(Cross(Vector3.zero));
            Assert.IsFalse(Cross(Vector3.right*radius));
            Assert.IsTrue(Cross(Vector3.left*radius));
        }
        [Test]
        public void TumblerTileOnlyHitsInsideItsMarkedSquareAfterTheWarning()
        {
            var tile=new FleetBullet{Style=FleetShot.Tile};
            bool Cross(float x)=>FleetOrdnance.Touches(tile,new Vector3(x,.02f,-.15f),new Vector3(x,.02f,.15f),.01f);
            Assert.IsFalse(Cross(0));
            tile.Age=FleetOrdnance.TileDelay;
            Assert.IsTrue(Cross(.045f));
            Assert.IsFalse(Cross(.075f));
        }
    }
}
