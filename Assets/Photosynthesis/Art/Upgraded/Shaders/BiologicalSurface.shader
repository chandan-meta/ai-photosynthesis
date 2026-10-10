Shader "Photosynthesis/Biological Surface"
{
    Properties
    {
        _BaseMap("Albedo",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1)
        _BumpMap("Normal",2D)="bump"{} _BumpScale("Normal strength",Range(0,1))=.3
        _MetallicGlossMap("Smoothness (A)",2D)="white"{} _Smoothness("Smoothness scale",Range(0,1))=1
        _OcclusionMap("AO",2D)="white"{} _ThicknessMap("Thickness",2D)="white"{}
        _Transmission("Thin tissue light",Range(0,.4))=.12
        [HideInInspector] _SrcBlend("Source blend",Float)=1
        [HideInInspector] _DstBlend("Destination blend",Float)=0
        [HideInInspector] _ZWrite("Depth write",Float)=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite] Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_OcclusionMap);SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_ThicknessMap);SAMPLER(sampler_ThicknessMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;half4 _BaseColor;half _BumpScale,_Smoothness,_Transmission,_SrcBlend,_DstBlend,_ZWrite;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;half fog:TEXCOORD4;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes a)
            {
                Varyings o=(Varyings)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(a.positionOS.xyz);VertexNormalInputs n=GetVertexNormalInputs(a.normalOS,a.tangentOS);
                o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=n.normalWS;o.tangentWS=half4(n.tangentWS,a.tangentOS.w*GetOddNegativeScale());
                o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.fog=ComputeFogFactor(p.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 base=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;
                half3 nt=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv),_BumpScale);
                half3 normal=NormalizeNormalPerPixel(TransformTangentToWorld(nt,half3x3(i.tangentWS.xyz,cross(i.normalWS,i.tangentWS.xyz)*i.tangentWS.w,i.normalWS)));
                InputData d=(InputData)0;d.positionWS=i.positionWS;d.normalWS=normal;d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord=TransformWorldToShadowCoord(i.positionWS);d.bakedGI=SampleSH(normal);d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);d.shadowMask=half4(1,1,1,1);
                SurfaceData s=(SurfaceData)0;s.albedo=base.rgb;s.alpha=base.a;s.metallic=0;s.specular=half3(.04,.04,.04);s.normalTS=nt;
                s.smoothness=SAMPLE_TEXTURE2D(_MetallicGlossMap,sampler_MetallicGlossMap,i.uv).a*_Smoothness;s.occlusion=SAMPLE_TEXTURE2D(_OcclusionMap,sampler_OcclusionMap,i.uv).g;
                Light light=GetMainLight(d.shadowCoord);
                half back=saturate(dot(-normal,light.direction));half thickness=SAMPLE_TEXTURE2D(_ThicknessMap,sampler_ThicknessMap,i.uv).r;
                s.emission=base.rgb*light.color*back*thickness*_Transmission*light.shadowAttenuation;
                half4 c=UniversalFragmentPBR(d,s);c.rgb=MixFog(c.rgb,i.fog);c.a=base.a;return c;
            }
            ENDHLSL
        }
    }
}
