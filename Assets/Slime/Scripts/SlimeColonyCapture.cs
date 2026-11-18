using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace SlimeLab
{
    public sealed partial class SlimeLabController
    {
        [Serializable] sealed class ColonyFrame
        {
            public int frame,bodies,particles;public Vector3 leader,leaderViewport;public float clearance,farthestFollower;public bool cameraInsideSolid,cameraInsideLiquid;
        }
        [Serializable] sealed class ColonyCaptureReport
        {
            public string version="Slime v9 - Unity 6 HDRP optics",method="18 second deterministic 30 Hz replay, 90 Hz physics, 1440x900 SMAA. Video samples every second frame (15 fps). Uses the same split, movement, follow and merge code as gameplay.";
            public bool passed,hdrpActive,cinemachineActive,postProcessingEnabled;public List<ColonyFrame> frames=new List<ColonyFrame>();public List<int> gpuSolidViolations=new List<int>();
        }
        IEnumerator CaptureColony(string directory)
        {
            Directory.CreateDirectory(directory);batchAudit=true;offlineMotion=true;replaying=true;view.enabled=false;Application.runInBackground=true;
            var target=new RenderTexture(1440,900,24,RenderTextureFormat.ARGB32){antiAliasing=1};target.Create();
            var pixels=new Texture2D(1440,900,TextureFormat.RGB24,false);
            var report=new ColonyCaptureReport{cinemachineActive=view.GetComponent<Cinemachine.CinemachineBrain>()!=null,postProcessingEnabled=view.GetComponent<HDAdditionalCameraData>().renderingPathCustomFrameSettings.IsEnabled(FrameSettingsField.Postprocess)};
            Select(Scenario.VerticalRail);rig.Yaw=90;rig.Pitch=12;rig.Distance=4.8f;
            UpdatePresentation(1f/30,true);surface.EnsureCurrent();view.targetTexture=target;SlimeRendering.Render(view);view.targetTexture=null;yield return null;
            for(int frame=0;frame<540;frame++)
            {
                if(frame==120){Select(Scenario.Course);rig.Yaw=90;rig.Pitch=12;rig.Distance=4.8f;for(int i=0;i<6;i++)SplitSlime();}
                command=new SlimeInput{move=frame<270?Vector2.up:Vector2.zero,cameraYaw=90};
                for(int step=0;step<3;step++)Simulate(SlimeSimulation.StepTime);
                UpdatePresentation(1f/30,frame==0||frame==120);surface.EnsureCurrent();foreach(var skin in followerSurfaces.Values)skin.EnsureCurrent();spray.UpdateMesh();
                var sample=new ColonyFrame{frame=frame,bodies=colony.Followers.Count+1,particles=colony.TotalParticles,leader=Simulation.Center,clearance=Simulation.MinClearance(),cameraInsideSolid=Physics.CheckSphere(view.transform.position,.1f,SlimeFollowCamera.ObstacleMask,QueryTriggerInteraction.Ignore)};
                sample.leaderViewport=view.WorldToViewportPoint(Simulation.Center);sample.cameraInsideLiquid=Simulation.Bounds.Contains(view.transform.position);
                foreach(var f in colony.Followers){sample.clearance=Mathf.Min(sample.clearance,f.simulation.MinClearance());sample.farthestFollower=Mathf.Max(sample.farthestFollower,Vector3.Distance(f.simulation.Center,Simulation.Center));}
                report.frames.Add(sample);
                if(frame%2==0)
                {
                    view.targetTexture=target;SlimeRendering.Render(view);view.targetTexture=null;
                    RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1440,900),0,0);pixels.Apply();RenderTexture.active=null;
                    File.WriteAllBytes(Path.Combine(directory,$"frame-{frame/2:D4}.png"),pixels.EncodeToPNG());
                }
                if(frame==30||frame==60||frame==118||frame==140||frame==180||frame==260||frame==330||frame==450||frame==538)
                {
                    int violations=surface.SolidVolumeViolations();foreach(var skin in followerSurfaces.Values){int n=skin.SolidVolumeViolations();violations=n<0?-1000:violations+n;}
                    report.gpuSolidViolations.Add(violations);Debug.Log("[SLIME-COLONY] "+JsonUtility.ToJson(sample)+"; solid voxels="+violations);
                }
                yield return null;
            }
            report.hdrpActive=RenderPipelineManager.currentPipeline is HDRenderPipeline;
            report.passed=report.hdrpActive&&report.cinemachineActive&&report.postProcessingEnabled&&report.frames[118].leader.x>3&&report.frames[120].bodies>=7&&report.frames[539].bodies==1;
            foreach(var f in report.frames)report.passed&=f.particles==512&&f.clearance>-.0001f&&!f.cameraInsideSolid&&!f.cameraInsideLiquid&&f.farthestFollower<6&&f.leaderViewport.z>0&&f.leaderViewport.x>.02f&&f.leaderViewport.x<.98f&&f.leaderViewport.y>.02f&&f.leaderViewport.y<.98f;
            foreach(int n in report.gpuSolidViolations)report.passed&=n==0;
            File.WriteAllText(Path.Combine(directory,"results.json"),JsonUtility.ToJson(report,true));target.Release();Destroy(target);Destroy(pixels);Application.Quit(report.passed?0:2);
        }
    }
}
