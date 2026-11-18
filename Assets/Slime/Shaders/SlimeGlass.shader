Shader "Slime/Volume Glass"
{
    Properties { _Volume("Liquid density",3D)="white"{} _Environment("Environment",Cube)=""{} _Absorption("Absorption / m",Vector)=(.78,.04,.18,0) }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="HDRenderPipeline"}

        Pass
        {
            Tags {"LightMode"="ForwardOnly"}
            ZWrite On Cull Back ZTest Always
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
#include "SlimeHDRP.hlsl"
            samplerCUBE _Environment;
            sampler3D _Volume;float _VolumeTextureSize;float3 _VolumeMin,_VolumeSize,_VolumeResolution,_Absorption;
            float3 _EyeLeft,_EyeRight,_EyeForward;float4 _Bubbles[96];float _SlimeDebug,_EyeScale;int _BubbleCount;
            struct v2f {float4 pos:SV_POSITION;float3 world:TEXCOORD0;};
            struct result {float4 color:SV_Target;float depth:SV_Depth;};
            struct Attributes {float4 vertex:POSITION;};
            v2f vert(Attributes v){v2f o;o.pos=TransformObjectToHClip(v.vertex.xyz);o.world=GetAbsolutePositionWS(TransformObjectToWorld(v.vertex.xyz));return o;}
            float field(float3 p){float3 uv=(p-_VolumeMin)/_VolumeSize;if(any(uv<0)||any(uv>1))return -.85;uv=(uv*(_VolumeResolution-1)+.5)/_VolumeTextureSize;return tex3Dlod(_Volume,float4(uv,0)).r;}
            float densityField(float3 p){float3 uv=(p-_VolumeMin)/_VolumeSize;if(any(uv<0)||any(uv>1))return -.85;uv=(uv*(_VolumeResolution-1)+.5)/_VolumeTextureSize;return tex3Dlod(_Volume,float4(uv,0)).g;}
            float3 normalAt(float3 p)
            {
                float e=.065;
                // 自由表面使用未裁切密度的法线，避免地面裁切给薄边制造一圈镜面折痕。
                if(densityField(p)<.15)return normalize(float3(densityField(p-float3(e,0,0))-densityField(p+float3(e,0,0)),densityField(p-float3(0,e,0))-densityField(p+float3(0,e,0)),densityField(p-float3(0,0,e))-densityField(p+float3(0,0,e)))+1e-8);
                return normalize(float3(field(p-float3(e,0,0))-field(p+float3(e,0,0)),field(p-float3(0,e,0))-field(p+float3(0,e,0)),field(p-float3(0,0,e))-field(p+float3(0,0,e)))+1e-8);
            }
            bool boundsRay(float3 ro,float3 rd,out float lo,out float hi)
            {float3 inv=1/(rd+1e-8),a=(_VolumeMin-ro)*inv,b=(_VolumeMin+_VolumeSize-ro)*inv;float3 l=min(a,b),h=max(a,b);lo=max(0,max(l.x,max(l.y,l.z)));hi=min(h.x,min(h.y,h.z));return hi>lo;}
            float surfaceHit(float3 ro,float3 rd,float start,float end,bool leaving)
            {
                float t=start,prior=t;
                [loop]for(int k=0;k<112;k++)
                {
                    float f=field(ro+rd*t);
                    if(leaving?f<0:f>0){float a=prior,b=t;[unroll]for(int j=0;j<8;j++){float m=(a+b)*.5;bool hit=leaving?field(ro+rd*m)<0:field(ro+rd*m)>0;if(hit)b=m;else a=m;}return (a+b)*.5;}
                    prior=t;t+=clamp(abs(f)*.055,.012,.075);if(t>end)break;
                }
                return -1;
            }
            float3 environment(float3 rd){return texCUBElod(_Environment,float4(rd,.35)).rgb;}
            float3 sceneRay(float3 start,float3 direction)
            {
                float3 fallback=environment(direction);
                if(direction.y<-.001)
                {
                    float ground=max(.0001,(.003-start.y)/direction.y);
                    if(ground>0&&ground<80)
                    {
                        float3 p=start+direction*ground;float4 cp=SlimeClip(p);float4 screen=SlimeScreen(cp);float2 uv=screen.xy/screen.w;
                        if(cp.w>0&&all(uv>.002)&&all(uv<.998))
                        {
                            float opaque=LinearEyeDepth(SampleSceneDepth(uv),_ZBufferParams),d=-SlimeView(p).z;
                            if(abs(opaque-d)<.1){float4 grab=SlimeScreen(cp);return SampleSceneColor(grab.xy/grab.w);}
                        }
                    }
                }
                float t=.055;
                [loop]for(int k=0;k<30;k++)
                {
                    float3 p=start+direction*t;float4 cp=SlimeClip(p);if(cp.w<=0)break;
                    float4 screen=SlimeScreen(cp);float2 uv=screen.xy/screen.w;if(any(uv<.002)||any(uv>.998))break;
                    float opaque=LinearEyeDepth(SampleSceneDepth(uv),_ZBufferParams);float depth=-SlimeView(p).z;
                    if(depth>opaque+.012&&depth<opaque+.6+t*.025){float4 grab=SlimeScreen(cp);return SampleSceneColor(grab.xy/grab.w);}
                    t+=.10+t*.12;
                }
                return fallback;
            }
            float3 eyeLight(float3 eye,float3 start,float3 rd,float lengthInside)
            {
                float3 delta=eye-start;float depth=dot(delta,rd);if(depth<0||depth>lengthInside||field(eye)<0)return 0;
                float2 local=mul((float3x3)UNITY_MATRIX_V,delta-rd*depth).xy;
                float r=length(local/(float2(.085,.13)*max(.5,_EyeScale)));float glow=(1-smoothstep(.8,1.12,r))*2.5+exp(-r*r)*.06;
                return float3(1,.96,.67)*glow*exp(-_Absorption*depth*.5)*smoothstep(-.1,.4,dot(_EyeForward,-rd));
            }
            float3 bubbleLight(float3 start,float3 rd,float lengthInside)
            {
                float3 color=0;
                [loop]for(int j=0;j<_BubbleCount;j++)
                {
                    float3 delta=_Bubbles[j].xyz-start;float t=dot(delta,rd);if(t<.02||t>lengthInside)continue;
                    float r=_Bubbles[j].w;float3 perpendicular=delta-rd*t;float q=dot(perpendicular,perpendicular)/(r*r);
                    if(q<1.2&&field(_Bubbles[j].xyz)>0){float ring=pow(saturate(q),3)*(1-smoothstep(.8,1.2,q));float glint=pow(saturate(dot(normalize(perpendicular+float3(0,r*.3,0)),normalize(_SlimeSunDirection.xyz))),12);color+=(ring*.9+glint*1.4)*float3(.65,.95,1)*exp(-_Absorption*t);}
                }
                return color;
            }
            result frag(v2f i)
            {
                float3 ro=_WorldSpaceCameraPos,rd=normalize(i.world-ro);float near,far;clip(boundsRay(ro,rd,near,far)?1:-1);
                float entry=surfaceHit(ro,rd,near+.001,far,false);clip(entry);
                float3 p=ro+rd*entry,n=normalAt(p);float4 cp=SlimeClip(p),screen=SlimeScreen(cp);
                float opaque=LinearEyeDepth(SampleSceneDepth(screen.xy/screen.w),_ZBufferParams);float eyeDepth=-SlimeView(p).z;clip(opaque-eyeDepth+.002);
                float3 inner=refract(rd,n,1/1.333);float exitLength=surfaceHit(p,inner,.025,7,true);exitLength=max(.025,exitLength);
                float3 exitPoint=p+inner*exitLength,outgoing=inner;
                float3 internalLight=eyeLight(_EyeLeft,p,inner,exitLength)+eyeLight(_EyeRight,p,inner,exitLength)+bubbleLight(p,inner,exitLength);
                // 未裁切密度在边界内仍为正时，光线撞到的是实体；地面和平台均不能误算为空气界面。
                if(exitPoint.y>.03&&densityField(exitPoint)<.15)
                {
                    float3 exitNormal=normalAt(exitPoint);outgoing=refract(inner,-exitNormal,1.333);
                    if(dot(outgoing,outgoing)<.001)
                    {
                        float3 bounce=reflect(inner,-exitNormal);float second=surfaceHit(exitPoint,bounce,.012,6,true);
                        if(second>0){exitPoint+=bounce*second;exitLength+=second;outgoing=exitPoint.y<.03||densityField(exitPoint)>.15?bounce:refract(bounce,-normalAt(exitPoint),1.333);}
                        if(dot(outgoing,outgoing)<.001)outgoing=bounce;
                    }
                }
                float3 through=sceneRay(exitPoint+outgoing*.025,outgoing);float3 scattering=float3(.002,.008,.004),extinction=_Absorption+scattering;
                float3 transmission=exp(-extinction*exitLength);
                float fresnel=.0204+.9796*pow(1-saturate(dot(n,-rd)),5);
                float3 reflection=sceneRay(p+n*.025,reflect(rd,n));float3 light=normalize(_SlimeSunDirection.xyz);float highlight=saturate(dot(n,normalize(light-rd)));float spec=pow(highlight,700)*1.15+pow(highlight,85)*.08;
                
                float phase=.84/pow(max(.12,1.16-.8*dot(-inner,light)),1.5);
                float3 inScatter=scattering/extinction*(1-transmission)*(float3(.28,.48,.42)+(_SlimeSunColor.rgb*GetCurrentExposureMultiplier())*phase*.65);
                float3 color=lerp(through*transmission+inScatter,reflection,fresnel)+internalLight+spec*(_SlimeSunColor.rgb*GetCurrentExposureMultiplier());
                if(_SlimeDebug==1)color=n*.5+.5;
                if(_SlimeDebug==2)color=reflection;
                if(_SlimeDebug==3)color=through;
                if(_SlimeDebug==4)color=exitLength/3;
                if(_SlimeDebug==5)color=environment(outgoing);
                result o;o.color=float4(color,1);o.depth=cp.z/cp.w;return o;
            }
            ENDHLSL
        }
    }
}




