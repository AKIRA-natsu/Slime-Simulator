using System;
using System.Collections.Generic;
using UnityEngine;
namespace SlimeLab
{
    [Serializable] public class CheckResult {public string name;public bool passed;public string detail;}
    [Serializable] public class CheckReport {public string version="Unity 6 - automatic obstacle recognition";public bool passed;public List<CheckResult> checks=new List<CheckResult>();[NonSerialized]public Action<CheckResult> progress;}
    public static class SlimeChecks
    {
        static void Add(CheckReport r,string name,bool pass,string detail){var result=new CheckResult{name=name,passed=pass,detail=detail};r.checks.Add(result);r.progress?.Invoke(result);}
        static void RunFor(SlimeSimulation s,float seconds,Action<int> input=null){int n=Mathf.RoundToInt(seconds/SlimeSimulation.StepTime);for(int i=0;i<n;i++){input?.Invoke(i);s.Step(SlimeSimulation.StepTime);}}
        static int LargestCluster(SlimeSimulation s)
        {
            var seen=new bool[SlimeSimulation.Count];var queue=new Queue<int>();int largest=0;
            for(int a=0;a<seen.Length;a++)if(!seen[a]){seen[a]=true;queue.Enqueue(a);int count=0;while(queue.Count>0){int i=queue.Dequeue();count++;for(int j=0;j<seen.Length;j++)if(!seen[j]&&(s.Position[i]-s.Position[j]).sqrMagnitude<.58f*.58f){seen[j]=true;queue.Enqueue(j);}}largest=Math.Max(largest,count);}
            return largest;
        }
        public static CheckReport Run(Action<CheckResult> progress=null)
        {
            var report=new CheckReport{progress=progress};var s=new SlimeSimulation();s.Reset(new Vector3(0,1,0));
            float initialSpeed=s.RmsSpeed;Vector3[] saved=(Vector3[])s.Position.Clone();float maxDrift=0;
            RunFor(s,30,i=>{for(int p=0;p<SlimeSimulation.Count;p++)maxDrift=Mathf.Max(maxDrift,(s.Position[p]-saved[p]).magnitude);});
            Add(report,"静置 30 秒逐点不漂移",s.Sleeping&&maxDrift<.002f,$"sleep={s.Sleeping}; initial RMS={initialSpeed:F6}; max drift={maxDrift:F6} m");
            var b=s.Bounds;float aspect=b.size.y/Mathf.Max(b.size.x,b.size.z);Add(report,"静置时保留圆拱主体",aspect>.50f&&aspect<.72f&&b.size.y>1.25f&&b.size.y<1.75f,$"height={b.size.y:F3}; width={b.size.x:F3}; aspect={aspect:F3}");
            float baseWidth=0,midWidth=0;foreach(var p in s.Position){if(p.y<.30f)baseWidth=Mathf.Max(baseWidth,Mathf.Abs(p.x-s.Center.x)*2);if(p.y>.45f&&p.y<.8f)midWidth=Mathf.Max(midWidth,Mathf.Abs(p.x-s.Center.x)*2);}
            Add(report,"底部适度外扩而非宽大裙边",baseWidth>midWidth*1.025f&&baseWidth<midWidth*1.3f,$"base={baseWidth:F3}; middle={midWidth:F3}");
            Vector3 top=new Vector3(s.Center.x,b.max.y,s.Center.z);s.BeginGrab(top);Vector3 anchor=s.GrabTarget;
            RunFor(s,1.5f,i=>s.GrabTarget=anchor+new Vector3(.45f,1.45f,0)*Mathf.Clamp01(i/(1.5f/SlimeSimulation.StepTime)));
            float liftHeight=s.Bounds.size.y;Add(report,"局部抓取可唤醒并拉伸",!s.Sleeping&&liftHeight>b.size.y*1.2f,$"rest={b.size.y:F3}; lifted={liftHeight:F3}");
            int connected=LargestCluster(s);Add(report,"提拉时底部下垂且全部粒子连通",s.Bounds.min.y<.35f&&connected==SlimeSimulation.Count,$"bottom={s.Bounds.min.y:F3}; connected={connected}/{SlimeSimulation.Count}");
            s.EndGrab();RunFor(s,14);Add(report,"释放后重新稳定",s.Sleeping&&s.RmsSpeed<.001f,$"sleep={s.Sleeping}; RMS={s.RmsSpeed:F6}");
            float recovery=Mathf.Abs(s.Bounds.size.y/s.Bounds.size.x-b.size.y/b.size.x);
            Add(report,"释放后恢复圆拱比例",recovery<.08f,$"aspect delta={recovery:F4}; bounds={s.Bounds.size}");
            {
                var side=new SlimeSimulation();side.Reset(new Vector3(0,1,0));float width=side.Bounds.size.x;side.BeginGrab(side.Center+new Vector3(.8f,.15f,0));Vector3 start=side.GrabTarget;
                RunFor(side,3,i=>side.GrabTarget=start+new Vector3(2.3f,.25f,0)*Mathf.Clamp01(i/(2.5f/SlimeSimulation.StepTime)));
                float error=100;foreach(var point in side.Position)error=Mathf.Min(error,(point-side.GrabTarget).magnitude);
                Add(report,"侧向提拉形成接地拖尾",side.Bounds.size.x>width*1.3f&&side.Bounds.min.y<.1f&&error<.5f&&LargestCluster(side)==SlimeSimulation.Count,$"width={width:F3}->{side.Bounds.size.x:F3}; bottom={side.Bounds.min.y:F3}; target error={error:F3}");
            }
            {
                var tapped=new SlimeSimulation();tapped.Reset(new Vector3(0,1,0));float height=tapped.Bounds.max.y;tapped.Poke(new Vector3(tapped.Center.x,height,tapped.Center.z),Vector3.down*7,.65f);RunFor(tapped,.18f);
                Add(report,"轻触产生局部凹陷",height-tapped.Bounds.max.y>.06f&&!tapped.Sleeping,$"height drop={height-tapped.Bounds.max.y:F3}; speed={tapped.RmsSpeed:F3}");
                RunFor(tapped,4);Add(report,"轻触结束后自行静止",tapped.Sleeping&&tapped.RmsSpeed==0,$"sleep={tapped.Sleeping}; speed={tapped.RmsSpeed:F6}");
                tapped.Drive=Vector3.right*14;RunFor(tapped,.6f);float upper=0,lower=0;int upCount=0,lowCount=0;foreach(var p in tapped.Position){if(p.y>.6f){upper+=p.x;upCount++;}if(p.y<.2f){lower+=p.x;lowCount++;}}
                float lag=upper/Math.Max(1,upCount)-lower/Math.Max(1,lowCount);Add(report,"移动时局部滞后但不拉成扁长条",lag>.04f&&lag<.4f&&tapped.Bounds.size.y/tapped.Bounds.size.x>.45f&&upCount>0&&lowCount>0,$"upper/lower lag={lag:F3} m; bounds={tapped.Bounds.size}");
            }
            foreach(float speed in new[]{8f,30f,60f})
            {
                var box=new SolidBox("thin",new Vector3(1,1,0),new Vector3(.025f,1,2));Vector3 p=new Vector3(0,1,0),to=p+Vector3.right*speed*.1f;
                Vector3 result=box.Sweep(p,to,SlimeSimulation.Radius);
                Add(report,$"薄墙连续碰撞 {speed} m/s",result.x<.901f&&box.Distance(result)>=SlimeSimulation.Radius-.001f,$"x={result.x:F6}");
            }
            {
                var box=new SolidBox("boundary",new Vector3(1,1,0),new Vector3(.025f,1,2));Vector3 start=box.center-new Vector3(box.half.x+SlimeSimulation.Radius,0,0);
                var result=box.Sweep(start,new Vector3(1.8f,1,0),SlimeSimulation.Radius);
                Add(report,"已贴住薄墙再推进不会跨面",result.x<=start.x+.001f,$"start={start.x:F6}; end={result.x:F6}");
            }
            s=new SlimeSimulation();s.Solids.AddRange(Arena.Create(Scenario.Wall));s.Reset(new Vector3(-.5f,1,0));s.Goal=new Vector3(5,1,0);
            float clearance=1;RunFor(s,7,i=>clearance=Mathf.Min(clearance,s.MinClearance()));
            float maxX=s.Bounds.max.x;Add(report,"持续顶实体墙不穿透",clearance>-.002f&&maxX<1.81f,$"clearance={clearance:F6}; max x={maxX:F4}");
            s.Goal=null;s.Impulse(Vector3.right*30);RunFor(s,2);Add(report,"整体高速撞墙不穿透",s.Bounds.max.x<1.81f&&s.MinClearance()>-.002f,$"max x={s.Bounds.max.x:F4}");
            s=new SlimeSimulation();s.Solids.AddRange(Arena.Create(Scenario.Climb));s.Reset(new Vector3(.4f,1,0));s.Climb=true;s.Goal=new Vector3(2.1f,3.9f,0);
            clearance=1;RunFor(s,10,i=>clearance=Mathf.Min(clearance,s.MinClearance()));
            Add(report,"贴墙向上爬升",s.Center.y>2.3f&&clearance>-.002f,$"center y={s.Center.y:F3}; clearance={clearance:F6}");
            float climbingHeight=s.Center.y;s.Climb=false;s.Goal=null;RunFor(s,4);Add(report,"取消攀附后受重力下落",s.Center.y<climbingHeight-.5f,$"before={climbingHeight:F3}; after={s.Center.y:F3}");
            foreach(var scene in new[]{Scenario.Door,Scenario.Window})
            {
                s=new SlimeSimulation();s.Solids.AddRange(Arena.Create(scene));s.Reset(new Vector3(-.5f,1,0));s.Goal=new Vector3(4,scene==Scenario.Window?1.25f:1,0);s.Climb=scene==Scenario.Window;
                clearance=1;int crossed=0;bool legalPath=true;var prior=(Vector3[])s.Position.Clone();int crossings=0;
                RunFor(s,16,i=>{
                    clearance=Mathf.Min(clearance,s.MinClearance());
                    for(int p=0;p<SlimeSimulation.Count;p++){
                        Vector3 now=s.Position[p];if(prior[p].x<2&&now.x>=2){crossings++;float t=(2-prior[p].x)/(now.x-prior[p].x);Vector3 at=Vector3.Lerp(prior[p],now,t);float halfGap=scene==Scenario.Door?.37f:.45f,low=scene==Scenario.Door?0:.6f,high=scene==Scenario.Door?2.4f:2.1f;legalPath&=Mathf.Abs(at.z)<=halfGap-SlimeSimulation.Radius+.002f&&at.y>=low+SlimeSimulation.Radius-.002f&&at.y<=high-SlimeSimulation.Radius+.002f;}prior[p]=now;
                    }
                });
                foreach(var p in s.Position)if(p.x>2.4f)crossed++;
                Add(report,scene==Scenario.Door?"窄门挤压通行":"高窗开口通行",crossed>=SlimeSimulation.Count*.98f&&clearance>-.002f,$"crossed={crossed}/{SlimeSimulation.Count}; clearance={clearance:F6}; center={s.Center}");
                Add(report,(scene==Scenario.Door?"门":"窗")+"通行轨迹只经过开口",legalPath&&crossings>=SlimeSimulation.Count*.98f,$"legal={legalPath}; plane crossings={crossings}");
                s.Goal=null;s.Climb=false;RunFor(s,10);connected=LargestCluster(s);Add(report,(scene==Scenario.Door?"门":"窗")+"通行后融合并稳定",connected>=SlimeSimulation.Count*.98f&&s.Sleeping,$"connected={connected}; sleep={s.Sleeping}; speed={s.RmsSpeed:F6}");
            }
            bool finite=true;foreach(var p in s.Position)finite&=float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
            Add(report,"粒子数量守恒、状态有限",finite&&s.Position.Length==SlimeSimulation.Count,$"count={s.Position.Length}");
            RunControls(report);RunResetCache(report);RunPerception(report);report.passed=report.checks.TrueForAll(c=>c.passed);return report;
        }
        static void RunPerception(CheckReport report)
        {
            var walk=new SlimeInput{move=Vector2.up,cameraYaw=90};var s=new SlimeSimulation();s.Reset(new Vector3(0,1,0));var motor=new SlimeMotor(s);motor.Reset(90);bool triggered=false;
            RunMotor(s,motor,1.5f,i=>{triggered|=motor.Climbing||motor.Squeezing;return walk;});
            Add(report,"开阔地不误触发自动攀附和挤压",!triggered&&s.Center.y<.8f,$"triggered={triggered}; height={s.Center.y:F3}");
            s=new SlimeSimulation();s.Solids.Add(new SolidBox("side surface",new Vector3(0,1.5f,1.5f),new Vector3(8,1.5f,.2f),true));s.Reset(new Vector3(-3,1,0));motor=new SlimeMotor(s);motor.Reset(90);triggered=false;
            RunMotor(s,motor,2,i=>{triggered|=motor.Climbing;return walk;});
            Add(report,"沿墙平行行走不会被吸上墙",!triggered&&s.Center.y<.8f,$"climbed={triggered}; center={s.Center}");
            s=new SlimeSimulation();foreach(float side in new[]{-1f,1f})s.Solids.Add(new SolidBox("wide passage",new Vector3(2,1.5f,side*3.7f),new Vector3(.25f,1.5f,2),true));s.Reset(new Vector3(0,1,0));motor=new SlimeMotor(s);motor.Reset(90);triggered=false;
            RunMotor(s,motor,3,i=>{triggered|=motor.Climbing||motor.Squeezing;return walk;});
            Add(report,"宽通道保持正常走动而不强行变形",!triggered&&s.Bounds.min.x>2.4f,$"triggered={triggered}; rear={s.Bounds.min.x:F3}");
            s=new SlimeSimulation();foreach(float side in new[]{-1f,1f})s.Solids.Add(new SolidBox("open topped passage",new Vector3(2,1.5f,side*2.4f),new Vector3(.25f,1.5f,2),true));s.Reset(new Vector3(.4f,1,0));var sensor=new SlimePerception(s);sensor.Tick(Vector3.right,SlimeSimulation.StepTime);
            Add(report,"没有过梁的窄通道也能识别",sensor.Opening&&!sensor.NeedsLift(s.Center.y),$"opening={sensor.Opening}; lift={sensor.NeedsLift(s.Center.y)}");
            s=new SlimeSimulation();foreach(var box in Arena.Create(Scenario.Window))s.Solids.Add(new SolidBox("unlabelled",new Vector3(box.center.z+3,box.center.y,box.center.x),new Vector3(box.half.z,box.half.y,box.half.x),box.climbable));s.Reset(new Vector3(3,1,-.5f));motor=new SlimeMotor(s);motor.Reset(0);bool climb=false,squeeze=false;float clearance=1;
            RunMotor(s,motor,6,i=>{climb|=motor.Climbing;squeeze|=motor.Squeezing;clearance=Mathf.Min(clearance,s.MinClearance());return new SlimeInput{move=Vector2.up,cameraYaw=0};});
            Add(report,"平移并转向的无名称高窗仍能识别通行",climb&&squeeze&&s.Bounds.min.z>2.4f&&clearance>-.002f,$"climb={climb}; squeeze={squeeze}; rear={s.Bounds.min.z:F3}; clearance={clearance:F5}");
            s=new SlimeSimulation();s.Solids.AddRange(Arena.Create(Scenario.Door));s.Reset(new Vector3(5,1,0));motor=new SlimeMotor(s);motor.Reset(-90);
            RunMotor(s,motor,6,i=>new SlimeInput{move=Vector2.up,cameraYaw=-90});
            Add(report,"从开口背面接近也能自动挤过",s.Bounds.max.x<1.6f&&s.MinClearance()>-.002f,$"rear={s.Bounds.max.x:F3}; clearance={s.MinClearance():F5}");
            s=new SlimeSimulation();s.Solids.AddRange(Arena.Create(Scenario.Climb));s.Reset(Arena.Spawn(Scenario.Climb));motor=new SlimeMotor(s);motor.Reset(90);RunMotor(s,motor,2,i=>walk);motor.Tick(default,SlimeSimulation.StepTime);
            Add(report,"松开输入立即取消识别动作",!motor.Climbing&&!motor.Squeezing&&!s.Climb&&s.SqueezeDirection==Vector3.zero,$"climb={motor.Climbing}; squeeze={motor.Squeezing}");
            RunMotor(s,motor,.3f,i=>walk);motor.Tick(new SlimeInput{move=Vector2.up,cameraYaw=-90},SlimeSimulation.StepTime);
            Add(report,"转向离墙立即取消攀附",!motor.Climbing&&!s.Climb,$"climb={motor.Climbing}");
            RunMotor(s,motor,.3f,i=>walk);s.BeginGrab(s.Center);motor.Tick(walk,SlimeSimulation.StepTime);
            Add(report,"抓取优先于自动环境动作",!motor.Climbing&&!motor.Squeezing&&s.Drive==Vector3.zero,$"climb={motor.Climbing}; squeeze={motor.Squeezing}; drive={s.Drive}");
            s=new SlimeSimulation();foreach(float side in new[]{-1f,1f})s.Solids.Add(new SolidBox("sealed slit",new Vector3(2,2,side*2.05f),new Vector3(.25f,2,2)));s.Reset(new Vector3(-.5f,1,0));motor=new SlimeMotor(s);motor.Reset(90);triggered=false;
            RunMotor(s,motor,3,i=>{triggered|=motor.Squeezing;return walk;});
            Add(report,"过小的缝隙不判为可通过开口",!triggered&&s.Bounds.max.x<1.68f&&s.MinClearance()>-.002f,$"squeeze={triggered}; front={s.Bounds.max.x:F3}");
        }
        static void RunResetCache(CheckReport report)
        {
            var s=new SlimeSimulation();s.Reset(new Vector3(0,1,0));var pose=(Vector3[])s.Position.Clone();
            s.Reset(new Vector3(5,1,0));float error=0;for(int i=0;i<SlimeSimulation.Count;i++)error=Mathf.Max(error,(s.Position[i]-pose[i]-Vector3.right*5).magnitude);
            Add(report,"重复重置复用稳定状态且正确平移",s.Sleeping&&error<.0001f,$"maximum pose error={error:F6}");
            float height=s.Bounds.size.y;s.Gravity=15;s.Reset(new Vector3(5,1,0));
            Add(report,"物理参数改变会使重置缓存失效",Mathf.Abs(s.Bounds.size.y-height)>.025f,$"height={height:F3}->{s.Bounds.size.y:F3}");
            s.Gravity=9.81f;s.Reset(new Vector3(0,1,0));s.Solids.Add(new SolidBox("reset obstacle",new Vector3(1.3f,1,0),new Vector3(.2f,1,2)));s.Reset(new Vector3(0,1,0));
            Add(report,"重置缓存不能覆盖新加入的实体",s.MinClearance()>-.002f&&s.Bounds.max.x<1.027f,$"clearance={s.MinClearance():F5}; max x={s.Bounds.max.x:F3}");
        }
        static void RunMotor(SlimeSimulation s,SlimeMotor motor,float seconds,Func<int,SlimeInput> input)
        {for(int i=0;i<Mathf.RoundToInt(seconds/SlimeSimulation.StepTime);i++){motor.Tick(input(i),SlimeSimulation.StepTime);s.Step(SlimeSimulation.StepTime);}}
        static void RunControls(CheckReport report)
        {
            var forward=SlimeMotor.Direction(Vector2.up,90);Add(report,"W 按相机朝向前进",(forward-Vector3.right).magnitude<.0001f,$"yaw90 forward={forward}");
            Add(report,"斜向输入不会额外加速",Mathf.Abs(SlimeMotor.Direction(Vector2.one,37).magnitude-1)<.0001f,"diagonal magnitude=1");
            var s=new SlimeSimulation();s.Reset(new Vector3(0,1,0));var motor=new SlimeMotor(s);motor.Reset(90);var walk=new SlimeInput{move=Vector2.up,cameraYaw=90};
            RunMotor(s,motor,1.5f,i=>walk);float walkDistance=s.Center.x;
            Add(report,"手动步行驱动真实液体",walkDistance>1&&Mathf.Abs(s.Center.z)<.2f&&s.MinClearance()>-.002f,$"center={s.Center}; clearance={s.MinClearance():F5}");
            float yaw=motor.FacingYaw;RunMotor(s,motor,.02f,i=>new SlimeInput{cameraYaw=-90});Add(report,"环视镜头不会旋转身体朝向",Mathf.Abs(Mathf.DeltaAngle(yaw,motor.FacingYaw))<.01f,$"before={yaw}; after={motor.FacingYaw}");
            RunMotor(s,motor,5,i=>default);Add(report,"松开移动键后收拢并静止",s.Sleeping&&s.RmsSpeed==0,$"sleep={s.Sleeping}; rms={s.RmsSpeed:F5}");
            s.Reset(new Vector3(0,1,0));motor.Reset(90);var sprint=walk;sprint.sprint=true;RunMotor(s,motor,1.5f,i=>sprint);
            Add(report,"Shift 加速产生更快位移",s.Center.x>walkDistance*1.2f,$"walk={walkDistance:F3}; sprint={s.Center.x:F3}");
            Add(report,"奔跑仍保留圆润体积与连通性",s.Bounds.size.y/Mathf.Max(s.Bounds.size.x,s.Bounds.size.z)>.45f&&LargestCluster(s)==SlimeSimulation.Count,$"bounds={s.Bounds.size}; aspect={s.Bounds.size.y/Mathf.Max(s.Bounds.size.x,s.Bounds.size.z):F3}");
            s.Reset(new Vector3(0,1,0));motor.Reset(90);float baseHeight=s.Center.y,peak=baseHeight;
            RunMotor(s,motor,.7f,i=>{peak=Mathf.Max(peak,s.Center.y);return new SlimeInput{jump=i==0||i==35,cameraYaw=90};});
            Add(report,"起跳离地且空中不能再次起跳",peak-baseHeight>.45f&&motor.JumpCount==1,$"height gain={peak-baseHeight:F3}; jumps={motor.JumpCount}");
            RunMotor(s,motor,6,i=>default);Add(report,"落地后恢复静止体态",s.Sleeping&&motor.Grounded&&LargestCluster(s)==SlimeSimulation.Count,$"sleep={s.Sleeping}; grounded={motor.Grounded}; bounds={s.Bounds.size}");
            s.Reset(new Vector3(10,1,0));motor.Reset(90);RunMotor(s,motor,1.5f,i=>walk);Add(report,"训练场远端仍可移动和聚合",s.Center.x>11&&LargestCluster(s)==SlimeSimulation.Count,$"center={s.Center}");
            s=new SlimeSimulation();s.Solids.AddRange(Arena.Create(Scenario.Wall));s.Reset(Arena.Spawn(Scenario.Wall));motor=new SlimeMotor(s);motor.Reset(90);RunMotor(s,motor,4,i=>sprint);
            Add(report,"玩家持续跑向实体墙仍被阻挡",s.Bounds.max.x<1.81f&&s.MinClearance()>-.002f,$"max x={s.Bounds.max.x:F4}; clearance={s.MinClearance():F5}");
            foreach(var scene in new[]{Scenario.Door,Scenario.Window})
            {
                s=new SlimeSimulation();s.Solids.AddRange(Arena.Create(scene));s.Reset(Arena.Spawn(scene));motor=new SlimeMotor(s);motor.Reset(90);float clearance=1;
                RunMotor(s,motor,5,i=>{clearance=Mathf.Min(clearance,s.MinClearance());return new SlimeInput{move=Vector2.up,cameraYaw=90,};});
                int crossed=0;foreach(var p in s.Position)if(p.x>2.4f)crossed++;
                Add(report,scene==Scenario.Door?"仅方向输入识别并挤过窄门":"仅方向输入识别并通过高窗",crossed==SlimeSimulation.Count&&clearance>-.002f,$"crossed={crossed}; clearance={clearance:F5}");
            }
            s=new SlimeSimulation();s.Solids.AddRange(Arena.Create(Scenario.Climb));s.Reset(Arena.Spawn(Scenario.Climb));motor=new SlimeMotor(s);motor.Reset(90);
            RunMotor(s,motor,2,i=>new SlimeInput{move=Vector2.up,cameraYaw=90});float climbHeight=s.Center.y;
            Add(report,"仅方向输入识别墙面并爬升",climbHeight>1.6f&&s.MinClearance()>-.002f,$"height={climbHeight:F3}");
            RunMotor(s,motor,4,i=>default);Add(report,"松开方向后取消攀附并下落",s.Center.y<climbHeight-.4f,$"before={climbHeight:F3}; after={s.Center.y:F3}");
        }
    }
}


