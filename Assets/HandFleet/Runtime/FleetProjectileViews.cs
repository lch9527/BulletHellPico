using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandFleet
{
    // Geometry is shared by all instances of a shot. The second submesh is the bright core.
    sealed class FleetProjectileViews
    {
        readonly Mesh[] shapes;
        readonly Transform[] views;
        readonly MeshFilter[] filters;
        readonly MeshRenderer[] renderers;
        readonly Material[][] palettes;
        readonly int[] lastPalette;

        public FleetProjectileViews(Transform parent,int capacity,Material rose,Material warning,Material cyan,Material core)
        {
            palettes=new[]{new[]{rose,core},new[]{warning,rose},new[]{cyan,core}};
            views=new Transform[capacity];filters=new MeshFilter[capacity];renderers=new MeshRenderer[capacity];
            lastPalette=new int[capacity];
            shapes=new Mesh[Enum.GetValues(typeof(FleetShot)).Length];
            foreach(FleetShot style in Enum.GetValues(typeof(FleetShot)))shapes[(int)style]=new Builder().Build(style);
            for(int i=0;i<capacity;i++)
            {
                var t=FleetGeometry.Root(parent,"Projectile "+i);views[i]=t;
                filters[i]=t.gameObject.AddComponent<MeshFilter>();renderers[i]=t.gameObject.AddComponent<MeshRenderer>();
                renderers[i].sharedMaterials=palettes[0];renderers[i].shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                t.gameObject.SetActive(false);
            }
        }

        public void Present(FleetBullet[] bullets)
        {
            for(int i=0;i<views.Length;i++)
            {
                var b=bullets[i];var t=views[i];
                if(t.gameObject.activeSelf!=b.Active)t.gameObject.SetActive(b.Active);
                if(!b.Active)continue;
                var style=b.Hostile?b.Style:FleetShot.Bolt;
                var size=style==FleetShot.Ring?FleetOrdnance.RingRadius(b.Age):b.Radius;
                t.localPosition=b.Position;t.localScale=Vector3.one*size;
                t.localRotation=FleetOrdnance.Facing(b.Velocity);
                int palette=!b.Hostile?2:style==FleetShot.Tile&&b.Age<FleetOrdnance.TileDelay?1:0;
                if(lastPalette[i]!=palette){renderers[i].sharedMaterials=palettes[palette];lastPalette[i]=palette;}
                var mesh=shapes[(int)style];if(filters[i].sharedMesh!=mesh)filters[i].sharedMesh=mesh;
            }
        }
        public void Dispose(){foreach(var mesh in shapes)UnityEngine.Object.Destroy(mesh);}

        sealed class Builder
        {
            readonly List<Vector3> vertices=new();
            readonly List<int>[] indices={new(),new()};
            void Quad(int a,int b,int c,int d,int sub)
            {
                var t=indices[sub];t.Add(a);t.Add(b);t.Add(c);t.Add(a);t.Add(c);t.Add(d);
            }
            // A pointed solid between two endpoints, with its widest section at 'waist'.
            void Lens(Vector3 a,Vector3 b,float width,float height,float waist,int sides,int sub)
            {
                var orient=FleetOrdnance.Facing(b-a);int start=vertices.Count;
                vertices.Add(a);vertices.Add(b);
                for(int i=0;i<sides;i++){
                    float angle=i*Mathf.PI*2/sides;
                    vertices.Add(Vector3.Lerp(a,b,waist)+orient*new Vector3(Mathf.Cos(angle)*width,Mathf.Sin(angle)*height,0));
                }
                var t=indices[sub];
                for(int i=0;i<sides;i++){
                    int p=start+2+i,q=start+2+(i+1)%sides;
                    t.Add(start);t.Add(q);t.Add(p);t.Add(start+1);t.Add(p);t.Add(q);
                }
            }
            void Axial(float back,float front,float width,float height,int sides=8,int sub=0)
            {Lens(Vector3.forward*back,Vector3.forward*front,width,height,.55f,sides,sub);}
            void Outline(bool square)
            {
                int steps=square?4:38;
                for(int i=0;i<=steps;i++)
                {
                    float angle=(square?45+i*90f:40+i*280f/steps)*Mathf.Deg2Rad;
                    var radial=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
                    for(int j=0;j<4;j++){
                        float section=j*Mathf.PI*.5f;
                        vertices.Add(radial*(1+.055f*Mathf.Cos(section))+Vector3.forward*(.055f*Mathf.Sin(section)));
                    }
                    if(i==0)continue;
                    for(int j=0;j<4;j++){int a=4*(i-1)+j,b=4*(i-1)+(j+1)%4;Quad(a,a+4,b+4,b,j==0?1:0);}
                }
            }
            public Mesh Build(FleetShot style)
            {
                switch(style)
                {
                    case FleetShot.Ring:case FleetShot.Tile: Outline(style==FleetShot.Tile);break;
                    case FleetShot.Bolt:
                        for(int s=-1;s<=1;s+=2)Lens(new Vector3(s*.85f,0,-5),Vector3.zero,.29f,.29f,.8f,6,0);
                        Axial(-1.3f,.95f,.67f,.67f,8,1);break;
                    case FleetShot.Sliver: Axial(-2.6f,1,1,.22f,4);Axial(-.2f,1.12f,.4f,.13f,4,1);break;
                    case FleetShot.Petal: Axial(-2.6f,.8f,1.35f,.46f);Axial(-1,.98f,.4f,.19f,6,1);break;
                    case FleetShot.Bar: Lens(Vector3.down*1.65f,Vector3.up*1.65f,.5f,.55f,.5f,4,0);Axial(-.45f,.65f,.45f,.85f,4,1);break;
                    case FleetShot.Seed: Axial(-1.45f,1.05f,.9f,.9f,5);Axial(.15f,1.12f,.48f,.48f,5,1);break;
                    case FleetShot.Sight: Axial(-5,.1f,.24f,.24f,4);Axial(-1.05f,1.2f,.9f,.9f,4,1);break;
                    case FleetShot.Skip:
                        for(int i=1;i<=3;i++){float z=-1.15f*i;Axial(z-.24f,z+.24f,.45f/i,.45f/i);}
                        Axial(-.95f,1,.72f,.72f,8,1);break;
                    case FleetShot.Dart: Axial(-3.6f,.7f,.7f,.7f,5);Axial(-.7f,1.05f,.45f,.45f,5,1);break;
                    case FleetShot.Bead: Axial(-.85f,.85f,.95f,.95f);Axial(.25f,.96f,.52f,.52f,8,1);break;
                    default: Axial(-2.1f,.45f,.66f,.66f);Axial(-.7f,1,.85f,.85f,10,1);break;
                }
                var mesh=new Mesh{name=style.ToString()};mesh.SetVertices(vertices);mesh.subMeshCount=2;
                mesh.SetTriangles(indices[0],0);mesh.SetTriangles(indices[1],1);mesh.RecalculateNormals();mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
