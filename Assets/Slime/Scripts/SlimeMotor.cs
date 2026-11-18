using UnityEngine;
namespace SlimeLab
{
    public struct SlimeInput
    {
        public Vector2 move;
        public float cameraYaw;
        public bool sprint,jump;
    }

    // 玩家和回放共用同一控制器；输入只产生力，不直接平移液体或关闭碰撞。
    public sealed class SlimeMotor
    {
        readonly SlimeSimulation simulation;
        readonly SlimePerception perception;
        readonly System.Collections.Generic.List<SolidBox> contacts=new System.Collections.Generic.List<SolidBox>();
        float jumpCooldown;
        public float FacingYaw {get;private set;}
        public bool Grounded {get;private set;}
        public bool Climbing {get;private set;}
        public bool Squeezing {get;private set;}
        public int JumpCount {get;private set;}
        public Vector3 Forward=>Direction(new Vector2(0,1),FacingYaw);
        public Vector3 Velocity {get;private set;}
        public string State=>Climbing?"攀附":Squeezing?"挤压":!Grounded?"空中":Velocity.magnitude>.15f?"移动":"停留";
        public SlimeMotor(SlimeSimulation s){simulation=s;perception=new SlimePerception(s);}
        public void Reset(float yaw){FacingYaw=yaw;jumpCooldown=0;JumpCount=0;Velocity=Vector3.zero;Grounded=true;Climbing=false;Squeezing=false;perception.Reset();}
        public static Vector3 Direction(Vector2 input,float yaw)
        {
            input=Vector2.ClampMagnitude(input,1);float a=yaw*Mathf.Deg2Rad,s=Mathf.Sin(a),c=Mathf.Cos(a);
            return new Vector3(input.x*c+input.y*s,0,input.y*c-input.x*s);
        }
        public void Tick(SlimeInput input,float dt)
        {
            jumpCooldown=Mathf.Max(0,jumpCooldown-dt);Velocity=Vector3.zero;int supported=0,wallContact=0;
            Bounds region=simulation.Bounds;region.Expand(1.4f);contacts.Clear();
            foreach(var box in simulation.Solids)if(region.Intersects(new Bounds(box.center,box.half*2)))contacts.Add(box);
            for(int i=0;i<simulation.ParticleCount;i++)Velocity+=simulation.Velocity[i]/simulation.ParticleCount;
            for(int particle=0;particle<simulation.ParticleCount;particle++)
            {
                Vector3 p=simulation.Position[particle];bool floor=p.y<SlimeSimulation.Radius+.045f;
                foreach(var box in contacts)
                {
                    float top=box.center.y+box.half.y;
                    floor|=p.y>=top&&p.y-top<SlimeSimulation.Radius+.045f&&Mathf.Abs(p.x-box.center.x)<box.half.x&&Mathf.Abs(p.z-box.center.z)<box.half.z;
                    if(box.climbable&&p.y<top+.3f&&box.Distance(p)<.65f)wallContact++;
                }
                if(floor)supported++;
            }
            Grounded=supported>=Mathf.Max(1,simulation.ParticleCount/64)&&Velocity.y<.8f;
            Vector3 direction=Direction(input.move,input.cameraYaw);perception.Tick(direction,dt);
            Climbing=jumpCooldown<=0&&wallContact>=Mathf.Max(3,simulation.ParticleCount/85)&&perception.NeedsLift(simulation.Center.y);Squeezing=perception.Opening;
            simulation.Goal=null;simulation.SqueezeDirection=Vector3.zero;simulation.Climb=Climbing;
            if(simulation.IsGrabbed){simulation.Drive=Vector3.zero;Climbing=false;Squeezing=false;simulation.Climb=false;return;}
            if(direction.sqrMagnitude>.01f)FacingYaw=Mathf.MoveTowardsAngle(FacingYaw,Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,360*dt);
            Vector3 horizontal=new Vector3(Velocity.x,0,Velocity.z),desired=direction*(input.sprint?3.4f:2.0f);
            simulation.Drive=direction.sqrMagnitude<.01f&&horizontal.magnitude<.12f?Vector3.zero:Vector3.ClampMagnitude((desired-horizontal)*7+desired*1.7f,32)*(Grounded?1f:.45f);
            if(Climbing)simulation.Drive=Vector3.ClampMagnitude((direction*1.3f-horizontal)*6+direction*3,16)+Vector3.up*Mathf.Clamp((1.4f-Velocity.y)*10,-16,18);
            if(Squeezing)simulation.SqueezeDirection=direction.normalized;
            if(input.jump&&jumpCooldown<=0&&(Grounded||Climbing))
            {
                simulation.Impulse(Vector3.up*(5.2f-Mathf.Min(0,Velocity.y)));jumpCooldown=.5f;JumpCount++;Grounded=false;simulation.Climb=false;Climbing=false;
            }
        }
    }
}

