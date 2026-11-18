#ifndef SLIME_HDRP
#define SLIME_HDRP
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
float4 _SlimeSunDirection,_SlimeSunColor;
float4 SlimeClip(float3 world){return TransformWorldToHClip(GetCameraRelativePositionWS(world));}
float4 SlimeView(float3 world){return mul(UNITY_MATRIX_V,float4(GetCameraRelativePositionWS(world),1));}
float4 SlimeScreen(float4 clip){return float4(clip.xy*float2(.5,.5*_ProjectionParams.x)+clip.w*.5,clip.zw);}
float SampleSceneDepth(float2 uv){return LoadCameraDepth(uint2(clamp(uv,0,1-_ScreenSize.zw)*_ScreenSize.xy));}
float3 SampleSceneColor(float2 uv){return SampleCameraColor(clamp(uv,0,1-_ScreenSize.zw),0);}
#endif
