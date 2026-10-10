Shader "Photosynthesis/Clear Water"
{
    Properties
    {
        _BaseColor("Water tint",Color)=(.08,.32,.42,.13)
        _BumpMap("Flow normal",2D)="bump"{} _BumpScale("Ripple strength",Range(0,1))=.18
        _FlowSpeed("Flow speed",Float)=.035 _Fresnel("Edge opacity",Range(0,1))=.24
        _Smoothness("Smoothness",Range(0,1))=.88
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BumpMap_ST;half4 _BaseColor;half _BumpScale,_FlowSpeed,_Fresnel,_Smoothness;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;half fog:TEXCOORD4;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes a)
            {
                Varyings o=(Varyings)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(a.positionOS.xyz);VertexNormalInputs n=GetVertexNormalInputs(a.normalOS,a.tangentOS);
                o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=n.normalWS;o.tangentWS=half4(n.tangentWS,a.tangentOS.w*GetOddNegativeScale());o.uv=TRANSFORM_TEX(a.uv,_BumpMap);o.fog=ComputeFogFactor(p.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 nt=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv+float2(0,_Time.y*_FlowSpeed)),_BumpScale);
                half3 n=NormalizeNormalPerPixel(TransformTangentToWorld(nt,half3x3(i.tangentWS.xyz,cross(i.normalWS,i.tangentWS.xyz)*i.tangentWS.w,i.normalWS)));
                half3 v=GetWorldSpaceNormalizeViewDir(i.positionWS);half f=pow(1-saturate(dot(n,v)),5);
                Light light=GetMainLight();half spec=pow(saturate(dot(n,normalize(light.direction+v))),96)*.4;
                half3 reflection=GlossyEnvironmentReflection(reflect(-v,n),i.positionWS,1-_Smoothness,1,GetNormalizedScreenSpaceUV(i.positionCS));
                half3 color=_BaseColor.rgb*(SampleSH(n)+light.color*saturate(dot(n,light.direction))*.3)+reflection*(.04+.5*f)+light.color*spec;
                return half4(MixFog(color,i.fog),saturate(_BaseColor.a+f*_Fresnel));
            }
            ENDHLSL
        }
    }
}
