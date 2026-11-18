using Cinemachine;
using UnityEngine;
namespace SlimeLab
{
    // 可变形角色会先穿过窄孔。只约束相机实际经过的路径，避免视线碰到栏杆便把机位瞬间推到角色内部。
    public sealed class SlimeTpsObstacles:CinemachineExtension
    {
        const float Radius=.16f;
        Vector3 previous;Quaternion previousAim;bool initialized;float aperture,hold,apertureVelocity;
        bool Blocked(Vector3 from,Vector3 to)
        {
            Vector3 delta=to-from;return delta.sqrMagnitude>.000001f&&Physics.SphereCast(from,Radius,delta.normalized,out _,delta.magnitude,SlimeFollowCamera.ObstacleMask,QueryTriggerInteraction.Ignore);
        }
        Vector3 Sweep(Vector3 from,Vector3 to)
        {
            Vector3 remaining=to-from;
            for(int i=0;i<3&&remaining.sqrMagnitude>1e-10f;i++)
            {
                float length=remaining.magnitude;Vector3 direction=remaining/length;
                if(!Physics.SphereCast(from,Radius,direction,out var hit,length,SlimeFollowCamera.ObstacleMask,QueryTriggerInteraction.Ignore)){from+=remaining;break;}
                float travel=Mathf.Max(0,hit.distance-.003f);from+=direction*travel;
                remaining=Vector3.ProjectOnPlane(remaining-direction*travel,hit.normal);
            }
            from.y=Mathf.Max(Radius,from.y);return from;
        }
        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,CinemachineCore.Stage stage,ref CameraState state,float dt)
        {
            if(stage!=CinemachineCore.Stage.Body||vcam.Follow==null)return;
            Vector3 desired=state.CorrectedPosition;
            if(dt<0||!initialized||!vcam.PreviousStateIsValid){previous=desired;previousAim=vcam.Follow.rotation;aperture=hold=apertureVelocity=0;initialized=true;return;}
            if(Blocked(previous,desired)||Physics.CheckSphere(desired,Radius,SlimeFollowCamera.ObstacleMask,QueryTriggerInteraction.Ignore))hold=.65f;
            else hold=Mathf.Max(0,hold-dt);
            aperture=Mathf.SmoothDamp(aperture,hold>0?1:0,ref apertureVelocity,.18f,Mathf.Infinity,dt);
            var follow=((CinemachineVirtualCamera)vcam).GetCinemachineComponent<Cinemachine3rdPersonFollow>();
            Vector3 centered=vcam.Follow.position-vcam.Follow.forward*follow.CameraDistance-Vector3.up*.25f;
            Vector3 candidate=Vector3.Lerp(desired,centered,aperture);
            // 只限制无转向输入时的机位回弹；快速鼠标转向仍在同一帧响应。
            if(Quaternion.Angle(previousAim,vcam.Follow.rotation)<.01f)candidate=Vector3.MoveTowards(previous,candidate,8*dt);
            Vector3 position=Sweep(previous,candidate);previousAim=vcam.Follow.rotation;
            state.PositionCorrection+=position-desired;previous=position;
            // 不做 LookAt 或旋转补偿，避免碰撞改变玩家鼠标的瞄准方向。
        }
    }
}
