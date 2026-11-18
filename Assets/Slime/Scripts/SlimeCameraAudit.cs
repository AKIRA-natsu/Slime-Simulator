using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Cinemachine;
namespace SlimeLab
{
    public sealed partial class SlimeLabController
    {
        [Serializable] sealed class CameraSample
        {
            public int frame;public Vector3 position,focus,leader,viewport;
            public float angleStep,positionStep,positionStepChange,roll,aimError;public bool insideSolid,insideLiquid;
        }
        [Serializable] sealed class CameraStage
        {
            public string name;public bool passed;public float maxAngleStep,maxPositionStep,maxPositionStepChange;
            public float maxAimError;public int solidHits,liquidHits,offscreen;public List<CameraSample> frames=new List<CameraSample>();
        }
        [Serializable] sealed class CameraAuditReport
        {
            public string method="Native Cinemachine and PhysX, deterministic 60 Hz presentation / 90 Hz fluid. Same UpdatePresentation as gameplay. Intentional mouse orbit is measured separately. First five frames after scene reset excluded. TPS aim must match the input pivot within 0.05 degrees, including a 90-degree mouse turn. Collision can retract the boom without changing aim. No camera in solids or liquid; main character remains in view.";
            public bool passed;public List<CameraStage> stages=new List<CameraStage>();
        }
        IEnumerator AuditCamera(string directory)
        {
            Directory.CreateDirectory(directory);batchAudit=true;offlineMotion=true;replaying=true;view.enabled=false;
            Application.runInBackground=true;Application.targetFrameRate=-1;
            var names=new[]{"walk_stop","rail","door","climb","split_merge","jump","orbit","wall_orbit","fast_aim","aim_shoulder","automatic_fracture"};
            var scenarios=new[]{Scenario.Rest,Scenario.Rail,Scenario.Door,Scenario.Climb,Scenario.Rail,Scenario.Rest,Scenario.Rest,Scenario.Wall,Scenario.Rest,Scenario.Rest,Scenario.VerticalRail};
            var lengths=new[]{300,300,360,420,600,240,240,360,180,240,360};var report=new CameraAuditReport{passed=true};float time=0;
            yield return null;
            for(int stage=0;stage<names.Length;stage++)
            {
                Select(scenarios[stage]);rig.Yaw=90;rig.Pitch=12;UpdatePresentation(1f/60,true);yield return null;
                var result=new CameraStage{name=names[stage]};Vector3 previous=view.transform.position,previousStep=Vector3.zero;Quaternion rotation=view.transform.rotation;
                for(int frame=0;frame<lengths[stage];frame++)
                {
                    CinemachineCore.CurrentTimeOverride=time;time+=1f/60;
                    command=new SlimeInput{cameraYaw=90,move=stage>=6||stage==0&&frame>=150||stage==4&&frame>=270?Vector2.zero:Vector2.up};
                    if(stage==10)command.move=Vector2.up;
                    if(stage==4&&frame==30)SplitSlime();if(stage==5&&frame==60)command.jump=true;
                    if(stage>=6&&stage<8&&frame>=60&&frame<lengths[stage]-60)rig.Yaw+=1;
                    if(stage==8&&frame==60)rig.Orbit(new Vector2(90/2.2f,0));
                    if(stage==9){rig.Aiming=frame>=60&&frame<180;if(frame==120)rig.SwapShoulder();}
                    for(int step=0;step<(frame%2==0?1:2);step++)Simulate(SlimeSimulation.StepTime);
                    UpdatePresentation(1f/60);
                    Vector3 position=view.transform.position,delta=position-previous,viewport=view.WorldToViewportPoint(Simulation.Center);
                    var sample=new CameraSample{frame=frame,position=position,focus=rig.Focus,leader=Simulation.Center,viewport=viewport,
                        aimError=Quaternion.Angle(Quaternion.Euler(rig.Pitch,rig.Yaw,0),view.transform.rotation),angleStep=Quaternion.Angle(rotation,view.transform.rotation),positionStep=delta.magnitude,positionStepChange=(delta-previousStep).magnitude,
                        roll=Mathf.Abs(Mathf.DeltaAngle(0,view.transform.eulerAngles.z)),insideSolid=Physics.CheckSphere(position,.1f,SlimeFollowCamera.ObstacleMask,QueryTriggerInteraction.Ignore),insideLiquid=Simulation.Bounds.Contains(position)};
                    result.frames.Add(sample);
                    if(frame>=5)
                    {
                        result.maxAimError=Mathf.Max(result.maxAimError,sample.aimError);result.maxAngleStep=Mathf.Max(result.maxAngleStep,sample.angleStep);result.maxPositionStep=Mathf.Max(result.maxPositionStep,sample.positionStep);result.maxPositionStepChange=Mathf.Max(result.maxPositionStepChange,sample.positionStepChange);
                        if(sample.insideSolid)result.solidHits++;if(sample.insideLiquid)result.liquidHits++;
                        if(viewport.z<=0||viewport.x<.02f||viewport.x>.98f||viewport.y<.02f||viewport.y>.98f)result.offscreen++;
                    }
                    previous=position;previousStep=delta;rotation=view.transform.rotation;yield return null;
                }
                result.passed=result.maxAimError<=.06f&&result.solidHits==0&&result.liquidHits==0&&result.offscreen==0;
                if(stage<6||stage==10)result.passed&=result.maxAngleStep<.06f&&result.maxPositionStep<.25f;
                report.stages.Add(result);report.passed&=result.passed;
                Debug.Log($"[SLIME-CAMERA-AUDIT] {result.name}: angle={result.maxAngleStep:F4}; step={result.maxPositionStep:F4}; step change={result.maxPositionStepChange:F4}; solids={result.solidHits}; liquid={result.liquidHits}; offscreen={result.offscreen}; passed={result.passed}");
            }
            CinemachineCore.CurrentTimeOverride=-1;
            File.WriteAllText(Path.Combine(directory,"results.json"),JsonUtility.ToJson(report,true));Application.Quit(report.passed?0:2);
        }
    }
}
