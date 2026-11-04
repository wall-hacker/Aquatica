// Crystals growing out of the exit gateway's stone. Dormant (_Active 0) they pass for rock: matte, speckled grey,
// lit by the sun like the arch. Charged (_Active 1) they turn to blue crystal: a cold glow from inside that breathes
// and drifts up the shards, a bright rim on the facets, and thin electric veins crawling over them. Set _Active per
// renderer (MaterialPropertyBlock). Lit by the sun, ambient and the gateway's glow; fogged like everything else.
Shader "Aquatica/GateCrystal"
{
    Properties
    {
        _StoneColor ("Stone Color", Color) = (0.55, 0.56, 0.53, 1)
        _CrystalColor ("Crystal Color", Color) = (0.08, 0.3, 0.45, 1)
        [HDR] _GlowColor ("Glow Color", Color) = (0.35, 1.4, 2.2, 1)
        [HDR] _VeinColor ("Electric Vein Color", Color) = (1.2, 2.6, 3.2, 1)
        _Active ("Active", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "GateCrystal"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "AquaticaGlow.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _StoneColor, _CrystalColor, _GlowColor, _VeinColor;
                float _Active;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * float3(443.897, 441.423, 437.195));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + 1), f.x), f.y), f.z);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 p = input.positionOS;

                Light light = GetMainLight();
                // Wrapped a little so facets turned from the sun aren't black, like the arch's stone
                float wrapped = saturate((dot(normalWS, light.direction) + 0.3) / 1.3);
                half3 lighting = light.color * wrapped + SampleSH(normalWS) * 1.4 + AquaticaGlow(input.positionWS, normalWS);

                // Dormant: speckled stone
                float speckle = Noise(p * 14.0) * 0.6 + Noise(p * 40.0) * 0.4;
                half3 stone = _StoneColor.rgb * (0.7 + 0.5 * speckle) * lighting;

                // Charged: dark blue body, a glow inside that drifts up the shards, bright facet rims
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), 3.0);
                float drift = Noise(float3(p.x * 4.0, p.y * 4.0 - t * 0.6, p.z * 4.0 + t * 0.2));
                float breath = 0.75 + 0.25 * sin(t * 2.1 + p.z * 5.0);
                half3 crystal = _CrystalColor.rgb * lighting
                              + _GlowColor.rgb * (0.25 + 0.55 * drift) * breath
                              + _GlowColor.rgb * fresnel * 0.9;

                // Electric veins: thin bright lines where a moving noise crosses its middle, crawling continuously
                float veinNoise = Noise(p * 9.0 + float3(t * 3.1, -t * 2.3, t * 1.7));
                float vein = 1.0 - smoothstep(0.0, 0.035, abs(veinNoise - 0.5));
                crystal += _VeinColor.rgb * vein;

                half3 col = lerp(stone, crystal, saturate(_Active));
                return half4(MixFog(col, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
