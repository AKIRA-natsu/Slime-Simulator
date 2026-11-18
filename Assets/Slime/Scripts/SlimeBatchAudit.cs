using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
namespace SlimeLab
{
    public sealed partial class SlimeLabController
    {
        bool batchAudit;
        [Serializable] sealed class BatchStage
        {
            public string name;public Timing batches,drawCalls,setPass,triangles,renderAndFenceMs;
        }
        [Serializable] sealed class BatchReport
        {
            public string method="Release Player, 1440x900 SMAA. One explicit HDRP StandardRequest per frame; GUI suppressed. ProfilerRecorder reads the completed prior frame after yield. 15 warmup + 45 samples per view. Includes depth, shadows and post-processing; reflection_refresh intentionally renders six cubemap faces together, while gameplay refreshes one face per presentation frame. Fill-disabled view is diagnostic only.";
            public string utc,device;public bool countersValid;public int streetRenderers,streetColliders;public BatchStage[] stages;
        }
        IEnumerator AuditBatches(string path)
        {
            batchAudit=true;offlineMotion=true;view.enabled=false;Application.runInBackground=true;
            yield return null;
            string directory=Path.GetDirectoryName(Path.GetFullPath(path));Directory.CreateDirectory(directory);
            var batches=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Batches Count");
            if(!batches.Valid){batches.Dispose();batches=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Total Batches Count");}
            var draws=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count");var passes=ProfilerRecorder.StartNew(ProfilerCategory.Render,"SetPass Calls Count");var triangles=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count");
            var street=GameObject.Find("Static street geometry");var report=new BatchReport{utc=DateTime.UtcNow.ToString("o"),device=SystemInfo.graphicsDeviceName,countersValid=batches.Valid&&draws.Valid&&passes.Valid&&triangles.Valid,streetRenderers=street.GetComponentsInChildren<MeshRenderer>().Length,streetColliders=street.GetComponentsInChildren<Collider>().Length,stages=new BatchStage[5]};
            if(!report.countersValid){Debug.LogError("[SLIME-BATCH] Rendering counters unavailable");Application.Quit(2);yield break;}
            var target=new RenderTexture(1440,900,24,RenderTextureFormat.ARGB32){antiAliasing=1};target.Create();var fence=new Texture2D(1,1,TextureFormat.RGB24,false);
            var fill=GameObject.Find("Cool fill").GetComponent<Light>();var names=new[]{"course_default","course_reverse","rest_front","reflection_refresh","fill_disabled_diagnostic"};
            for(int stage=0;stage<names.Length;stage++)
            {
                Select(stage==2?Scenario.Rest:Scenario.Course);rig.Yaw=stage==1?-90:stage==2?-76:90;rig.Pitch=stage==2?12:23;UpdatePresentation(1f/60,true);fill.enabled=stage!=4;
                var bc=new List<double>();var dc=new List<double>();var sp=new List<double>();var tc=new List<double>();var ms=new List<double>();
                for(int frame=0;frame<60;frame++)
                {
                    var watch=System.Diagnostics.Stopwatch.StartNew();if(stage==3||!environmentReady)RefreshEnvironment();
                    view.targetTexture=target;SlimeRendering.Render(view);RenderTexture.active=target;fence.ReadPixels(new Rect(0,0,1,1),0,0,false);fence.Apply(false);RenderTexture.active=null;view.targetTexture=null;double elapsed=watch.Elapsed.TotalMilliseconds;
                    yield return null;
                    if(frame>=15){bc.Add(batches.LastValue);dc.Add(draws.LastValue);sp.Add(passes.LastValue);tc.Add(triangles.LastValue);ms.Add(elapsed);}
                }
                report.stages[stage]=new BatchStage{name=names[stage],batches=new Timing(bc),drawCalls=new Timing(dc),setPass=new Timing(sp),triangles=new Timing(tc),renderAndFenceMs=new Timing(ms)};
                var pixels=new Texture2D(1440,900,TextureFormat.RGB24,false);RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1440,900),0,0);pixels.Apply();RenderTexture.active=null;File.WriteAllBytes(Path.Combine(directory,names[stage]+".png"),pixels.EncodeToPNG());Destroy(pixels);
                Debug.Log("[SLIME-BATCH] "+JsonUtility.ToJson(report.stages[stage]));
            }
            fill.enabled=true;File.WriteAllText(path,JsonUtility.ToJson(report,true));batches.Dispose();draws.Dispose();passes.Dispose();triangles.Dispose();target.Release();Destroy(target);Destroy(fence);Application.Quit();
        }
    }
}

