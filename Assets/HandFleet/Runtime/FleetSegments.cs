using UnityEngine;
namespace HandFleet
{
    public static class FleetSegments
    {
        // Closest points on finite segments, including degenerate (stationary) motion.
        public static float DistanceSquared(Vector3 p,Vector3 q,Vector3 a,Vector3 b)
        {
            Vector3 d=q-p,e=b-a,r=p-a;float dd=Vector3.Dot(d,d),ee=Vector3.Dot(e,e),de=Vector3.Dot(d,e),dr=Vector3.Dot(d,r),er=Vector3.Dot(e,r);
            float s=0,t=0;
            if(dd<1e-8f&&ee<1e-8f)return r.sqrMagnitude;
            if(dd<1e-8f)t=Mathf.Clamp01(er/ee);
            else if(ee<1e-8f)s=Mathf.Clamp01(-dr/dd);
            else
            {
                float denom=dd*ee-de*de;s=denom>1e-8f?Mathf.Clamp01((de*er-dr*ee)/denom):0;
                t=(de*s+er)/ee;
                if(t<0){t=0;s=Mathf.Clamp01(-dr/dd);}else if(t>1){t=1;s=Mathf.Clamp01((de-dr)/dd);}
            }
            return (r+d*s-e*t).sqrMagnitude;
        }
    }
}
