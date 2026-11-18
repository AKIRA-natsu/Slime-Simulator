using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace SlimeLab
{
    public static class SlimeHDRPProject
    {
        const string Root="Assets/Slime/Resources/";
        static T Component<T>(VolumeProfile p) where T:VolumeComponent
        {
            if(!p.TryGet<T>(out var c)){c=p.Add<T>(true);AssetDatabase.AddObjectToAsset(c,p);}return c;
        }
        static Material Material(string name,string shader)
        {
            string path=Root+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}else m.shader=Shader.Find(shader);
            m.shaderKeywords=Array.Empty<string>();EditorUtility.SetDirty(m);return m;
        }
        static void Texture(string name,bool normal=false,bool srgb=false,bool readable=false)
        {
            var t=(TextureImporter)AssetImporter.GetAtPath(Root+name+".jpg");t.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            t.sRGBTexture=srgb;t.isReadable=readable;t.anisoLevel=8;t.mipmapEnabled=true;t.SaveAndReimport();
        }
        static void Surface(string material,string prefix,float scale,Color tint)
        {
            Texture(prefix+"Color",srgb:true);Texture(prefix+"Normal",normal:true);Texture(prefix+"Roughness",readable:true);
            var rough=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+prefix+"Roughness.jpg");var pixels=rough.GetPixels32();
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(0,255,255,(byte)(255-pixels[i].r));
            var mask=new Texture2D(rough.width,rough.height,TextureFormat.RGBA32,false,true);mask.SetPixels32(pixels);mask.Apply();
            string path=Root+prefix+"Mask.png";File.WriteAllBytes(path,mask.EncodeToPNG());UnityEngine.Object.DestroyImmediate(mask);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=false;importer.anisoLevel=8;importer.SaveAndReimport();
            var m=Material(material,"HDRP/Lit");m.SetColor("_BaseColor",tint);m.SetFloat("_Smoothness",1);
            m.SetTexture("_BaseColorMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+prefix+"Color.jpg"));
            m.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+prefix+"Normal.jpg"));m.SetFloat("_NormalScale",.7f);
            m.SetTexture("_MaskMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));m.SetFloat("_UVBase",5);m.SetFloat("_TexWorldScale",scale);
            HDShaderUtils.ResetMaterialKeywords(m);EditorUtility.SetDirty(m);
        }
        static Cubemap Daylight(out float illuminance)
        {
            string path=Root+"DaylightSky.hdr";var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureShape=TextureImporterShape.TextureCube;importer.generateCubemap=TextureImporterGenerateCubemap.AutoCubemap;
            importer.sRGBTexture=false;importer.isReadable=true;importer.maxTextureSize=512;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            var cube=AssetDatabase.LoadAssetAtPath<Cubemap>(path);double lux=0;int n=cube.width;
            // 积分上半球照度，让摄影 HDRI 的任意曝光正确对应场景中的物理照度。
            for(int face=0;face<6;face++)
            {
                var pixels=cube.GetPixels((CubemapFace)face);
                for(int y=0;y<n;y++)for(int x=0;x<n;x++)
                {
                    float u=2*(x+.5f)/n-1,v=2*(y+.5f)/n-1;
                    Vector3 d=face==0?new Vector3(1,v,-u):face==1?new Vector3(-1,v,u):face==2?new Vector3(u,1,-v):face==3?new Vector3(u,-1,v):face==4?new Vector3(u,v,1):new Vector3(-u,v,-1);
                    Color c=pixels[x+y*n];lux+=(c.r*.2126+c.g*.7152+c.b*.0722)*Mathf.Max(0,d.normalized.y)*4/(n*(double)n*Math.Pow(1+u*u+v*v,1.5));
                }
            }
            illuminance=(float)Math.Max(.0001,lux);importer.isReadable=false;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        }
        public static void Configure()
        {
            PlayerSettings.companyName="Slime Lab";PlayerSettings.productName="Slime Simulator HDRP";
            PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.runInBackground=true;PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});QualitySettings.vSyncCount=0;QualitySettings.antiAliasing=0;
            // 管线资产不混用。源项目保存在工程外的迁移备份中。
            foreach(string name in new[]{"SlimePipeline.asset","SlimeRenderer.asset","SlimePost.asset","Sky.mat","DaySky.mat","UrbanDay.hdr"})AssetDatabase.DeleteAsset(Root+name);
            AssetDatabase.DeleteAsset("Assets/UniversalRenderPipelineGlobalSettings.asset");
            var pipeline=AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(Root+"SlimeHDRP.asset");
            if(pipeline==null){pipeline=ScriptableObject.CreateInstance<HDRenderPipelineAsset>();AssetDatabase.CreateAsset(pipeline,Root+"SlimeHDRP.asset");}
            var settings=pipeline.currentPlatformRenderPipelineSettings;settings.supportSSR=true;settings.supportSSRTransparent=false;settings.supportSSAO=true;
            settings.supportSSGI=false;settings.supportVolumetrics=false;settings.supportVolumetricClouds=false;settings.supportRayTracing=false;settings.supportWater=false;
            settings.supportCustomPass=true;settings.supportDecals=false;settings.supportMotionVectors=true;settings.msaaSampleCount=MSAASamples.None;
            settings.supportedLitShaderMode=RenderPipelineSettings.SupportedLitShaderMode.DeferredOnly;
            pipeline.currentPlatformRenderPipelineSettings=settings;GraphicsSettings.defaultRenderPipeline=pipeline;
            int quality=QualitySettings.GetQualityLevel();for(int i=0;i<QualitySettings.names.Length;i++){QualitySettings.SetQualityLevel(i,false);QualitySettings.renderPipeline=pipeline;}QualitySettings.SetQualityLevel(quality,false);EditorUtility.SetDirty(pipeline);
            // Ensure 是 HDRP 编辑器自身用于填充资源、默认体积与全局设置的初始化入口。
            typeof(HDRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings").GetMethod("Ensure",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{true});
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root+"SlimeEnvironment.asset");
            if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,Root+"SlimeEnvironment.asset");}
            var visual=Component<VisualEnvironment>(profile);visual.skyType.Override((int)SkyType.HDRI);visual.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            if(profile.TryGet<PhysicallyBasedSky>(out var unusedSky)){profile.Remove<PhysicallyBasedSky>();UnityEngine.Object.DestroyImmediate(unusedSky,true);}
            var hdri=Component<HDRISky>(profile);hdri.hdriSky.Override(Daylight(out float skyLux));hdri.skyIntensityMode.Override(SkyIntensityMode.Lux);
            hdri.upperHemisphereLuxValue.Override(skyLux);hdri.desiredLuxValue.Override(18000);hdri.rotation.Override(125);
            var exposure=Component<Exposure>(profile);exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(12.5f);
            Component<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var bloom=Component<Bloom>(profile);bloom.intensity.Override(.12f);bloom.threshold.Override(1.25f);bloom.scatter.Override(.55f);
            var colors=Component<ColorAdjustments>(profile);colors.contrast.Override(5);colors.saturation.Override(0);
            var ao=Component<ScreenSpaceAmbientOcclusion>(profile);ao.intensity.Override(.65f);ao.radius.Override(.65f);ao.temporalAccumulation.Override(false);
            var contact=Component<ContactShadows>(profile);contact.enable.Override(true);contact.length.Override(.15f);contact.maxDistance.Override(30);
            var ssr=Component<ScreenSpaceReflection>(profile);ssr.enabled.Override(true);ssr.enabledTransparent.Override(false);
            Component<MotionBlur>(profile).intensity.Override(0);Component<DepthOfField>(profile).active=false;Component<FilmGrain>(profile).intensity.Override(0);
            EditorUtility.SetDirty(profile);
            foreach(string name in new[]{"Standard","Glow"}){var m=Material(name,name=="Glow"?"HDRP/Unlit":"HDRP/Lit");m.SetFloat("_Smoothness",.25f);HDShaderUtils.ResetMaterialKeywords(m);}
            Material("SlimeGlass","Slime/Volume Glass").SetVector("_Absorption",new Vector4(.78f,.04f,.18f,0));
            Material("Droplet","Slime/Impact Droplet");Material("WorldLabel","Slime/World Label");Material("WetContact","Slime/Wet Contact");
            Surface("Street","Road",.48f,Color.white);Surface("Facade","Brick",.3333f,Color.white);
            AssetDatabase.SaveAssets();
        }
    }
}
