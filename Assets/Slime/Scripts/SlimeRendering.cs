using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace SlimeLab
{
    public static class SlimeRendering
    {
        static void Setting(HDAdditionalCameraData camera,FrameSettingsField field,bool value)
        {camera.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field]=true;camera.renderingPathCustomFrameSettings.SetEnabled(field,value);}
        public static void Configure(Camera camera,bool postProcessing)
        {
            var data=camera.gameObject.AddComponent<HDAdditionalCameraData>();data.customRenderingSettings=true;data.clearColorMode=HDAdditionalCameraData.ClearColorMode.Sky;
            data.antialiasing=postProcessing?HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing:HDAdditionalCameraData.AntialiasingMode.None;
            Setting(data,FrameSettingsField.Postprocess,postProcessing);Setting(data,FrameSettingsField.ExposureControl,true);
            Setting(data,FrameSettingsField.Refraction,true);Setting(data,FrameSettingsField.CustomPass,true);
            if(!postProcessing){Setting(data,FrameSettingsField.SSR,false);Setting(data,FrameSettingsField.SSAO,false);Setting(data,FrameSettingsField.ShadowMaps,false);}
        }
        public static void Render(Camera camera)=>RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=camera.targetTexture});
        public static bool CaptureEnvironment(Camera camera,RenderTexture cube,int startFace=0,int count=6)
        {
            if(RenderPipelineManager.currentPipeline==null)return false;
            Quaternion prior=camera.transform.rotation;float fov=camera.fieldOfView,aspect=camera.aspect;camera.fieldOfView=90;camera.aspect=1;
            Vector3[] directions={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            Vector3[] up={Vector3.down,Vector3.down,Vector3.forward,Vector3.back,Vector3.down,Vector3.down};
            for(int face=startFace;face<startFace+count;face++)
            {camera.transform.rotation=Quaternion.LookRotation(directions[face],up[face]);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=cube,face=(CubemapFace)face});}
            camera.transform.rotation=prior;camera.fieldOfView=fov;camera.aspect=aspect;if(startFace+count==6)cube.GenerateMips();return true;
        }
        public static void AddPostProcessing()
        {
            var volume=new GameObject("HDRP daylight and optics").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=Resources.Load<VolumeProfile>("SlimeEnvironment");
            var pass=new GameObject("Liquid contact and wet ground").AddComponent<CustomPassVolume>();pass.isGlobal=true;pass.injectionPoint=CustomPassInjectionPoint.BeforePreRefraction;
            pass.customPasses.Add(new SlimeContactPass());
        }
        public static void ConfigureSun(Light sun,Light fill)
        {
            var data=sun.gameObject.AddComponent<HDAdditionalLightData>();data.angularDiameter=.65f;data.SetShadowResolution(2048);sun.intensity=55000;
            fill.gameObject.AddComponent<HDAdditionalLightData>();fill.intensity=450;
            Shader.SetGlobalVector("_SlimeSunDirection",-sun.transform.forward);Shader.SetGlobalColor("_SlimeSunColor",sun.color.linear*(sun.intensity/Mathf.PI));
        }
    }
}
