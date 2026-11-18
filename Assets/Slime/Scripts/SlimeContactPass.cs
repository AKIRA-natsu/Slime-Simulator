using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace SlimeLab
{
    // 在 HDRP 生成折射颜色金字塔前写入液体接触阴影与湿痕，折射也能看到接触关系。
    public sealed class SlimeContactPass:CustomPass
    {
        Material material;RTHandle color;
        protected override void Setup(ScriptableRenderContext context,CommandBuffer cmd)
        {
            material=new Material(Resources.Load<Material>("WetContact"));
            color=RTHandles.Alloc(Vector2.one,colorFormat:UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,dimension:TextureXR.dimension,slices:TextureXR.slices,useDynamicScale:true,name:"Slime opaque color");
        }
        protected override void Execute(CustomPassContext ctx)
        {
            HDUtils.BlitCameraTexture(ctx.cmd,ctx.cameraColorBuffer,color);
            ctx.cmd.SetGlobalTexture("_SlimeOpaqueColor",color);
            CoreUtils.DrawFullScreen(ctx.cmd,material,shaderPassId:0);
        }
        protected override void Cleanup(){CoreUtils.Destroy(material);color?.Release();}
    }
}
