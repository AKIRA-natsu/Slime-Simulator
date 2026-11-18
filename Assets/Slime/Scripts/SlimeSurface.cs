using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace SlimeLab
{
    // GPU 连续密度场与光线求交：表面不再受低分辨率三角网格限制。
    public sealed class SlimeSurface:MonoBehaviour
    {
        public SlimeSimulation Simulation;
        public float GridSpacing=.055f;
        public double LastRebuildMs {get;private set;}
        ComputeShader compute;Material material;RenderTexture raw,volume,wet;
        bool ownsWet=true,primary=true;
        public RenderTexture Wetness=>wet;
        ComputeBuffer particles,flow,heads,links;
        int textureSize=112;
        readonly Vector4[] data=new Vector4[SlimeSimulation.Count],centers=new Vector4[64],halves=new Vector4[64],bubbles=new Vector4[96];
        readonly Vector4[] flowData=new Vector4[SlimeSimulation.Count];
        readonly int[] headData=new int[32*20*24],linkData=new int[SlimeSimulation.Count];
        int nx,ny,nz,lastRevision=-1,build,blurX,blurY,filter,stamp;Vector3 minimum,size;
        public void Initialize(SlimeSimulation simulation,Material glass,RenderTexture sharedWetness=null)
        {
            Simulation=simulation;material=new Material(glass);
            textureSize=Mathf.Clamp(Mathf.CeilToInt(112*Mathf.Pow(simulation.ParticleCount/(float)SlimeSimulation.Count,1f/3)/8)*8,32,112);
            compute=Instantiate(Resources.Load<ComputeShader>("SlimeVolume"));build=compute.FindKernel("BuildVolume");filter=compute.FindKernel("FilterVolume");
            blurX=compute.FindKernel("BlurX");blurY=compute.FindKernel("BlurY");
            stamp=compute.FindKernel("StampWetness");primary=ownsWet=sharedWetness==null;
            wet=sharedWetness;if(ownsWet){wet=new RenderTexture(512,384,0,RenderTextureFormat.RFloat){enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};wet.Create();ClearWetness();Shader.SetGlobalTexture("_LiquidWet",wet);}
            particles=new ComputeBuffer(SlimeSimulation.Count,16);flow=new ComputeBuffer(SlimeSimulation.Count,16);heads=new ComputeBuffer(headData.Length,4);links=new ComputeBuffer(linkData.Length,4);
            var temporary=GameObject.CreatePrimitive(PrimitiveType.Cube);var mesh=temporary.GetComponent<MeshFilter>().sharedMesh;
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;Destroy(temporary);
            var renderer=gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.BlendProbes;gameObject.layer=8;
        }
        public void SetEyes(Vector3 left,Vector3 right,Vector3 forward){material.SetVector("_EyeLeft",left);material.SetVector("_EyeRight",right);material.SetVector("_EyeForward",forward);material.SetFloat("_EyeScale",Mathf.Pow(Simulation.ParticleCount/(float)SlimeSimulation.Count,1f/3));}
        public void SetEnvironment(Texture environment){if(environment!=null)material.SetTexture("_Environment",environment);}
        public void ClearWetness(){if(wet==null)return;var prior=RenderTexture.active;RenderTexture.active=wet;GL.Clear(false,true,Color.clear);RenderTexture.active=prior;}
        public int SolidVolumeViolations()
        {
            var read=AsyncGPUReadback.Request(volume,0);read.WaitForCompletion();if(read.hasError)return -1;var values=read.GetData<float>();int count=0;
            for(int z=0;z<nz;z++)for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)if(values[(x+textureSize*(y+textureSize*z))*2]>0){Vector3 p=minimum+Vector3.Scale(size,new Vector3(x/(float)(nx-1),y/(float)(ny-1),z/(float)(nz-1)));bool inside=p.y<0;foreach(var box in Simulation.Solids)inside|=box.Distance(p)<-.0001f;if(inside)count++;}
            return count;
        }
        public void EnsureCurrent(){if(Simulation!=null&&Simulation.ParticleCount>0&&lastRevision!=Simulation.Revision)Rebuild();}
        void LateUpdate(){EnsureCurrent();}
        RenderTexture MakeVolume(string label)
        {
            var texture=new RenderTexture(textureSize,textureSize,0,label=="Slime boundary"?RenderTextureFormat.RGFloat:RenderTextureFormat.RFloat){name=label,dimension=TextureDimension.Tex3D,volumeDepth=textureSize,enableRandomWrite=true,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp};texture.Create();return texture;
        }
        public void Rebuild()
        {
            if(Simulation==null)return;var watch=System.Diagnostics.Stopwatch.StartNew();lastRevision=Simulation.Revision;
            int needed=Mathf.Clamp(Mathf.CeilToInt(112*Mathf.Pow(Simulation.ParticleCount/(float)SlimeSimulation.Count,1f/3)/8)*8,32,112);
            if(needed!=textureSize)
            {
                if(raw!=null){raw.Release();Destroy(raw);raw=null;}if(volume!=null){volume.Release();Destroy(volume);volume=null;}
                textureSize=needed;
            }
            Bounds bounds=Simulation.Bounds;bounds.Expand(1.6f);minimum=bounds.min;minimum.y=Mathf.Max(-.14f,minimum.y);size=bounds.max-minimum;
            int x=Mathf.Clamp(Mathf.CeilToInt(size.x/GridSpacing/8)*8,24,textureSize),y=Mathf.Clamp(Mathf.CeilToInt(size.y/GridSpacing/8)*8,24,textureSize),z=Mathf.Clamp(Mathf.CeilToInt(size.z/GridSpacing/8)*8,24,textureSize);
            nx=x;ny=y;nz=z;
            // 固定分配，逻辑区域随形变更新；保持原采样精度，避免边界尺寸变化时反复创建 GPU 纹理。
            if(raw==null){raw=MakeVolume("Slime density");volume=MakeVolume("Slime boundary");material.SetFloat("_VolumeTextureSize",textureSize);}
            transform.position=minimum+size*.5f;transform.localScale=size;Array.Fill(headData,-1);
            Vector3 mean=Vector3.zero;for(int i=0;i<Simulation.ParticleCount;i++)mean+=Simulation.Velocity[i]/Simulation.ParticleCount;
            for(int i=0;i<Simulation.ParticleCount;i++)
            {
                Vector3 p=Simulation.Position[i];float support=p.y;
                foreach(var box in Simulation.Solids)
                {
                    float top=box.center.y+box.half.y;
                    if(p.y>=top&&Mathf.Abs(p.x-box.center.x)<box.half.x&&Mathf.Abs(p.z-box.center.z)<box.half.z)support=Mathf.Min(support,p.y-top);
                }
                data[i]=new Vector4(p.x,p.y,p.z,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.09f,.25f,support)));
                // 仅沿粒子的相对流速拉长重建核，整团平移和静置不会凭空生成波纹。
                Vector3 relative=Simulation.Velocity[i]-mean;float speed=relative.magnitude;Vector3 axis=speed>.001f?relative/speed:Vector3.zero;
                flowData[i]=new Vector4(axis.x,axis.y,axis.z,1+Mathf.Min(.15f,speed*.075f));
                Vector3 cell=(p-minimum)/.9f;int cx=Mathf.FloorToInt(cell.x),cy=Mathf.FloorToInt(cell.y),cz=Mathf.FloorToInt(cell.z),index=cx+32*(cy+20*cz);
                linkData[i]=headData[index];headData[index]=i;
            }
            particles.SetData(data);flow.SetData(flowData);heads.SetData(headData);links.SetData(linkData);int count=0;
            foreach(var box in Simulation.Solids)if(bounds.Intersects(new Bounds(box.center,box.half*2)))
            {if(count==centers.Length)throw new InvalidOperationException("Too many local solids for slime field");centers[count]=box.center;halves[count++]=box.half;}
            compute.SetInts("_Dimensions",nx,ny,nz);compute.SetVector("_Minimum",minimum);compute.SetVector("_Size",size);
            compute.SetInt("_SolidCount",count);compute.SetVectorArray("_SolidCenters",centers);compute.SetVectorArray("_SolidHalves",halves);
            compute.SetBuffer(build,"_Particles",particles);compute.SetBuffer(build,"_Flow",flow);compute.SetBuffer(build,"_Heads",heads);compute.SetBuffer(build,"_Links",links);compute.SetTexture(build,"_Raw",raw);
            compute.Dispatch(build,(nx+3)/4,(ny+3)/4,(nz+3)/4);
            compute.SetTexture(blurX,"_Raw",raw);compute.SetTexture(blurX,"_Volume",volume);compute.Dispatch(blurX,(nx+3)/4,(ny+3)/4,(nz+3)/4);
            compute.SetTexture(blurY,"_Raw",raw);compute.SetTexture(blurY,"_Volume",volume);compute.Dispatch(blurY,(nx+3)/4,(ny+3)/4,(nz+3)/4);
            compute.SetTexture(filter,"_Raw",raw);compute.SetTexture(filter,"_Volume",volume);compute.Dispatch(filter,(nx+3)/4,(ny+3)/4,(nz+3)/4);
            compute.SetTexture(stamp,"_Volume",volume);compute.SetTexture(stamp,"_Wet",wet);compute.SetFloat("_WetDecay",primary?Mathf.Exp(-.045f*Mathf.Min(Time.deltaTime,.05f)):1);compute.Dispatch(stamp,64,48,1);
            material.SetTexture("_Volume",volume);material.SetVector("_VolumeMin",minimum);material.SetVector("_VolumeSize",size);material.SetVector("_VolumeResolution",new Vector3(nx,ny,nz));
            for(int i=0;i<bubbles.Length;i++)
            {
                Vector3 p=Simulation.Position[(i*173+41)%Simulation.ParticleCount];
                p+=new Vector3(Mathf.Sin(i*4.13f),Mathf.Sin(i*2.17f),Mathf.Cos(i*3.71f))*.047f;bubbles[i]=new Vector4(p.x,p.y,p.z,i%11==0?.017f:.0035f+(i%7)*.0008f);
            }
            material.SetVectorArray("_Bubbles",bubbles);material.SetInt("_BubbleCount",Mathf.Clamp(Simulation.ParticleCount/5,1,96));
            if(primary){Shader.SetGlobalTexture("_LiquidVolume",volume);Shader.SetGlobalVector("_LiquidMinimum",minimum);Shader.SetGlobalVector("_LiquidSize",size);Shader.SetGlobalVector("_LiquidResolution",new Vector3(nx,ny,nz));Shader.SetGlobalFloat("_LiquidTextureSize",textureSize);}
            LastRebuildMs=watch.Elapsed.TotalMilliseconds;
        }
        void OnDestroy(){particles?.Release();flow?.Release();heads?.Release();links?.Release();if(raw!=null){raw.Release();Destroy(raw);}if(volume!=null){volume.Release();Destroy(volume);}if(ownsWet&&wet!=null){wet.Release();Destroy(wet);}if(material!=null)Destroy(material);if(compute!=null)Destroy(compute);}
    }
}


