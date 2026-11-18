using UnityEngine;
namespace SlimeLab
{
    // 从实际盒体表面和空隙测量通行条件，不读取关卡名称或预设路径点。
    public sealed class SlimePerception
    {
        readonly SlimeSimulation simulation;
        int axis,side;float sign,exit,openingFloor,openingCeiling;Vector3 heading;float untilScan;
        public bool Opening {get;private set;}
        public bool Wall {get;private set;}
        public bool Climbable {get;private set;}
        public float TargetHeight {get;private set;}
        public SlimePerception(SlimeSimulation s){simulation=s;}
        public void Reset(){Opening=Wall=Climbable=false;untilScan=0;heading=Vector3.zero;}
        float At(Vector3 p)=>p[axis]*sign;
        bool Occupied(float plane,float lateral,float y)
        {
            Vector3 p=Vector3.zero;p[axis]=plane;p[side]=lateral;p.y=y;
            foreach(var box in simulation.Solids)
                if(Mathf.Abs(p[axis]-box.center[axis])<=box.half[axis]+.02f&&Mathf.Abs(p[side]-box.center[side])<box.half[side]+SlimeSimulation.Radius&&Mathf.Abs(y-box.center.y)<box.half.y+SlimeSimulation.Radius)return true;
            return false;
        }
        bool NarrowSlot(float plane,float lateral,float y,out float width)
        {
            float left=float.NegativeInfinity,right=float.PositiveInfinity;
            foreach(var box in simulation.Solids)
            {
                if(Mathf.Abs(plane-box.center[axis])>box.half[axis]+.02f||Mathf.Abs(y-box.center.y)>=box.half.y+SlimeSimulation.Radius)continue;
                float lo=box.center[side]-box.half[side]-SlimeSimulation.Radius,hi=box.center[side]+box.half[side]+SlimeSimulation.Radius;
                if(hi<=lateral)left=Mathf.Max(left,hi);else if(lo>=lateral)right=Mathf.Min(right,lo);else {width=0;return false;}
            }
            width=right-left;return float.IsFinite(width)&&width>=.28f&&width<2.6f&&lateral-left<1.8f&&right-lateral<1.8f;
        }
        public void Tick(Vector3 direction,float dt)
        {
            if(direction.sqrMagnitude<.01f||simulation.IsGrabbed){Reset();return;}
            Bounds bounds=simulation.Bounds;Vector3 center=simulation.Center;
            if(Vector3.Dot(heading,direction)<.7f)Reset();
            // 开口后缘仍有液体时维持收窄；全部通过或改变方向后才解除，避免在窗框内突然膨胀。
            if(Opening)
            {
                float rear=(sign>0?bounds.min[axis]:-bounds.max[axis])-.2f;
                if(rear<=exit&&At(center)>exit-4){TargetHeight=(openingFloor+openingCeiling)*.5f;return;}
                Reset();
            }
            untilScan-=dt;if(untilScan>0)return;untilScan=.08f;heading=direction;
            Opening=Wall=Climbable=false;axis=Mathf.Abs(direction.x)>=Mathf.Abs(direction.z)?0:2;side=axis==0?2:0;sign=Mathf.Sign(direction[axis]);
            float closest=float.PositiveInfinity;SolidBox obstacle=default;bool found=false;
            foreach(var box in simulation.Solids)
            {
                float front=box.center[axis]*sign-box.half[axis],distance=front-At(center);
                if(distance>bounds.extents[axis]+.55f||distance<-bounds.extents[axis]-.2f||box.center.y+box.half.y<bounds.min.y+.14f||box.center.y-box.half.y>bounds.max.y+.4f)continue;
                if(Mathf.Abs(center[side]-box.center[side])>box.half[side]+Mathf.Max(.8f,bounds.extents[side])+.15f)continue;
                if(distance<closest){closest=distance;obstacle=box;found=true;}
            }
            if(!found)return;
            float plane=(obstacle.center[axis]*sign-obstacle.half[axis]+.025f)*sign;
            float low=Mathf.Max(.08f,bounds.min.y-.1f),high=Mathf.Min(7,Mathf.Max(bounds.max.y+.6f,center.y+1.8f));
            float start=-1;
            int rows=Mathf.CeilToInt((high-low)/.08f);
            for(int row=0;row<=rows;row++)
            {
                float y=low+row*.08f;
                // 最后一行总是结算开放区间，无过梁的窄通道也必须被识别。
                bool free=row<rows&&!Occupied(plane,center[side],y)&&NarrowSlot(plane,center[side],y,out _);
                if(free&&start<0)start=y;
                if(!free&&start>=0)
                {
                    if(y-start>=.64f)
                    {
                        Opening=true;Climbable=obstacle.climbable;openingFloor=Mathf.Max(0,start-SlimeSimulation.Radius);openingCeiling=y-.08f+SlimeSimulation.Radius;
                        TargetHeight=(openingFloor+openingCeiling)*.5f;exit=obstacle.center[axis]*sign+obstacle.half[axis];return;
                    }
                    start=-1;
                }
            }
            // 只有输入方向正对实体才攀爬；擦过侧面、平行行走不会吸到墙上。
            Wall=Occupied(plane,center[side],Mathf.Max(bounds.min.y+.2f,center.y));
            Climbable=Wall&&obstacle.climbable;TargetHeight=obstacle.center.y+obstacle.half.y+.7f;
        }
        public bool NeedsLift(float centerHeight)=>Climbable&&((Opening&&openingFloor>.2f)||Wall)&&centerHeight<TargetHeight;
    }
}
