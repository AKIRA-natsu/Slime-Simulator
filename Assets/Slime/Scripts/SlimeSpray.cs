using System.Collections.Generic;
using UnityEngine;
namespace SlimeLab
{
    // 次级液滴由真实速度和接触冲击触发；静置时没有定时发射器。
    public sealed class SlimeSpray:MonoBehaviour
    {
        const int Capacity=192;
        struct Drop {public Vector3 p,v;public float radius,life;}
        readonly Drop[] drops=new Drop[Capacity];
        readonly Vector3[] previousVelocity=new Vector3[SlimeSimulation.Count];
        readonly List<Vector3> vertices=new List<Vector3>(Capacity*4);
        readonly List<Vector2> uv=new List<Vector2>(Capacity*4);
        readonly List<int> triangles=new List<int>(Capacity*6);
        SlimeSimulation simulation;Camera camera;Material material;Mesh mesh;
        int cursor,tick,lastRevision=-1;float allowance;uint seed=73819;
        public int ActiveCount {get;private set;}
        public int TotalEmitted {get;private set;}
        float Random(){seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;return (seed&65535)/65535f;}
        public void Initialize(SlimeSimulation s,Camera view)
        {
            simulation=s;camera=view;material=new Material(Resources.Load<Material>("Droplet"));mesh=new Mesh{name="Impact droplets"};mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;gameObject.layer=8;
        }
        public void SetEnvironment(Texture texture)=>material.SetTexture("_Environment",texture);
        public void Disturb(Vector3 point,float strength)
        {
            int number=Mathf.Clamp(Mathf.RoundToInt(strength*2),4,20);
            for(int i=0;i<number;i++)
            {
                float angle=Random()*Mathf.PI*2;var radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                drops[cursor++%Capacity]=new Drop{p=point+Vector3.up*.16f+radial*(.05f+Random()*.14f),v=radial*(.4f+Random()*.8f)+Vector3.up*(1.4f+Random()*.7f),radius=.008f+Random()*.014f,life=1.2f};TotalEmitted++;
            }
        }
        public void Clear(){System.Array.Clear(drops,0,drops.Length);System.Array.Clear(previousVelocity,0,previousVelocity.Length);ActiveCount=0;TotalEmitted=0;cursor=0;tick=0;allowance=0;seed=73819;lastRevision=simulation.Revision;mesh.Clear();}
        public void Step(float dt)
        {
            allowance=Mathf.Min(5,allowance+dt*100);Vector3 center=simulation.Center,mean=Vector3.zero;
            for(int i=0;i<simulation.ParticleCount;i++)mean+=simulation.Velocity[i]/simulation.ParticleCount;
            if(simulation.Revision!=lastRevision)
            {
                for(int i=tick++%4;i<simulation.ParticleCount;i+=4)
                {
                    Vector3 p=simulation.Position[i],v=simulation.Velocity[i],normal=(p-center).normalized;
                    bool impact=p.y<SlimeSimulation.Radius+.035f&&previousVelocity[i].y<-.75f&&v.y-previousVelocity[i].y>.6f;
                    bool eject=p.y>.18f&&Vector3.Dot(v-mean,normal)>1.0f&&v.sqrMagnitude>1.8f;
                    if(allowance>=1&&(impact||eject))
                    {
                        int neighbours=0;for(int j=0;j<simulation.ParticleCount;j++)if(i!=j&&(p-simulation.Position[j]).sqrMagnitude<.30f*.30f)neighbours++;
                        if(impact||neighbours<12)
                        {
                            var sideways=new Vector3(Random()-.5f,0,Random()-.5f);Vector3 launch=impact?sideways*2.6f+Vector3.up*(1.1f+Random()):v*.65f+normal*.8f+Vector3.up*.5f;
                            drops[cursor++%Capacity]=new Drop{p=p+normal*.06f+Vector3.up*.025f,v=launch,radius=.009f+Random()*.019f,life=1.2f+Random()};allowance--;TotalEmitted++;
                        }
                    }
                }
                System.Array.Copy(simulation.Velocity,previousVelocity,SlimeSimulation.Count);lastRevision=simulation.Revision;
            }
            ActiveCount=0;
            for(int i=0;i<Capacity;i++)if(drops[i].life>0)
            {
                var d=drops[i];d.life-=dt;d.v+=Vector3.down*simulation.Gravity*dt;Vector3 target=d.p+d.v*dt,projected=target;
                foreach(var box in simulation.Solids)projected=box.Sweep(d.p,projected,d.radius);
                if(target.y<d.radius||(target-projected).sqrMagnitude>.000001f)d.life=0;
                d.p=projected;drops[i]=d;if(d.life>0)ActiveCount++;
            }
        }
        public void UpdateMesh()
        {
            if(ActiveCount==0&&mesh.vertexCount==0)return;vertices.Clear();uv.Clear();triangles.Clear();Vector3 right=camera.transform.right,up=camera.transform.up;
            foreach(var d in drops)if(d.life>0)
            {
                int n=vertices.Count;Vector3 x=right*d.radius,y=up*d.radius*Mathf.Min(1.65f,1+d.v.magnitude*.12f);
                vertices.Add(d.p-x-y);vertices.Add(d.p+x-y);vertices.Add(d.p+x+y);vertices.Add(d.p-x+y);
                uv.Add(new Vector2(0,0));uv.Add(new Vector2(1,0));uv.Add(new Vector2(1,1));uv.Add(new Vector2(0,1));
                triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);triangles.Add(n);triangles.Add(n+2);triangles.Add(n+3);
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
        }
        void LateUpdate()=>UpdateMesh();
        void OnDestroy(){if(mesh!=null)Destroy(mesh);if(material!=null)Destroy(material);}
    }
}

