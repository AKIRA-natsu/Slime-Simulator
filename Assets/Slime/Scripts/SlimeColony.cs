using System.Collections.Generic;
using UnityEngine;
namespace SlimeLab
{
    // 群体只交换导航目标；不同个体之间没有弹簧、质心约束或位置拉扯。
    public sealed class SlimeColony
    {
        public sealed class Follower
        {
            public SlimeSimulation simulation;
            public SlimeMotor motor;
            public int cursor;
            public Vector3 parking;
            public float age;
        }
        readonly SlimeSimulation leader;
        readonly SlimeMotor leaderMotor;
        readonly List<Follower> followers=new List<Follower>();
        readonly List<Vector3> trail=new List<Vector3>();
        public IReadOnlyList<Follower> Followers=>followers;
        public int TotalParticles {get{int n=leader.ParticleCount;foreach(var f in followers)n+=f.simulation.ParticleCount;return n;}}
        public bool Recalling {get;private set;}
        public int MergeCount {get;private set;}
        public int FractureCount {get;private set;}
        float idle,fractureClock;
        public SlimeColony(SlimeSimulation simulation,SlimeMotor motor){leader=simulation;leaderMotor=motor;}
        public void Reset(){followers.Clear();trail.Clear();Recalling=false;idle=fractureClock=0;MergeCount=FractureCount=0;}
        void Add(SlimeSimulation s,float yaw)
        {
            var m=new SlimeMotor(s);m.Reset(yaw);
            int cursor=0;float nearest=float.MaxValue;
            for(int i=0;i<trail.Count;i++){float d=(trail[i]-s.Center).sqrMagnitude;if(d<nearest){nearest=d;cursor=i;}}
            followers.Add(new Follower{simulation=s,motor=m,cursor=cursor,parking=s.Center});
        }
        public bool Split()
        {
            // 不设个体数量或冷却限制。继续细分当前最大的可分个体，直到离散粒子的物理分辨率。
            SlimeSimulation source=leader;SlimeMotor control=leaderMotor;
            foreach(var f in followers)if(!f.simulation.IsGrabbed&&f.simulation.ParticleCount>source.ParticleCount){source=f.simulation;control=f.motor;}
            if(source.IsGrabbed||source.ParticleCount<2)return false;
            if(trail.Count==0)trail.Add(leader.Center);
            Vector3 side=Vector3.Cross(Vector3.up,control.Forward)*(followers.Count%2==0?1:-1);
            var child=source.ExtractSide(side,source.ParticleCount/2);
            child.Impulse(side*1.8f);source.Impulse(-side*.7f);Add(child,control.FacingYaw);
            Recalling=false;idle=0;return true;
        }
        public void Recall(){Recalling=true;}
        void Detach(SlimeSimulation s,Vector3 direction,float yaw)
        {
            SlimeSimulation child;
            while((child=s.ExtractDisconnected(direction))!=null){Add(child,yaw);FractureCount++;}
        }
        public void Tick(SlimeInput input,float dt)
        {
            if(trail.Count==0)trail.Add(leader.Center);
            leaderMotor.Tick(input,dt);leader.Step(dt);
            fractureClock+=dt;
            if(fractureClock>=.1f)
            {
                fractureClock=0;int previousCount=followers.Count;
                Detach(leader,SlimeMotor.Direction(input.move,input.cameraYaw),leaderMotor.FacingYaw);
                for(int i=0;i<previousCount;i++)Detach(followers[i].simulation,followers[i].motor.Forward,followers[i].motor.FacingYaw);
            }
            if(followers.Count==0)return;
            bool moving=input.move.sqrMagnitude>.01f;idle=moving?0:idle+dt;
            if(idle>1.6f)Recalling=true;
            Vector3 center=leader.Center;
            if((center-trail[trail.Count-1]).sqrMagnitude>.20f*.20f)trail.Add(center);
            if(trail.Count>1024){trail.RemoveAt(0);foreach(var f in followers)f.cursor=Mathf.Max(0,f.cursor-1);}
            for(int k=followers.Count-1;k>=0;k--)
            {
                var f=followers[k];var s=f.simulation;f.age+=dt;int end=trail.Count-1;
                if(!Recalling&&moving)
                {
                    // 队伍按有限圆环展开，数量增加不会把队尾无限拉远。
                    float length=0,lag=.65f+(k%3)*.22f;
                    while(end>0&&length<lag){length+=Vector3.Distance(trail[end],trail[end-1]);end--;}
                }
                if(PathClear(s,s.Center,trail[end]))f.cursor=end;
                while(f.cursor<end&&Vector3.Distance(s.Center,trail[f.cursor])<.6f)f.cursor++;
                Vector3 target=trail[Mathf.Min(f.cursor,end)];
                if(!Recalling&&trail.Count<5)target=moving?center-leaderMotor.Forward*(1.3f+(k%3)*.3f):f.parking;
                if(Recalling&&PathClear(s,s.Center,center))target=center;
                if(!Recalling&&moving)
                {
                    Vector3 flank=target+Vector3.Cross(Vector3.up,leaderMotor.Forward)*((k%2==0?-1:1)*(1.35f+(k/2%3)*.6f));
                    if(PathClear(s,target,flank)&&PathClear(s,s.Center,flank))target=flank;
                }
                Vector3 delta=target-s.Center;delta.y=0;float distance=delta.magnitude;
                var command=new SlimeInput{cameraYaw=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg,move=distance>.10f?Vector2.up*Mathf.Clamp01(distance/.45f):Vector2.zero,sprint=distance>2.5f||(Recalling&&distance>.55f),jump=f.motor.Grounded&&target.y-s.Center.y>.7f&&distance<1.3f};
                f.motor.Tick(command,dt);s.Step(dt);
                if((Recalling||f.age>3)&&f.age>.8f&&Touches(leader,s))
                {leader.Absorb(s);followers.RemoveAt(k);MergeCount++;}
            }
        }
        static bool PathClear(SlimeSimulation s,Vector3 from,Vector3 to)
        {foreach(var box in s.Solids)if((box.Sweep(from,to,.12f)-to).sqrMagnitude>.00001f)return false;return true;}
        static bool Touches(SlimeSimulation a,SlimeSimulation b)
        {
            Bounds region=a.Bounds;region.Expand(.65f);if(!region.Intersects(b.Bounds))return false;
            for(int i=0;i<a.ParticleCount;i++)for(int j=0;j<b.ParticleCount;j++)if((a.Position[i]-b.Position[j]).sqrMagnitude<.30f*.30f&&(a.Collide(a.Position[i],b.Position[j])-b.Position[j]).sqrMagnitude<.00001f)return true;
            return false;
        }
    }
}
