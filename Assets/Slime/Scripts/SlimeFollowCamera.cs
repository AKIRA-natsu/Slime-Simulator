using UnityEngine;
using Cinemachine;
namespace SlimeLab
{
    // TPS 的朝向由玩家输入决定，软体形变只影响平移目标，不参与瞄准旋转。
    public sealed class SlimeFollowCamera
    {
        readonly Camera camera;
        readonly Transform target;
        readonly CinemachineBrain brain;
        readonly CinemachineVirtualCamera virtualCamera;
        readonly Cinemachine3rdPersonFollow follow;
        public const float TargetHeight=.30f;
        public const int ThinObstacleLayer=11,ObstacleMask=(1<<10)|(1<<ThinObstacleLayer);
        Vector3 targetVelocity;bool resetPending;float lens=65,boom=4.8f,shoulder=1,sideVelocity;
        public float Yaw=90,Pitch=12,Distance=4.8f;
        public bool Following=true,Aiming;
        public Vector3 Focus=>target.position;
        public float ActualDistance=>Vector3.Distance(camera.transform.position,Focus);
        public bool Obstructed=>ActualDistance<boom-.1f;
        public SlimeFollowCamera(Camera view)
        {
            camera=view;target=new GameObject("TPS aim pivot").transform;
            brain=view.gameObject.AddComponent<CinemachineBrain>();brain.m_UpdateMethod=CinemachineBrain.UpdateMethod.ManualUpdate;
            virtualCamera=new GameObject("Slime Cinemachine TPS").AddComponent<CinemachineVirtualCamera>();
            virtualCamera.Follow=target;virtualCamera.m_Lens.FieldOfView=65;virtualCamera.m_Lens.NearClipPlane=.06f;virtualCamera.m_Lens.FarClipPlane=180;
            follow=virtualCamera.AddCinemachineComponent<Cinemachine3rdPersonFollow>();
            follow.ShoulderOffset=new Vector3(.85f,.55f,0);follow.VerticalArmLength=.18f;follow.CameraSide=1;
            follow.Damping=Vector3.zero;follow.CameraCollisionFilter=0;
            virtualCamera.gameObject.AddComponent<SlimeTpsObstacles>();
        }
        public void Reset(Vector3 center,float yaw)
        {
            Following=true;Aiming=false;Yaw=yaw;Pitch=12;Distance=4.8f;boom=Distance;lens=65;
            target.position=center+Vector3.up*TargetHeight;targetVelocity=Vector3.zero;virtualCamera.PreviousStateIsValid=false;resetPending=true;
        }
        public void SwapShoulder(){shoulder=1-shoulder;}
        public void Orbit(Vector2 delta){float sensitivity=Aiming?1.25f:2.2f;Yaw+=delta.x*sensitivity;Pitch=Mathf.Clamp(Pitch-delta.y*sensitivity,-30,65);}
        public void Tick(Vector3 center,float dt,bool snap=false)
        {
            snap|=resetPending;
            if(Following)
            {
                Vector3 wanted=center+Vector3.up*TargetHeight;
                if(snap){target.position=wanted;targetVelocity=Vector3.zero;}
                else
                {
                    Vector3 p=target.position;
                    p.x=Mathf.SmoothDamp(p.x,wanted.x,ref targetVelocity.x,.075f,Mathf.Infinity,dt);
                    p.z=Mathf.SmoothDamp(p.z,wanted.z,ref targetVelocity.z,.075f,Mathf.Infinity,dt);
                    p.y=Mathf.SmoothDamp(p.y,wanted.y,ref targetVelocity.y,.22f,Mathf.Infinity,dt);target.position=p;
                }
            }
            target.rotation=Quaternion.Euler(Pitch,Yaw,0);
            float blend=snap?1:1-Mathf.Exp(-12*dt);
            boom=Mathf.Lerp(boom,Aiming?Distance*.72f:Distance,blend);lens=Mathf.Lerp(lens,Aiming?55:65,blend);
            follow.CameraDistance=boom;follow.CameraSide=snap?shoulder:Mathf.SmoothDamp(follow.CameraSide,shoulder,ref sideVelocity,.22f,Mathf.Infinity,dt);virtualCamera.m_Lens.FieldOfView=lens;
            if(snap){virtualCamera.PreviousStateIsValid=false;virtualCamera.UpdateCameraState(Vector3.up,-1);resetPending=false;}
            float prior=CinemachineCore.UniformDeltaTimeOverride;CinemachineCore.UniformDeltaTimeOverride=dt;
            brain.ManualUpdate();CinemachineCore.UniformDeltaTimeOverride=prior;
        }
    }
}
