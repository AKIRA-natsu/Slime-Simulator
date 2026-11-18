using System.Collections.Generic;
using UnityEngine;
namespace SlimeLab
{
    public enum Scenario { Rest, Wall, Climb, Door, Window, Course, Rail, VerticalRail }
    public static class Arena
    {
        public static readonly string[] Names={"静置与提拉","实体墙防穿透","爬墙与屋顶","窄门挤压","高窗穿行","TPS 街区探索","横栏杆与挤压","竖栏杆自动断团"};
        public static List<SolidBox> District()
        {
            var boxes=new List<SolidBox>();
            foreach(float z in new[]{-19f,19f})foreach(float x in new[]{-34f,-12f,12f,34f})
            {
                float height=Mathf.Abs(x)>20?5.2f:4.6f;
                boxes.Add(new SolidBox("District building",new Vector3(x,height*.5f,z),new Vector3(7,height*.5f,5.5f),true));
                boxes.Add(new SolidBox("District paving",new Vector3(x,.08f,z),new Vector3(8.5f,.08f,7),true));
            }
            foreach(float x in new[]{-49f,49f})boxes.Add(new SolidBox("District boundary",new Vector3(x,1.2f,0),new Vector3(.15f,1.2f,31)));
            foreach(float z in new[]{-31f,31f})boxes.Add(new SolidBox("District boundary",new Vector3(0,1.2f,z),new Vector3(49,1.2f,.15f)));
            foreach(float x in new[]{-25f,0,25f})foreach(float z in new[]{-10f,10f})
            {
                boxes.Add(new SolidBox("Street lamp",new Vector3(x,2.1f,z),new Vector3(.045f,2.1f,.045f)));
                boxes.Add(new SolidBox("Lamp hood",new Vector3(x,4.22f,z),new Vector3(.325f,.06f,.325f)));
                boxes.Add(new SolidBox("Planter",new Vector3(x+1.6f,.3f,z),new Vector3(.6f,.3f,.6f),true));
            }
            return boxes;
        }
        public static Vector3 Spawn(Scenario scenario)=>new Vector3(scenario==Scenario.Course?-10:scenario==Scenario.Rest?0:-.5f,1,0);
        public static List<SolidBox> Create(Scenario scenario)
        {
            var boxes=new List<SolidBox>();
            if(scenario==Scenario.VerticalRail)for(int z=-4;z<=4;z++)boxes.Add(new SolidBox("Rail vertical",new Vector3(2,1.5f,z*.7f),new Vector3(.1f,1.5f,.1f)));
            if(scenario==Scenario.Rail)
            {
                foreach(float y in new[]{.5f,1.05f,1.6f})boxes.Add(new SolidBox("Rail beam",new Vector3(2,y,0),new Vector3(.055f,.045f,3.1f)));
                foreach(float z in new[]{-3f,3f})boxes.Add(new SolidBox("Rail post",new Vector3(2,.85f,z),new Vector3(.07f,.85f,.07f)));
            }
            if(scenario==Scenario.Course)
            {
                boxes.Add(new SolidBox("Low obstacle",new Vector3(-4,.4f,-6.2f),new Vector3(.6f,.4f,1),true));
                boxes.Add(new SolidBox("Climb platform",new Vector3(.3f,1.1f,7.35f),new Vector3(1.6f,1.1f,1.45f),true));
                foreach(float x in new[]{6f,12f})
                {
                    float gap=x==6?.95f:1.1f,low=x==6?0:.65f,top=x==6?2.4f:2.1f;
                    float z=x==6?6:-6;
                    foreach(float side in new[]{-1f,1f})boxes.Add(new SolidBox(x==6?"Door jamb":"Window jamb",new Vector3(x,1.8f,z+side*(2.2f+gap*.25f)),new Vector3(.25f,1.8f,2.2f-gap*.25f),true));
                    boxes.Add(new SolidBox("Lintel",new Vector3(x,(3.6f+top)*.5f,z),new Vector3(.25f,(3.6f-top)*.5f,gap*.5f),true));
                    if(low>0)boxes.Add(new SolidBox("Window sill",new Vector3(x,low*.5f,z),new Vector3(.25f,low*.5f,gap*.5f),true));
                }
                foreach(float z in new[]{-3f,-2.25f,-1.5f,-.75f,0,.75f,1.5f,2.25f,3f})boxes.Add(new SolidBox("Rail vertical",new Vector3(-21,1,z+6.5f),new Vector3(.08f,1,.08f)));
                foreach(float y in new[]{.5f,1.05f,1.6f})boxes.Add(new SolidBox("Rail horizontal",new Vector3(23,y,-6),new Vector3(.055f,.045f,3.1f)));
                for(int i=0;i<4;i++)boxes.Add(new SolidBox("Roof approach",new Vector3(4+i*1.1f,.25f*(i+1),-11),new Vector3(.55f,.25f*(i+1),1.7f),true));
            }
            if(scenario==Scenario.Wall)boxes.Add(new SolidBox("Solid wall",new Vector3(2,3.6f,0),new Vector3(.12f,3.6f,5.2f)));
            if(scenario==Scenario.Climb)boxes.Add(new SolidBox("Climbing building",new Vector3(3.5f,1.5f,0),new Vector3(1.5f,1.5f,2),true));
            if(scenario==Scenario.Door||scenario==Scenario.Window)
            {
                float gap=scenario==Scenario.Door?.74f:.9f;
                boxes.Add(new SolidBox("Left jamb",new Vector3(2,2,-(2.5f+gap*.25f)),new Vector3(.25f,2,2.5f-gap*.25f),true));
                boxes.Add(new SolidBox("Right jamb",new Vector3(2,2,2.5f+gap*.25f),new Vector3(.25f,2,2.5f-gap*.25f),true));
                float top=scenario==Scenario.Door?2.4f:2.1f;
                boxes.Add(new SolidBox("Lintel",new Vector3(2,(4+top)*.5f,0),new Vector3(.25f,(4-top)*.5f,gap*.5f),true));
                if(scenario==Scenario.Window)boxes.Add(new SolidBox("Sill",new Vector3(2,.3f,0),new Vector3(.25f,.3f,gap*.5f),true));
            }
            return boxes;
        }
    }
}
