Shader "Slime/World Label"
{
    Properties{_MainTex("Font atlas",2D)="white"{}}
    SubShader
    {
        Tags{"Queue"="Transparent+50" "RenderType"="Transparent" "RenderPipeline"="HDRenderPipeline"}
        Pass
        {
            Tags{"LightMode"="ForwardOnly"}
            ZTest LEqual ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "SlimeHDRP.hlsl"
            sampler2D _MainTex;
            struct Attributes{float3 vertex:POSITION;float2 texcoord:TEXCOORD0;float4 color:COLOR;};
            struct v2f{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            v2f vert(Attributes v){v2f o;o.pos=TransformObjectToHClip(v.vertex);o.uv=v.texcoord;o.color=v.color;return o;}
            float4 frag(v2f i):SV_Target{return float4(i.color.rgb,tex2D(_MainTex,i.uv).a*i.color.a);}
            ENDHLSL
        }
    }
}
