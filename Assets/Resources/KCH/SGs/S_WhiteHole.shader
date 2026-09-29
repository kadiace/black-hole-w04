Shader "SG_WhiteHole"
{
    Properties
    {
        _ScreenSpaceScale("Screen Space Scale", Float) = 2
        Vector1_0bb6c794ceb5476bbe1601bd36eb7262("_DistortionExponent", Range(1, 16)) = 4
        _SpherePercentage("_SpherePercentage", Range(0, 1)) = 0.25
        Vector1_e1206881e1244ef4a008548fa38caa6a("_OuterGlowMultiplier", Float) = 1
        Vector1_486f1fcc37a949ffb726a117eab0987a("_OuterGlowExponent", Float) = 4
        Color_f649559c5a534a89a4820a4d0c46d676("_OuterGlowTint", Color) = (1, 1, 1, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "UniversalMaterialType" = "Unlit"
        }

        Pass
        {
            Name "Universal Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZTest LEqual
            ZWrite Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ScreenSpaceScale;
                float Vector1_0bb6c794ceb5476bbe1601bd36eb7262;
                float _SpherePercentage;
                float Vector1_e1206881e1244ef4a008548fa38caa6a;
                float Vector1_486f1fcc37a949ffb726a117eab0987a;
                float4 Color_f649559c5a534a89a4820a4d0c46d676;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewDirWS  : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.viewDirWS = GetWorldSpaceNormalizeViewDir(pos.positionWS);
                OUT.screenPos = ComputeScreenPos(pos.positionCS);

                return OUT;
            }

            // Exact equivalent of the supplied SF_Raycast.hlsl.
            // IMPORTANT: RayDirection is the Shader Graph View Direction,
            // i.e. from the surface toward the camera. The original function
            // deliberately uses -RayDirection as the forward ray direction.
            void Raycast_float(
                float3 RayOrigin,
                float3 RayDirection,
                float3 SphereOrigin,
                float SphereSize,
                out float Hit,
                out float3 HitPosition,
                out float3 HitNormal)
            {
                HitPosition = float3(0.0, 0.0, 0.0);
                HitNormal = float3(0.0, 0.0, 0.0);

                float t = 0.0f;
                float3 L = SphereOrigin - RayOrigin;
                float tca = dot(L, -RayDirection);

                if (tca < 0.0)
                {
                    Hit = 0.0;
                    return;
                }

                float d2 = dot(L, L) - tca * tca;
                float radius2 = SphereSize * SphereSize;

                if (d2 > radius2)
                {
                    Hit = 0.0;
                    return;
                }

                float thc = sqrt(radius2 - d2);
                t = tca - thc;

                Hit = 1.0;
                HitPosition = RayOrigin - RayDirection * t;
                HitNormal = normalize(HitPosition - SphereOrigin);
            }

            // Exact equivalent of GetScreenPosition_float() in SF_Raycast.hlsl.
            void GetScreenPosition(
                float3 Position,
                out float2 ScreenPosition,
                out float2 ScreenPositionAspectRatio)
            {
                // Unity 6 / current URP exposes ComputeScreenPos(float4) only.
                // The URP implementation itself applies _ProjectionParams.x.
                float4 screen = ComputeScreenPos(
                    TransformWorldToHClip(Position)
                );

                ScreenPosition = screen.xy / abs(screen.w);

                float aspectRatio = _ScreenParams.y / _ScreenParams.x;
                ScreenPositionAspectRatio =
                    float2(ScreenPosition.x, ScreenPosition.y * aspectRatio);
            }

            // Exact equivalent of MirrorUVCoordinates_float().
            float2 MirrorUVCoordinates(float2 UVs)
            {
                float2 NewUVs;

                if (UVs.x < 0.0 || UVs.x > 1.0)
                    NewUVs.x =
                        1.0 - abs((UVs.x - 2.0 * floor(UVs.x / 2.0)) - 1.0);
                else
                    NewUVs.x = UVs.x;

                if (UVs.y < 0.0 || UVs.y > 1.0)
                    NewUVs.y =
                        1.0 - abs((UVs.y - 2.0 * floor(UVs.y / 2.0)) - 1.0);
                else
                    NewUVs.y = UVs.y;

                return NewUVs;
            }

            float3 ObjectScaleWS()
            {
                return float3(
                    length(float3(UNITY_MATRIX_M[0].x,
                                  UNITY_MATRIX_M[1].x,
                                  UNITY_MATRIX_M[2].x)),
                    length(float3(UNITY_MATRIX_M[0].y,
                                  UNITY_MATRIX_M[1].y,
                                  UNITY_MATRIX_M[2].y)),
                    length(float3(UNITY_MATRIX_M[0].z,
                                  UNITY_MATRIX_M[1].z,
                                  UNITY_MATRIX_M[2].z))
                );
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Shader Graph World Space Position / View Direction equivalents.
                float3 objectPositionWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 cameraPositionWS = _WorldSpaceCameraPos;

                float3 worldNormal = normalize(IN.normalWS);
                float3 worldViewDirection = normalize(IN.viewDirWS);

                // Exact equivalent of:
                // float4(IN.ScreenPosition.xy / IN.ScreenPosition.w, 0, 0)
                float4 screenPosition =
                    float4(IN.screenPos.xy / IN.screenPos.w, 0.0, 0.0);

                // ---------------------------------------------------------
                // Distortion direction
                // ---------------------------------------------------------
                float2 objectScreenPosition;
                float2 objectScreenPositionAspect;

                float2 fragmentScreenPosition;
                float2 fragmentScreenPositionAspect;

                GetScreenPosition(
                    objectPositionWS,
                    objectScreenPosition,
                    objectScreenPositionAspect
                );

                GetScreenPosition(
                    IN.positionWS,
                    fragmentScreenPosition,
                    fragmentScreenPositionAspect
                );

                float2 distortionDirection =
                    normalize(
                        objectScreenPositionAspect -
                        fragmentScreenPositionAspect
                    );

                // ---------------------------------------------------------
                // Screen-space scale
                // ---------------------------------------------------------
                float halfFovRadians = radians(60.0 * 0.5);
                float tangentHalfFov = tan(halfFovRadians);

                float cameraDistance =
                    distance(objectPositionWS, cameraPositionWS);

                float3 objectScaleWS = ObjectScaleWS();

                float3 screenScale =
                    objectScaleWS /
                    (
                        tangentHalfFov *
                        2.0 *
                        cameraDistance
                    );

                screenScale *= _ScreenSpaceScale;

                float2 distortion =
                    distortionDirection * screenScale.xy;

                // ---------------------------------------------------------
                // Angular distortion curve
                // ---------------------------------------------------------
                float3 cameraToObject =
                    normalize(cameraPositionWS - objectPositionWS);

                float normalCameraDot =
                    saturate(dot(worldNormal, cameraToObject));

                float angle =
                    acos(normalCameraDot);

                float angleNormalized =
                    angle / 1.57075;

                float oneMinusAngle =
                    1.0 - angleNormalized;

                float spherePercentage =
                    _SpherePercentage;

                float denominator =
                    1.0 - spherePercentage;

                float distortionInput =
                    oneMinusAngle / denominator;

                float distortionCurve =
                    pow(
                        distortionInput,
                        Vector1_0bb6c794ceb5476bbe1601bd36eb7262
                    );

                // ---------------------------------------------------------
                // Facing attenuation
                //
                // Exact graph:
                //   (-WorldSpaceViewDirection)
                //   dot
                //   normalize(ObjectPosition - CameraPosition)
                //   clamp 0..1
                // ---------------------------------------------------------
                float3 negativeViewDirection =
                    worldViewDirection * -1.0;

                float3 objectToCameraDirection =
                    normalize(objectPositionWS - cameraPositionWS);

                float facing =
                    clamp(
                        dot(
                            negativeViewDirection,
                            objectToCameraDirection
                        ),
                        0.0,
                        1.0
                    );

                float distortionAmount =
                    distortionCurve * facing;

                float2 distortedUV =
                    screenPosition.xy +
                    distortion * distortionAmount;

                // Exact custom-node mirror behavior from SF_Raycast.hlsl.
                distortedUV =
                    MirrorUVCoordinates(distortedUV);

                // ---------------------------------------------------------
                // Scene color
                //
                // Unity 6 URP replacement for the old _GrabbedTexture.
                // Enable URP Asset/Camera -> Opaque Texture.
                // ---------------------------------------------------------
                half4 sceneColor =
                    half4(SampleSceneColor(distortedUV), 1.0h);

                // ---------------------------------------------------------
                // White-hole sphere raycast
                // ---------------------------------------------------------
                float sphereRadius =
                    _SpherePercentage * objectScaleWS.x;

                float hit;
                float3 hitPosition;
                float3 hitNormal;

                // IMPORTANT:
                // worldViewDirection is surface -> camera, exactly as the
                // original Shader Graph supplied to Raycast_float().
                Raycast_float(
                    cameraPositionWS,
                    worldViewDirection,
                    objectPositionWS,
                    sphereRadius,
                    hit,
                    hitPosition,
                    hitNormal
                );

                // ---------------------------------------------------------
                // Outer glow / Fresnel
                // ---------------------------------------------------------
                float fresnel =
                    pow(
                        1.0 -
                        saturate(
                            dot(
                                normalize(hitNormal),
                                normalize(worldViewDirection)
                            )
                        ),
                        Vector1_486f1fcc37a949ffb726a117eab0987a
                    );

                float glow =
                    fresnel *
                    Vector1_e1206881e1244ef4a008548fa38caa6a;

                float4 glowColor =
                    Color_f649559c5a534a89a4820a4d0c46d676 *
                    glow;

                // Exact graph:
                // Add(Hit, Glow)
                float4 whiteHoleColor =
                    float4(hit, hit, hit, hit) +
                    glowColor;

                // Exact graph Lerp:
                // Lerp(SceneColor, WhiteHoleColor, Hit)
                float4 result =
                    lerp(
                        sceneColor,
                        whiteHoleColor,
                        float4(hit, hit, hit, hit)
                    );

                return half4(result.xyz, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }

            Cull Back
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthNormalsVert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.normalWS = nrm.normalWS;

                return OUT;
            }

            half4 DepthNormalsFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 normalWS = normalize(IN.normalWS);
                return half4(normalWS * 0.5h + 0.5h, 0.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZTest LEqual
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // URP supplies these globals to the ShadowCaster pass.
            // They must be declared explicitly when the pass is written inline.
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float3 biasedPositionWS =
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS);

                OUT.positionCS = TransformWorldToHClip(biasedPositionWS);

                #if UNITY_REVERSED_Z
                    OUT.positionCS.z =
                        min(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z =
                        max(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
