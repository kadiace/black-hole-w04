Shader "Fluid/ParticlePreview"
{
    // 재질 설정
    Properties
    {
        _BaseColor ("Color", Color) = (0.1, 0.6, 1, 1)
        _Radius ("Radius", Range(0.01, 0.5)) = 0.08
        _Opacity ("Opacity", Range(0, 1)) = 1
        [Enum(Off,0,On,1)] _DepthWrite ("Depth Write", Float) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0
        _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionStrength ("Emission Strength", Range(0, 5)) = 0
        _FresnelStrength ("Fresnel Strength", Range(0, 2)) = 0.2
        _FresnelPower ("Fresnel Power", Range(1, 8)) = 4
    }

    // 입자 렌더링
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_DepthWrite]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 위치 버퍼
            StructuredBuffer<float4> _Positions;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Radius;
                float _Opacity;
                float _Smoothness;
                float _Metallic;
                float4 _EmissionColor;
                float _EmissionStrength;
                float _FresnelStrength;
                float _FresnelPower;
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
                float3 positionWS : TEXCOORD1;
            };

            // 정점 배치
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 center = _Positions[input.instanceID].xyz;
                float3 positionWS = center + input.positionOS * (2.0 * _Radius);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = input.normalOS;
                output.positionWS = positionWS;
                return output;
            }

            // 색상 출력
            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(_WorldSpaceCameraPos - input.positionWS);
                Light mainLight = GetMainLight();
                float3 lightDirection = normalize(mainLight.direction);
                float diffuse = saturate(dot(normal, lightDirection));
                float3 halfVector = normalize(view + lightDirection);
                float specular = pow(saturate(dot(normal, halfVector)), lerp(8.0, 128.0, _Smoothness)) * diffuse;
                float3 reflection = lerp(float3(0.04, 0.04, 0.04), _BaseColor.rgb, _Metallic);
                float fresnel = pow(1.0 - saturate(dot(normal, view)), _FresnelPower);
                float3 color = _BaseColor.rgb * (1.0 - _Metallic) * (0.25 + diffuse * mainLight.color);
                color += reflection * specular * mainLight.color;
                color += _BaseColor.rgb * fresnel * _FresnelStrength;
                color += _EmissionColor.rgb * _EmissionStrength;
                return half4(color, _BaseColor.a * _Opacity);
            }
            ENDHLSL
        }
    }
}