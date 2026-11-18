Shader "Slime/Impact Droplet"
{
    Properties {_Environment("Environment",Cube)=""{}}
    SubShader
    {
        Tags {"Queue"="Transparent+30" "RenderType"="Transparent" "RenderPipeline"="HDRenderPipeline"}

        Pass
        {
            Tags {"LightMode"="ForwardOnly"}
            Cull Off ZWrite On Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
#include "SlimeHDRP.hlsl"
            samplerCUBE _Environment;
            struct v2f{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 screen:TEXCOORD1;float3 world:TEXCOORD2;};
            struct Attributes {float4 vertex:POSITION;float2 texcoord:TEXCOORD0;};
            v2f vert(Attributes v){v2f o;o.pos=TransformObjectToHClip(v.vertex.xyz);o.uv=v.texcoord.xy;o.screen=SlimeScreen(o.pos);o.world=GetAbsolutePositionWS(TransformObjectToWorld(v.vertex.xyz));return o;}
            float4 frag(v2f i):SV_Target
            {
                float2 q=i.uv*2-1;float r2=dot(q,q);clip(1-r2);float z=sqrt(saturate(1-r2));
                float3 n=normalize(mul((float3x3)UNITY_MATRIX_I_V,float3(q,z))),v=normalize(_WorldSpaceCameraPos-i.world);
                float2 uv=i.screen.xy/i.screen.w;float3 behind=SampleSceneColor(uv+q*.003)*float3(.90,1,.97);
                float3 reflection=texCUBElod(_Environment,float4(reflect(-v,n),.8)).rgb;
                float fresnel=.0204+.9796*pow(1-z,5),glint=pow(saturate(dot(n,normalize(normalize(_SlimeSunDirection.xyz)+v))),180)*1.5;
                return float4(lerp(behind,reflection,fresnel)+glint*(_SlimeSunColor.rgb*GetCurrentExposureMultiplier()),saturate((1-r2)/max(fwidth(r2),.02)));
            }
            ENDHLSL
        }
    }
}

