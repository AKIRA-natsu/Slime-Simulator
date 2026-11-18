using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace SlimeLab
{
    public sealed partial class SlimeLabController
    {
        [Serializable] sealed class Timing
        {
            public double mean,median,p95;
            public Timing(List<double> values){values.Sort();foreach(double v in values)mean+=v;mean/=values.Count;median=values[values.Count/2];p95=values[(int)((values.Count-1)*.95)];}
        }
        [Serializable] sealed class BenchmarkStage
        {
            public string name;public int samples;public double resetMs;
            public Timing physicsCpu,volumeSubmitCpu,presentationCpu,renderAndGpuFence,total;
            public Vector3 bounds;public float speed;
        }
        [Serializable] sealed class BenchmarkReport
        {
            public string method="1440x900 SMAA, deterministic 60 Hz replay / 90 Hz simulation; 30 warmup + 90 samples per stage. Render includes a synchronous one-pixel readback GPU fence. CPU submission and synchronized frame cost, not gameplay FPS or GPU timestamp queries.";
            public string device;public string utc;public BenchmarkStage[] stages;
        }
        IEnumerator Benchmark(string path)
        {
            offlineMotion=true;replaying=true;view.enabled=false;Application.runInBackground=true;
            string directory=Path.GetDirectoryName(Path.GetFullPath(path));Directory.CreateDirectory(directory);
            var target=new RenderTexture(1440,900,24,RenderTextureFormat.ARGB32){antiAliasing=1};target.Create();
            var fence=new Texture2D(1,1,TextureFormat.RGB24,false);
            var report=new BenchmarkReport{device=SystemInfo.graphicsDeviceName,utc=DateTime.UtcNow.ToString("o"),stages=new BenchmarkStage[4]};
            for(int stage=0;stage<4;stage++)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();Select(Scenario.Course);double reset=watch.Elapsed.TotalMilliseconds;if(stage==3)for(int i=0;i<15;i++)colony.Split();
                var physics=new List<double>();var volume=new List<double>();var presentation=new List<double>();var render=new List<double>();var total=new List<double>();
                for(int frame=0;frame<120;frame++)
                {
                    command=new SlimeInput{move=stage==0?Vector2.zero:Vector2.up,cameraYaw=90,sprint=stage==2};
                    watch.Restart();for(int step=0;step<(frame%2==0?1:2);step++)Simulate(SlimeSimulation.StepTime);double p=watch.Elapsed.TotalMilliseconds;
                    watch.Restart();UpdatePresentation(1f/60);double c=watch.Elapsed.TotalMilliseconds;
                    watch.Restart();surface.EnsureCurrent();foreach(var skin in followerSurfaces.Values)skin.EnsureCurrent();double v=watch.Elapsed.TotalMilliseconds;
                    watch.Restart();view.targetTexture=target;SlimeRendering.Render(view);RenderTexture.active=target;fence.ReadPixels(new Rect(0,0,1,1),0,0,false);fence.Apply(false);RenderTexture.active=null;view.targetTexture=null;double r=watch.Elapsed.TotalMilliseconds;
                    if(frame>=30){physics.Add(p);presentation.Add(c);volume.Add(v);render.Add(r);total.Add(p+c+v+r);}
                    yield return null;
                }
                report.stages[stage]=new BenchmarkStage{name=new[]{"rest","walk","run","sixteen_bodies"}[stage],samples=90,resetMs=reset,physicsCpu=new Timing(physics),volumeSubmitCpu=new Timing(volume),presentationCpu=new Timing(presentation),renderAndGpuFence=new Timing(render),total=new Timing(total),bounds=Simulation.Bounds.size,speed=motor.Velocity.magnitude};
                var snapshot=new Texture2D(1440,900,TextureFormat.RGB24,false);RenderTexture.active=target;snapshot.ReadPixels(new Rect(0,0,1440,900),0,0);snapshot.Apply();RenderTexture.active=null;File.WriteAllBytes(Path.Combine(directory,"benchmark-"+report.stages[stage].name+".png"),snapshot.EncodeToPNG());Destroy(snapshot);
                Debug.Log("[SLIME-BENCHMARK] "+JsonUtility.ToJson(report.stages[stage]));
            }
            File.WriteAllText(path,JsonUtility.ToJson(report,true));target.Release();Destroy(target);Destroy(fence);Application.Quit();
        }
    }
}


