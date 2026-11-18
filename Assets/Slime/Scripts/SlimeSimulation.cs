using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeLab
{
    [Serializable] public struct SolidBox
    {
        public Vector3 center, half;
        public bool climbable;
        public string name;
        public SolidBox(string name, Vector3 center, Vector3 half, bool climbable = false)
        { this.name = name; this.center = center; this.half = half; this.climbable = climbable; }
        public float Distance(Vector3 p)
        {
            Vector3 q = Abs(p - center) - half;
            return Vector3.Max(q, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y,q.z)),0);
        }
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z));
        public Vector3 Project(Vector3 p, float radius)
        {
            Vector3 q = p-center, h=half+Vector3.one*radius;
            if(Mathf.Abs(q.x)>=h.x||Mathf.Abs(q.y)>=h.y||Mathf.Abs(q.z)>=h.z)return p;
            Vector3 d=h-Abs(q);
            if(d.x<d.y&&d.x<d.z)q.x=(q.x<0?-1:1)*(h.x+.0001f);
            else if(d.y<d.z)q.y=(q.y<0?-1:1)*(h.y+.0001f);
            else q.z=(q.z<0?-1:1)*(h.z+.0001f);
            return center+q;
        }
        // 对膨胀包围盒进行连续扫掠，防止高速运动和约束修正跨过薄墙。
        public Vector3 Sweep(Vector3 from, Vector3 to, float radius)
        {
            Vector3 lo=center-half-Vector3.one*radius,hi=center+half+Vector3.one*radius;
            if((from.x<lo.x&&to.x<lo.x)||(from.x>hi.x&&to.x>hi.x)||(from.y<lo.y&&to.y<lo.y)||(from.y>hi.y&&to.y>hi.y)||(from.z<lo.z&&to.z<lo.z)||(from.z>hi.z&&to.z>hi.z))return to;
            Vector3 delta=to-from;float enter=0,exit=1;Vector3 normal=Vector3.zero;
            for(int axis=0;axis<3;axis++)
            {
                if(Mathf.Abs(delta[axis])<1e-9f){if(from[axis]<lo[axis]||from[axis]>hi[axis])return Project(to,radius);continue;}
                float a=(lo[axis]-from[axis])/delta[axis],b=(hi[axis]-from[axis])/delta[axis];
                float sign=-Mathf.Sign(delta[axis]);if(a>b){float tmp=a;a=b;b=tmp;}
                if(a>enter||(a==enter&&normal==Vector3.zero)){enter=a;normal=Vector3.zero;normal[axis]=sign;}
                exit=Mathf.Min(exit,b);if(enter>exit)return Project(to,radius);
            }
            if(normal==Vector3.zero||enter<0||enter>1)return Project(to,radius);
            Vector3 hit=from+delta*Mathf.Max(0,enter-.0001f);
            Vector3 remainder=delta*(1-enter);remainder-=normal*Mathf.Min(0,Vector3.Dot(remainder,normal));
            return Project(hit+remainder,radius);
        }
    }

    public sealed class SlimeSimulation
    {
        public const int Count=512;
        public int ParticleCount {get;private set;}=Count;
        public const float Radius=.075f, StepTime=1f/90f;
        public readonly Vector3[] Position=new Vector3[Count],Velocity=new Vector3[Count];
        public readonly List<SolidBox> Solids=new List<SolidBox>();
        readonly List<SolidBox> nearbySolids=new List<SolidBox>();
        readonly Vector3[] old=new Vector3[Count],correction=new Vector3[Count],gradient=new Vector3[Count];
        readonly Vector3[] localCenter=new Vector3[Count];
        readonly int[] localCount=new int[Count];
        readonly int[] component=new int[Count];
        readonly bool[] separatedBySolid=new bool[Count];
        readonly float[] density=new float[Count],lambda=new float[Count],gradientSq=new float[Count];
        struct Pair { public int i,j; public Pair(int a,int b){i=a;j=b;} }
        readonly List<Pair> pairs=new List<Pair>(12000);
        readonly int[] cells=new int[32*32*24],next=new int[Count];
        readonly List<int> grabbed=new List<int>(); readonly List<Vector3> grabOffsets=new List<Vector3>();
        readonly bool[] floorAdhesion=new bool[Count];
        readonly Vector3[] floorAnchor=new Vector3[Count];
        struct Tendon {public int a,b;public float length;}
        readonly List<Tendon> tendons=new List<Tendon>();
        public float Cohesion=100,Viscosity=.18f,Gravity=9.81f,BodyCohesion=35,FootCohesion=.65f;
        public bool Sleeping {get;private set;} public int Revision {get;private set;}
        public bool IsGrabbed=>grabbed.Count>0;
        public Vector3 GrabTarget,Drive,SqueezeDirection;
        public bool Climb;
        public Vector3? Goal;
        public float SleepSpeed=.08f;
        float quietTime;
        int gridX,gridZ;
        static readonly Vector3[] seeds=CreateSeeds();
        Vector3[] settledPose;Vector4 settledForces;Vector3 settledSettings;
        static Vector3[] CreateSeeds()
        {
            var candidates=new List<Vector3>();
            for(int y=-2;y<=8;y++)for(int z=-8;z<=8;z++)for(int x=-8;x<=8;x++)candidates.Add(new Vector3((x+(y&1)*.5f)*.245f,y*.212f,(z+((x+y)&1)*.5f)*.245f));
            candidates.Sort((a,b)=>a.sqrMagnitude.CompareTo(b.sqrMagnitude));return candidates.GetRange(0,Count).ToArray();
        }
        bool ClearPose(Vector3[] points,Vector3 offset)
        {
            foreach(var point in points)foreach(var box in Solids)if(box.Distance(point+offset)<Radius+.2f)return false;
            return true;
        }
        public Vector3 Center {get{Vector3 c=Vector3.zero;for(int i=0;i<ParticleCount;i++)c+=Position[i];return c/ParticleCount;}}
        public float RmsSpeed {get{float e=0;for(int i=0;i<ParticleCount;i++)e+=Velocity[i].sqrMagnitude;return Mathf.Sqrt(e/ParticleCount);}}
        public Bounds Bounds {get{Bounds b=new Bounds(Position[0],Vector3.zero);for(int i=1;i<ParticleCount;i++)b.Encapsulate(Position[i]);return b;}}
        public void Reset(Vector3 center,bool settle=true)
        {
            ParticleCount=Count;var forces=new Vector4(Cohesion,Gravity,BodyCohesion,FootCohesion);var settings=new Vector3(Viscosity,SleepSpeed,center.y);Vector3 offset=new Vector3(center.x,0,center.z);
            bool reuse=settle&&settledPose!=null&&forces.Equals(settledForces)&&settings.Equals(settledSettings)&&ClearPose(settledPose,offset);
            float lowest=float.MaxValue;for(int i=0;i<ParticleCount;i++)lowest=Mathf.Min(lowest,seeds[i].y+center.y);
            center.y+=Mathf.Max(0,Radius-lowest);
            for(int i=0;i<ParticleCount;i++){Position[i]=center+seeds[i];Velocity[i]=Vector3.zero;}
            Array.Clear(floorAdhesion,0,Count);
            grabbed.Clear();tendons.Clear();Goal=null;Drive=Vector3.zero;SqueezeDirection=Vector3.zero;Climb=false;Sleeping=false;quietTime=0;Revision++;
            // 缓存的是已实际求解的自由地面状态；参数改变或附近有实体时重新求解，不能把缓存放进墙里。
            if(reuse){for(int i=0;i<ParticleCount;i++)Position[i]=settledPose[i]+offset;Sleeping=true;quietTime=1;return;}
            if(settle)for(int i=0;i<960&&!Sleeping;i++)Step(StepTime);
            if(settle&&Sleeping&&ClearPose(Position,Vector3.zero))
            {
                if(settledPose==null)settledPose=new Vector3[Count];for(int i=0;i<ParticleCount;i++)settledPose[i]=Position[i]-offset;
                settledForces=forces;settledSettings=settings;
            }
        }
        public void Wake(){Sleeping=false;quietTime=0;}
        // 分裂只重新分配已有粒子，不复制质量、不瞬移、不保留跨个体的弹簧。
        public SlimeSimulation ExtractSide(Vector3 direction,int amount)
        {
            if(IsGrabbed||amount<1||ParticleCount-amount<1)throw new InvalidOperationException("Invalid split");
            var order=new int[ParticleCount];for(int i=0;i<order.Length;i++)order[i]=i;
            Array.Sort(order,(a,b)=>Vector3.Dot(Position[b]-Position[a],direction).CompareTo(0));
            var take=new bool[ParticleCount];for(int i=0;i<amount;i++)take[order[i]]=true;
            return Extract(take,amount);
        }
        SlimeSimulation Extract(bool[] take,int amount)
        {
            var child=new SlimeSimulation{ParticleCount=amount,Cohesion=Cohesion,Viscosity=Viscosity,Gravity=Gravity,BodyCohesion=BodyCohesion,FootCohesion=FootCohesion};
            child.Solids.AddRange(Solids);int a=0,b=0;
            for(int i=0;i<ParticleCount;i++)if(take[i]){child.Position[a]=Position[i];child.Velocity[a++]=Velocity[i];}else{Position[b]=Position[i];Velocity[b++]=Velocity[i];}
            ParticleCount=b;EndGrab();child.Wake();Revision++;child.Revision++;return child;
        }
        // 连通性使用当前粒子位置与实体阻隔；断开的岛立即成为独立求解器。
        public SlimeSimulation ExtractDisconnected(Vector3 travel)
        {
            if(IsGrabbed||Sleeping||ParticleCount<2)return null;
            for(int i=0;i<ParticleCount;i++){component[i]=i;localCount[i]=0;localCenter[i]=Vector3.zero;}
            for(int i=0;i<ParticleCount;i++)for(int j=i+1;j<ParticleCount;j++)
            {
                if((Position[i]-Position[j]).sqrMagnitude>.48f*.48f)continue;
                bool clear=true;
                foreach(var box in nearbySolids)if((box.Sweep(Position[i],Position[j],Radius*.35f)-Position[j]).sqrMagnitude>.000001f){clear=false;break;}
                if(clear){int a=Component(i),b=Component(j);if(a!=b)component[b]=a;}
            }
            for(int i=0;i<ParticleCount;i++){int c=Component(i);localCount[c]++;localCenter[c]+=Position[i];}
            int keep=0;for(int i=0;i<ParticleCount;i++)if(localCount[i]>localCount[keep])keep=i;
            if(localCount[keep]==ParticleCount)return null;
            int largest=localCount[keep];
            // 同等大小的断团优先保留前进侧，玩家不会被留下的后半团拖回去。
            if(travel.sqrMagnitude>.01f)for(int i=0;i<ParticleCount;i++)if(localCount[i]>=Mathf.Max(2,largest/2)&&Vector3.Dot(localCenter[i]/localCount[i]-localCenter[keep]/localCount[keep],travel)>.2f)keep=i;
            int detach=-1;for(int i=0;i<ParticleCount;i++)if(i!=keep&&localCount[i]>0&&(detach<0||localCount[i]>localCount[detach]))detach=i;
            if(detach<0)return null;
            var take=new bool[ParticleCount];for(int i=0;i<ParticleCount;i++)take[i]=Component(i)==detach;
            return Extract(take,localCount[detach]);
        }
        public void Absorb(SlimeSimulation child)
        {
            if(child==this||ParticleCount+child.ParticleCount>Count)throw new InvalidOperationException("Invalid merge");
            for(int i=0;i<child.ParticleCount;i++){Position[ParticleCount+i]=child.Position[i];Velocity[ParticleCount+i]=child.Velocity[i];}
            ParticleCount+=child.ParticleCount;child.ParticleCount=0;Revision++;Wake();
        }
        public void BeginGrab(Vector3 point)
        {
            grabbed.Clear();grabOffsets.Clear();int nearest=0;float best=float.MaxValue;
            for(int i=0;i<ParticleCount;i++){float d=(Position[i]-point).sqrMagnitude;if(d<best){nearest=i;best=d;}}
            Vector3 anchor=Position[nearest];
            for(int i=0;i<ParticleCount;i++){floorAdhesion[i]=Position[i].y<Radius+.03f;floorAnchor[i]=Position[i];}
            for(int i=0;i<ParticleCount;i++)if((Position[i]-anchor).sqrMagnitude<.38f*.38f){grabbed.Add(i);grabOffsets.Add(Position[i]-anchor);floorAdhesion[i]=false;}
            // 只在抓取期间保留短时黏弹连接，释放即溶解，避免液体记住变形后的褶皱。
            tendons.Clear();for(int i=0;i<ParticleCount;i++)for(int j=i+1;j<ParticleCount;j++){float d=(Position[i]-Position[j]).magnitude;if(d<.37f)tendons.Add(new Tendon{a=i,b=j,length=d});}
            GrabTarget=anchor;Wake();
        }
        public void EndGrab(){grabbed.Clear();grabOffsets.Clear();tendons.Clear();Wake();}
        public void Impulse(Vector3 v){for(int i=0;i<ParticleCount;i++)Velocity[i]+=v;Wake();}
        public void Poke(Vector3 point,Vector3 impulse,float radius=.6f)
        {
            for(int i=0;i<ParticleCount;i++){float w=Mathf.Max(0,1-(Position[i]-point).magnitude/radius);Velocity[i]+=impulse*w;}
            Wake();
        }
        public float MinClearance()
        {
            float min=float.MaxValue;for(int i=0;i<ParticleCount;i++){min=Mathf.Min(min,Position[i].y-Radius);foreach(var b in Solids)min=Mathf.Min(min,b.Distance(Position[i])-Radius);}return min;
        }
        public Vector3 Collide(Vector3 from,Vector3 to)=>Collide(from,to,Solids);
        Vector3 Collide(Vector3 from,Vector3 to,List<SolidBox> solids)
        {
            to.y=Mathf.Max(Radius,to.y);
            for(int pass=0;pass<2;pass++)foreach(var box in solids)to=box.Sweep(from,to,Radius);
            to.y=Mathf.Max(Radius,to.y);to.x=Mathf.Clamp(to.x,-49,49);to.z=Mathf.Clamp(to.z,-31,31);to.y=Mathf.Min(to.y,13);
            return to;
        }
        public void Step(float dt)
        {
            bool active=IsGrabbed||Goal.HasValue||Drive.sqrMagnitude>.0001f||Climb;
            if(Sleeping&&!active)return;if(active)Wake();
            // 整团扫掠范围先剔除远处建筑；局部仍逐粒子连续碰撞，包括抓取约束的最大位移。
            Bounds region=Bounds;float maxSpeed=0;foreach(var v in Velocity)maxSpeed=Mathf.Max(maxSpeed,v.magnitude);
            float forceTravel=(Cohesion*(ParticleCount-1)*4/27f+BodyCohesion*3+Gravity+Drive.magnitude*1.2f+85*region.size.magnitude)*dt*dt;
            region.Expand(2*(Mathf.Max(1.2f,forceTravel+.1f)+maxSpeed*dt));nearbySolids.Clear();
            foreach(var box in Solids)if(IsGrabbed||region.Intersects(new Bounds(box.center,box.half*2)))nearbySolids.Add(box);
            float fluidity=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.10f,1.2f,RmsSpeed));
            float viscosity=Mathf.Lerp(Mathf.Max(.58f,Viscosity),Viscosity,fluidity);
            const float H=.48f,Rest=4.8f;float h2=H*H;
            gridX=Mathf.FloorToInt(Center.x/H)-16;gridZ=Mathf.FloorToInt(Center.z/H)-12;
            pairs.Clear();Array.Fill(cells,-1);
            for(int i=0;i<ParticleCount;i++)
            {
                Vector3 p=Position[i];int cx=Mathf.FloorToInt(p.x/H),cy=Mathf.FloorToInt(p.y/H),cz=Mathf.FloorToInt(p.z/H);
                int key=Cell(cx,cy,cz);
                for(int z=-1;z<=1;z++)for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
                    for(int j=cells[key+x+y*32+z*1024];j>=0;j=next[j])if((p-Position[j]).sqrMagnitude<h2)pairs.Add(new Pair(j,i));
                next[i]=cells[key];cells[key]=i;
            }
            // 黏聚力作为成对的真实加速度积分；不使用负压的位置拉扯。
            for(int i=0;i<ParticleCount;i++){component[i]=i;localCenter[i]=Vector3.zero;localCount[i]=0;}
            foreach(var pair in pairs)
            {
                int i=pair.i,j=pair.j;float dx=Position[j].x-Position[i].x,dy=Position[j].y-Position[i].y,dz=Position[j].z-Position[i].z;
                int ca=Component(i),cb=Component(j);if(ca!=cb)component[cb]=ca;
                float len=Mathf.Sqrt(dx*dx+dy*dy+dz*dz);if(len<.0001f)continue;
                float q=len/H;
                float scale=Cohesion*q*(1-q)*(1-q)*dt/len;
                Accumulate(ref Velocity[i],ref Velocity[j],dx*scale,dy*scale,dz*scale);
            }
            Vector3 center=Center;
            for(int i=0;i<ParticleCount;i++){int c=Component(i);localCenter[c]+=Position[i];localCount[c]++;}
            for(int c=0;c<ParticleCount;c++)
            {
                separatedBySolid[c]=false;if(localCount[c]==0||localCount[c]==ParticleCount)continue;
                Vector3 own=localCenter[c]/localCount[c],other=(center*ParticleCount-localCenter[c])/(ParticleCount-localCount[c]);
                foreach(var box in nearbySolids)if((box.Sweep(own,other,Radius)-other).sqrMagnitude>.0025f){separatedBySolid[c]=true;break;}
            }
            Vector3 steer=Drive;
            if(Goal.HasValue){Vector3 delta=Goal.Value-center;steer=Vector3.ClampMagnitude(delta*8,18);}
            if(!Climb)steer.y=0;
            for(int i=0;i<ParticleCount;i++)
            {
                old[i]=Position[i];float moveWeight=Mathf.Lerp(.8f,1.2f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(Radius,.85f,Position[i].y)));
                Vector3 acceleration=Vector3.down*Gravity+steer*(!Climb&&!Goal.HasValue&&SqueezeDirection.sqrMagnitude<.01f?moveWeight:1);
                if(SqueezeDirection.sqrMagnitude>.01f)
                {
                    Vector3 sideways=Position[i]-center;sideways.y=0;sideways-=SqueezeDirection*Vector3.Dot(sideways,SqueezeDirection);
                    acceleration-=sideways*60;
                }
                bool atWall=false;if(Climb)foreach(var box in nearbySolids)if(box.climbable&&box.Distance(Position[i])<.85f){atWall=true;break;}
                // 主动移动把每个局部体积牵引向路径点，墙体仍始终参与连续碰撞。
                if(Goal.HasValue)
                {
                    Vector3 target=Goal.Value-Position[i];
                    acceleration.x=Mathf.Clamp(target.x*24,-38,38);
                    acceleration.z=Mathf.Clamp(target.z*34,-30,30);
                    if(Climb&&atWall)acceleration.y=Mathf.Clamp(target.y*24,-28,28);
                }
                else if(Climb&&atWall)acceleration.y=steer.y;
                // 体积内的聚合势只施加力，不保存静止网格或永久邻接连接。
                // 压力保持体积，液体可以交换邻居并在通过开口后重新聚合。
                Vector3 attraction=Vector3.ClampMagnitude(center-Position[i],3)*BodyCohesion*(SqueezeDirection.sqrMagnitude>.01f?.65f:1);
                // 障碍两侧不能通过整体质心隔墙互相牵制；受阻区域改用局部邻域聚合。
                int island=Component(i);
                if(!IsGrabbed&&separatedBySolid[island])attraction=Vector3.ClampMagnitude(localCenter[island]/localCount[island]-Position[i],3)*BodyCohesion;

                float support=Position[i].y;
                foreach(var box in nearbySolids)if(Position[i].y>=box.center.y+box.half.y&&Mathf.Abs(Position[i].x-box.center.x)<box.half.x&&Mathf.Abs(Position[i].z-box.center.z)<box.half.z) support=Mathf.Min(support,Position[i].y-box.center.y-box.half.y);
                float foot=Mathf.SmoothStep(FootCohesion,1,Mathf.InverseLerp(Radius,.55f,support));attraction.x*=foot;attraction.z*=foot;
                acceleration+=attraction;
                Velocity[i]=(Velocity[i]+acceleration*dt)*Mathf.Exp(-Mathf.Lerp(3.2f,1.25f,fluidity)*dt);
                Position[i]=Collide(old[i],old[i]+Velocity[i]*dt,nearbySolids);
            }
            for(int iteration=0;iteration<3;iteration++)
            {
                for(int i=0;i<ParticleCount;i++){density[i]=1;gradient[i]=Vector3.zero;gradientSq[i]=0;correction[i]=Vector3.zero;}
                foreach(var pair in pairs)
                {
                    int i=pair.i,j=pair.j;float dx=Position[i].x-Position[j].x,dy=Position[i].y-Position[j].y,dz=Position[i].z-Position[j].z,r2=dx*dx+dy*dy+dz*dz;if(r2>=h2)continue;
                    float q=1-r2/h2,w=q*q*q;density[i]+=w;density[j]+=w;
                    float scale=-6*q*q/(h2*Rest),gx=dx*scale,gy=dy*scale,gz=dz*scale,gs=gx*gx+gy*gy+gz*gz;
                    Accumulate(ref gradient[i],ref gradient[j],gx,gy,gz);gradientSq[i]+=gs;gradientSq[j]+=gs;
                }
                for(int i=0;i<ParticleCount;i++)lambda[i]=-Mathf.Max(0,density[i]/Rest-1)/(gradientSq[i]+gradient[i].sqrMagnitude+.2f);
                foreach(var pair in pairs)
                {
                    int i=pair.i,j=pair.j;float dx=Position[i].x-Position[j].x,dy=Position[i].y-Position[j].y,dz=Position[i].z-Position[j].z,r2=dx*dx+dy*dy+dz*dz;if(r2>=h2||r2<1e-10f)continue;
                    float q=1-r2/h2,len=Mathf.Sqrt(r2),scale=-6*q*q/(h2*Rest)*(lambda[i]+lambda[j])+Mathf.Max(0,.125f-len)*.22f/len;
                    float magnitude=Mathf.Abs(scale)*len;if(magnitude>.025f)scale*=.025f/magnitude;
                    Accumulate(ref correction[i],ref correction[j],dx*scale,dy*scale,dz*scale);
                }
                Vector3 mean=Vector3.zero;
                for(int i=0;i<ParticleCount;i++){correction[i]=Vector3.ClampMagnitude(correction[i]*.65f,.018f);mean+=correction[i]/ParticleCount;}
                for(int i=0;i<ParticleCount;i++)Position[i]+=correction[i]-mean;
                if(IsGrabbed)foreach(var t in tendons)
                {
                    Vector3 d=Position[t.b]-Position[t.a];float length=d.magnitude,limit=t.length*1.55f;
                    if(length>limit){Vector3 pull=d*(Mathf.Min(.025f,(length-limit)*.16f)/length);Position[t.a]+=pull;Position[t.b]-=pull;}
                }
                for(int k=0;k<grabbed.Count;k++)
                {
                    int i=grabbed[k];Vector3 target=GrabTarget+grabOffsets[k]*.75f;
                    Position[i]+=Vector3.ClampMagnitude(target-Position[i],.3f)*.8f;
                }
                // 黏附接触有柔度和脱附距离，允许底部滑动，超过局部剥离阈值就释放。
                if(IsGrabbed)for(int i=0;i<ParticleCount;i++)if(floorAdhesion[i])
                {
                    if(Position[i].y-Radius>.18f)floorAdhesion[i]=false;
                    else
                    {
                        Position[i].y=Mathf.Lerp(Position[i].y,Radius,.5f);
                        Position[i].x=Mathf.Lerp(Position[i].x,floorAnchor[i].x,.08f);Position[i].z=Mathf.Lerp(Position[i].z,floorAnchor[i].z,.08f);
                    }
                }
                for(int i=0;i<ParticleCount;i++)Position[i]=Collide(old[i],Position[i],nearbySolids);
            }
            for(int i=0;i<ParticleCount;i++)Velocity[i]=(Position[i]-old[i])/dt;
            // 归一化的邻域耗散不会反复放大速度；接触切向阻尼只作用于地面。
            for(int i=0;i<ParticleCount;i++){correction[i]=Vector3.zero;density[i]=1;}
            foreach(var pair in pairs)
            {
                int i=pair.i,j=pair.j;float q=Mathf.Max(0,1-(Position[i]-Position[j]).sqrMagnitude/h2),w=q*q;
                Accumulate(ref correction[i],ref correction[j],(Velocity[j].x-Velocity[i].x)*w,(Velocity[j].y-Velocity[i].y)*w,(Velocity[j].z-Velocity[i].z)*w);density[i]+=w;density[j]+=w;
            }
            for(int i=0;i<ParticleCount;i++)
            {
                Velocity[i]+=correction[i]*(viscosity/density[i]);
                if(Position[i].y<Radius+.003f){Velocity[i].x*=.97f;Velocity[i].z*=.97f;}
                Velocity[i]=Vector3.ClampMagnitude(Velocity[i],35);
            }
            if(!active&&RmsSpeed<SleepSpeed&&Bounds.min.y<Radius+.01f)quietTime+=dt;else quietTime=0;
            if(quietTime>.65f){Sleeping=true;for(int i=0;i<ParticleCount;i++)Velocity[i]=Vector3.zero;}
            Revision++;
        }
        int Cell(int x,int y,int z)=>Mathf.Clamp(x-gridX,1,30)+Mathf.Clamp(y+1,1,30)*32+Mathf.Clamp(z-gridZ,1,22)*1024;
        int Component(int i){while(component[i]!=i){component[i]=component[component[i]];i=component[i];}return i;}
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        static void Accumulate(ref Vector3 a,ref Vector3 b,float x,float y,float z){a.x+=x;a.y+=y;a.z+=z;b.x-=x;b.y-=y;b.z-=z;}
    }
}





