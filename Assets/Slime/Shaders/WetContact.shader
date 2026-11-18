Shader "Slime/Wet Contact"
{
    SubShader
    {
        Tags {"RenderPipeline"="HDRenderPipeline"}
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
            TEXTURE2D_X(_SlimeOpaqueColor);
            sampler2D _LiquidWet;sampler3D _LiquidVolume;
            float3 _LiquidMinimum,_LiquidSize,_LiquidResolution;float _LiquidTextureSize;float4 _SlimeSunDirection;
            float density(float3 p)
            {
                float3 uv=(p-_LiquidMinimum)/max(_LiquidSize,.01);if(any(uv<0)||any(uv>1))return 0;
                return saturate(tex3Dlod(_LiquidVolume,float4((uv*(_LiquidResolution-1)+.5)/max(1,_LiquidTextureSize),0)).r);
            }
            float4 Frag(Varyings i):SV_Target
            {
                uint2 pixel=i.positionCS.xy;float2 uv=i.positionCS.xy*_ScreenSize.zw;
                float3 original=SAMPLE_TEXTURE2D_X_LOD(_SlimeOpaqueColor,s_linear_clamp_sampler,uv*_RTHandleScale.xy,0).rgb;
                float depth=LoadCameraDepth(pixel);if(depth==0)return float4(original,1);
                PositionInputs pos=GetPositionInput(pixel,_ScreenSize.zw,depth,UNITY_MATRIX_I_VP,UNITY_MATRIX_V);
                float3 world=GetAbsolutePositionWS(pos.positionWS);
                float wet=abs(world.y)<.06?tex2D(_LiquidWet,world.xz/float2(100,64)+.5).r:0;
                float shade=0;
                [unroll]for(int j=1;j<=7;j++)shade+=density(world+normalize(_SlimeSunDirection.xyz)*(j*.28))*.32;
                float3 transmit=exp(-shade*float3(.55,.14,.28));
                // 近地薄层的绿色透射与聚光只是有界近似，不生成不断运动的假水纹。
                float focus=shade*exp(-shade*1.7);
                return float4(original*lerp(1,.72,saturate(wet))*transmit+original*float3(.02,.24,.07)*focus,1);
            }
            ENDHLSL
        }
    }
}
