// Seabed props: seaweed and shells. Lit like the seafloor (sun, the same world-space caustics, gradient ambient,
// fog) so they sit in it naturally, plus:
//  - a tint (one grey seaweed texture gives every seaweed colour; shells get their colours the same way)
//  - optional alpha cut-out and two-sided lighting (blades)
//  - optional sway (seaweed): a slow watery sway plus a finer flutter, both growing towards the tip, and a
//    per-instance bend (_Bend, world-space tip offset) that SeabedField drives with a spring when the submarine
//    pushes through. Seaweed meshes are normalised to unit height, so object-space y is the bend weight.
// Supports GPU instancing (SeabedField draws everything with DrawMeshInstanced).
Shader "Aquatica/Seabed"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Toggle(_NORMALMAP)] _UseNormalMap ("Normal Map", Float) = 0
        [NoScaleOffset] [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Cut-out", Float) = 0
        _Cutoff ("Cut-out Threshold", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _Wrap ("Light Wrap (thin surfaces)", Range(0, 1)) = 0

        [Header(Sway)]
        [Toggle(_SWAY)] _UseSway ("Sway (plants)", Float) = 0
        _SwayAmount ("Sway (fraction of height)", Range(0, 0.4)) = 0.08
        _SwaySpeed ("Sway Speed", Float) = 0.9
        _FlutterAmount ("Flutter", Range(0, 0.2)) = 0.03

        [Header(Caustics)]
        _CausticColor ("Caustic Color", Color) = (0.65, 0.95, 1.0, 1)
        _CausticStrength ("Caustic Strength", Range(0, 4)) = 2.4
        _CausticSize ("Caustic Size (m)", Float) = 12
        _CausticSpeed ("Caustic Speed", Float) = 0.5
        _CausticFullDepth ("Caustics Full Strength Above Depth (m)", Float) = 60
        _CausticFadeDepth ("Caustics Vanish At Depth (m)", Float) = 82
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale, _Cutoff, _Wrap;
            float _SwayAmount, _SwaySpeed, _FlutterAmount;
            half4 _CausticColor;
            float _CausticStrength, _CausticSize, _CausticSpeed, _CausticFullDepth, _CausticFadeDepth;
        CBUFFER_END

        UNITY_INSTANCING_BUFFER_START(SeabedProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Bend)
        UNITY_INSTANCING_BUFFER_END(SeabedProps)

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);

        // Set globally by UnderwaterWorld
        float _AquaticaSurfaceHeight;

        // Seaweed: watery sway + flutter growing towards the tip, and the spring-driven bend away from the hull
        float3 DeformWorld(float3 positionOS, float3 positionWS)
        {
        #if defined(_SWAY)
            float3 root = TransformObjectToWorld(float3(0, 0, 0));
            float height = length(float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y));
            float w = saturate(positionOS.y);
            w *= w;
            float t = _Time.y * _SwaySpeed;
            float phase = dot(root.xz, float2(0.21, 0.17));
            float2 sway = float2(sin(t + phase), sin(t * 0.73 + phase * 1.31 + 1.7)) * _SwayAmount
                        + float2(sin(t * 3.1 + positionOS.y * 6.0 + phase), cos(t * 2.7 + positionOS.y * 5.0 + phase)) * _FlutterAmount * w;
            float3 bend = UNITY_ACCESS_INSTANCED_PROP(SeabedProps, _Bend).xyz;
            float3 offset = float3(sway.x, 0, sway.y) * height * w + bend * w;
            // Leaning drops the tip a little so the plant keeps its length instead of stretching
            float lean = saturate(length(offset.xz) / max(height, 0.01));
            offset.y -= height * w * (1.0 - sqrt(saturate(1.0 - lean * lean)));
            positionWS += offset;
        #endif
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "SeabedForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _SWAY

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = DeformWorld(input.positionOS.xyz, TransformObjectToWorld(input.positionOS.xyz));
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = n.normalWS;
                output.tangentWS = float4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Tileable water caustic, identical to the seafloor's so the pattern continues onto the props
            float Caustic(float2 uv, float time)
            {
                const float TAU = 6.28318530718;
                float2 p = frac(uv) * TAU - 250.0;
                float2 i = p;
                float c = 1.0;
                const float inten = 0.005;
                [unroll] for (int n = 0; n < 5; n++)
                {
                    float t = time * (1.0 - (3.5 / float(n + 1)));
                    i = p + float2(cos(t - i.x) + sin(t + i.y), sin(t - i.y) + cos(t + i.x));
                    c += 1.0 / length(float2(p.x / (sin(i.x + t) / inten), p.y / (cos(i.y + t) / inten)));
                }
                c /= 5.0;
                c = 1.17 - pow(abs(c), 1.4);
                return pow(abs(c), 8.0);
            }

            half4 frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            #if defined(_ALPHATEST_ON)
                clip(tex.a - _Cutoff);
            #endif
                half3 albedo = tex.rgb * _BaseColor.rgb;

                float3 normalWS = normalize(input.normalWS);
            #if defined(_NORMALMAP)
                half3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, input.uv), _BumpScale);
                float3 bitangent = input.tangentWS.w * cross(normalWS, input.tangentWS.xyz);
                normalWS = normalize(TransformTangentToWorld(tn, half3x3(input.tangentWS.xyz, bitangent, normalWS)));
            #endif
                normalWS = frontFace ? normalWS : -normalWS;

                float3 positionWS = input.positionWS;
                Light light = GetMainLight(TransformWorldToShadowCoord(positionWS));
                float NdotL = saturate((dot(normalWS, light.direction) + _Wrap) / (1.0 + _Wrap));
                float depth = max(_AquaticaSurfaceHeight - positionWS.y, 0.0);
                float causticFade = 1.0 - smoothstep(_CausticFullDepth, _CausticFadeDepth, depth);
                float2 causticUV = (positionWS.xz - light.direction.xz / max(light.direction.y, 0.2) * positionWS.y * 0.15) / _CausticSize;
                float time = _Time.y * _CausticSpeed;
                float caustic = Caustic(causticUV, time) * 0.6 + Caustic(causticUV * 1.37 + 0.31, time * 0.83) * 0.4;
                half3 causticLight = _CausticColor.rgb * caustic * _CausticStrength * causticFade * saturate(normalWS.y);

                half3 direct = light.color * (light.shadowAttenuation * light.distanceAttenuation) * NdotL;
                half3 color = albedo * (direct * (0.45 + causticLight) + SampleSH(normalWS));


                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull [_Cull]
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _SWAY

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = DeformWorld(input.positionOS.xyz, TransformObjectToWorld(input.positionOS.xyz));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half frag(Varyings input) : SV_Target
            {
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
            #endif
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
