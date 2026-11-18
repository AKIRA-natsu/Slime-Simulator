using System;
using UnityEngine;
namespace SlimeLab
{
    public static class SlimeColonyChecks
    {
        static void Add(CheckReport report,string name,bool pass,string detail)
        {var check=new CheckResult{name=name,passed=pass,detail=detail};report.checks.Add(check);report.progress?.Invoke(check);}
        public static CheckReport Run(Action<CheckResult> progress=null)
        {
            var report=new CheckReport{version="Slime v9 - Unity 6 colony",progress=progress};
            {
                var s=new SlimeSimulation();s.Reset(Vector3.up);var m=new SlimeMotor(s);m.Reset(90);var group=new SlimeColony(s,m);bool repeated=true;
                for(int i=0;i<6;i++)repeated&=group.Split();
                Add(report,"连续六次分裂，不限制两只分身",repeated&&group.Followers.Count>=6&&group.TotalParticles==512,$"bodies={group.Followers.Count+1}; mass={group.TotalParticles}");
            }
            {
                var s=new SlimeSimulation();s.Reset(Vector3.up);var child=s.ExtractSide(Vector3.right,256);
                for(int i=0;i<child.ParticleCount;i++)child.Position[i]+=Vector3.right*5;
                s.Absorb(child);s.Solids.Add(new SolidBox("Separation wall",new Vector3(2.5f,2,0),new Vector3(.15f,2,2)));
                var m=new SlimeMotor(s);m.Reset(90);var group=new SlimeColony(s,m);
                for(int i=0;i<45;i++)group.Tick(new SlimeInput{move=Vector2.up,cameraYaw=90},SlimeSimulation.StepTime);
                Add(report,"被实体切断的两团自动拥有独立控制器",group.Followers.Count>0&&group.TotalParticles==512,$"bodies={group.Followers.Count+1}; mass={group.TotalParticles}");
            }
            foreach(bool vertical in new[]{false,true})
            {
                var s=new SlimeSimulation();if(vertical){for(int z=-4;z<=4;z++)s.Solids.Add(new SolidBox("Bar",new Vector3(2,1.5f,z*.7f),new Vector3(.1f,1.5f,.1f)));}else s.Solids.AddRange(Arena.Create(Scenario.Rail));
                s.Reset(Arena.Spawn(Scenario.Rail));var motor=new SlimeMotor(s);motor.Reset(90);var group=new SlimeColony(s,motor);float clearance=1;
                for(int i=0;i<540;i++){group.Tick(new SlimeInput{move=Vector2.up,cameraYaw=90},SlimeSimulation.StepTime);clearance=Mathf.Min(clearance,s.MinClearance());}
                int crossed=0;for(int i=0;i<s.ParticleCount;i++)if(s.Position[i].x>2.2f)crossed++;
                Add(report,vertical?"竖栏杆通过，断团允许自动独立":"横栏杆通过，断团允许自动独立",crossed==s.ParticleCount&&s.Center.x>5&&group.TotalParticles==512&&clearance>-.0001f,$"crossed={crossed}; min clearance={clearance:F6}; bodies={group.Followers.Count+1}");
                if(vertical)
                {
                    bool fractured=group.FractureCount>0;group.Recall();
                    for(int i=0;i<900;i++)group.Tick(new SlimeInput{cameraYaw=90},SlimeSimulation.StepTime);
                    Add(report,"真实竖栏杆切断后自主跟随并融合",fractured&&group.Followers.Count==0&&group.TotalParticles==512,$"fractures={group.FractureCount}; bodies={group.Followers.Count+1}; mass={group.TotalParticles}");
                }
            }
            {
                var s=new SlimeSimulation();s.Reset(new Vector3(-.5f,1,0));for(int i=256;i<512;i++)s.Position[i]+=Vector3.right*5;
                s.Solids.Add(new SolidBox("Separation wall",new Vector3(2,2,0),new Vector3(.25f,2,3)));
                float start=0;for(int i=256;i<512;i++)start+=s.Position[i].x/256;
                for(int i=0;i<270;i++){s.Drive=Vector3.right*14;s.Step(SlimeSimulation.StepTime);}
                float end=0;for(int i=256;i<512;i++)end+=s.Position[i].x/256;
                Add(report,"被墙隔开的前方液体不会被后方团块拉住",end-start>2&&s.MinClearance()>-.0001f,$"free island travel={end-start:F3}; clearance={s.MinClearance():F6}");
            }
            foreach(bool rail in new[]{false,true})
            {
                var s=new SlimeSimulation();if(rail)s.Solids.AddRange(Arena.Create(Scenario.Rail));s.Reset(new Vector3(-2,1,0));var motor=new SlimeMotor(s);motor.Reset(90);var group=new SlimeColony(s,motor);
                Vector3 center=s.Center;bool split=group.Split();Vector3 after=s.Center*s.ParticleCount;foreach(var f in group.Followers)after+=f.simulation.Center*f.simulation.ParticleCount;after/=512;
                Add(report,"主动分裂保持粒子质量和瞬时质心"+(rail?"（栏杆）":""),split&&group.TotalParticles==512&&group.Followers.Count==1&&Vector3.Distance(center,after)<.0001f,$"particles={group.TotalParticles}; bodies={group.Followers.Count+1}; center shift={Vector3.Distance(center,after):F6}");
                float minClearance=1,maxDistance=0;bool mass=true;float start=s.Center.x;
                for(int i=0;i<540;i++)
                {
                    group.Tick(new SlimeInput{move=Vector2.up,cameraYaw=90},SlimeSimulation.StepTime);mass&=group.TotalParticles==512;minClearance=Mathf.Min(minClearance,s.MinClearance());
                    foreach(var f in group.Followers){minClearance=Mathf.Min(minClearance,f.simulation.MinClearance());maxDistance=Mathf.Max(maxDistance,Vector3.Distance(f.simulation.Center,s.Center));}
                }
                bool followed=true;foreach(var f in group.Followers)followed&=f.simulation.Center.x>start+3;
                Add(report,"分裂后主角移动与独立跟随"+(rail?"（穿栏杆）":""),s.Center.x>start+6&&followed&&maxDistance<6&&minClearance>-.0001f&&mass,$"leader travel={s.Center.x-start:F2}; follower max gap={maxDistance:F2}; clearance={minClearance:F6}");
                for(int i=0;i<900;i++){group.Tick(new SlimeInput{cameraYaw=90},SlimeSimulation.StepTime);mass&=group.TotalParticles==512;}
                Add(report,"停步自动集合并接触融合"+(rail?"（栏杆后）":""),group.Followers.Count==0&&s.ParticleCount==512&&group.MergeCount>=1&&mass,$"bodies={group.Followers.Count+1}; merged={group.MergeCount}; particles={s.ParticleCount}");
            }
            {
                var s=new SlimeSimulation();s.Reset(new Vector3(-6,1,-1.5f));var motor=new SlimeMotor(s);motor.Reset(90);var group=new SlimeColony(s,motor);group.Split();
                bool repeatedAccepted=group.Split()&&group.TotalParticles==512;
                for(int i=0;i<315;i++)group.Tick(new SlimeInput{move=Vector2.up,cameraYaw=i<180?90:0,jump=i==120},SlimeSimulation.StepTime);
                group.Recall();float start=s.Center.x;
                for(int i=0;i<450;i++)group.Tick(new SlimeInput{move=Vector2.up,cameraYaw=90},SlimeSimulation.StepTime);
                Add(report,"转弯后可在持续移动中召回融合，连续分裂不复制质量",repeatedAccepted&&group.TotalParticles==512&&group.Followers.Count==0&&s.Center.x>start+6,$"followers={group.Followers.Count}; particles={group.TotalParticles}; moving distance={s.Center.x-start:F2}");
            }
            {
                var s=new SlimeSimulation();s.Reset(Vector3.up);var m=new SlimeMotor(s);m.Reset(90);var group=new SlimeColony(s,m);bool ok=true;
                for(int i=0;i<31;i++)ok&=group.Split();
                Add(report,"可连续细分为三十二个个体，质量不增不减",ok&&group.Followers.Count==31&&group.TotalParticles==512,$"bodies={group.Followers.Count+1}; mass={group.TotalParticles}");
            }
            foreach(bool north in new[]{true,false})
            {
                var s=new SlimeSimulation();s.Solids.AddRange(Arena.District());s.Reset(north?new Vector3(-21,1,0):new Vector3(15,1,0));var m=new SlimeMotor(s);m.Reset(north?0:90);float clearance=1;
                for(int i=0;i<720;i++){m.Tick(new SlimeInput{move=Vector2.up,cameraYaw=north?0:90},SlimeSimulation.StepTime);s.Step(SlimeSimulation.StepTime);clearance=Mathf.Min(clearance,s.MinClearance());}
                Add(report,north?"街区纵向可跨越旧地图边界":"街区横向可跨越旧地图边界",(north?s.Center.z>10:s.Center.x>25)&&clearance>-.0001f,$"center={s.Center}; clearance={clearance:F6}");
            }
            {
                var s=new SlimeSimulation();s.Solids.AddRange(Arena.District());s.Reset(new Vector3(34,7,19),false);var m=new SlimeMotor(s);m.Reset(90);
                for(int i=0;i<300;i++){m.Tick(default,SlimeSimulation.StepTime);s.Step(SlimeSimulation.StepTime);}
                float apex=0,clearance=1;
                for(int i=0;i<210;i++){m.Tick(new SlimeInput{jump=i==0},SlimeSimulation.StepTime);s.Step(SlimeSimulation.StepTime);apex=Mathf.Max(apex,s.Bounds.max.y);clearance=Mathf.Min(clearance,s.MinClearance());}
                Add(report,"屋顶跳跃不再被旧的七米高度截断",m.JumpCount==1&&apex>7.4f&&clearance>-.0001f,$"jumps={m.JumpCount}; apex={apex:F3}; clearance={clearance:F6}");
            }
            report.passed=true;foreach(var check in report.checks)report.passed&=check.passed;return report;
        }
    }
}
