Shader "Fluid/ParticlePreview"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.1, 0.6, 1, 1)
        _Radius ("Radius", Range(0.01, 0.2)) = 0.08
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float4> _Positions;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Radius;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 center = _Positions[input.instanceID].xyz;
                float3 positionWS = center + input.positionOS * (2.0 * _Radius);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = input.normalOS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float light = saturate(dot(normalize(input.normalWS), normalize(float3(0.4, 0.8, -0.5))));
                return half4(_BaseColor.rgb * (0.35 + 0.65 * light), _BaseColor.a);
            }
            ENDHLSL
        }
    }
}