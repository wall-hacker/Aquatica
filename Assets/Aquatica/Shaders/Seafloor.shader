// Procedural seafloor surface. Four layers are painted from the per-vertex masks written by Seafloor.cs
// (R = rock, G = silt, B = gravel, A = occlusion) plus the surface slope: steep ground always turns to rock.
// Layers are blended by their height maps (stored in the albedo alpha) so pebbles and rocks poke through
// sand instead of cross-fading. Rock is projected triplanar so cliffs don't stretch. Animated caustics are
// projected along the sun direction and fade out between two depths below the water surface.
Shader "Aquatica/Seafloor"
{
    Properties
    {
        [Header(Sand)]
        _SandMap ("Sand (RGB albedo, A height)", 2D) = "white" {}
        [Normal][NoScaleOffset] _SandNormal ("Sand Normal", 2D) = "bump" {}
        _SandTiling ("Sand Tile Size (m)", Float) = 5

        [Header(Silt)]
        _SiltMap ("Silt (RGB albedo, A height)", 2D) = "white" {}
        [Normal][NoScaleOffset] _SiltNormal ("Silt Normal", 2D) = "bump" {}
        _SiltTiling ("Silt Tile Size (m)", Float) = 3

        [Header(Gravel)]
        _GravelMap ("Gravel (RGB albedo, A height)", 2D) = "white" {}
        [Normal][NoScaleOffset] _GravelNormal ("Gravel Normal", 2D) = "bump" {}
        _GravelTiling ("Gravel Tile Size (m)", Float) = 2.5

        [Header(Rock)]
        _RockMap ("Rock (RGB albedo, A height)", 2D) = "white" {}
        [Normal][NoScaleOffset] _RockNormal ("Rock Normal", 2D) = "bump" {}
        _RockTiling ("Rock Tile Size (m)", Float) = 6
        _RockSlope ("Rock Slope (start, end)", Vector) = (0.3, 0.55, 0, 0)

        [Header(Surface)]
        _Tint ("Albedo Tint (water absorbs red)", Color) = (0.78, 0.92, 1.0, 1)
        _BlendDepth ("Height Blend Softness", Range(0.01, 1)) = 0.2
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1

        [Header(Caustics)]
        _CausticColor ("Caustic Color", Color) = (0.65, 0.95, 1.0, 1)
        _CausticStrength ("Caustic Strength", Range(0, 4)) = 1.6
        _CausticSize ("Caustic Size (m)", Float) = 7
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
            float4 _SandMap_ST, _SiltMap_ST, _GravelMap_ST, _RockMap_ST;
            float _SandTiling, _SiltTiling, _GravelTiling, _RockTiling;
            float4 _RockSlope;
            half4 _Tint;
            float _BlendDepth;
            float _NormalStrength;
            half4 _CausticColor;
            float _CausticStrength, _CausticSize, _CausticSpeed, _CausticFullDepth, _CausticFadeDepth;
        CBUFFER_END

        // Set globally by UnderwaterWorld
        float _AquaticaSurfaceHeight;
        ENDHLSL

        Pass
        {
            Name "SeafloorForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_SandMap);   SAMPLER(sampler_SandMap);
            TEXTURE2D(_SandNormal);
            TEXTURE2D(_SiltMap);
            TEXTURE2D(_SiltNormal);
            TEXTURE2D(_GravelMap);
            TEXTURE2D(_GravelNormal);
            TEXTURE2D(_RockMap);
            TEXTURE2D(_RockNormal);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 masks : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.masks = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            // Tileable water caustic (after Dave Hoskins' / joltz0r's classic): bright lines where waves focus light
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

            half3 UnpackScaled(TEXTURE2D_PARAM(tex, samp), float2 uv)
            {
                half3 n = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uv));
                n.xy *= _NormalStrength;
                return n;
            }

            // Whiteout blend of a tangent normal from a top-down (xz) projection onto the surface normal
            float3 TopDownToWorld(half3 tn, float3 n)
            {
                tn = half3(tn.xy + n.xz, abs(tn.z) * n.y);
                return tn.xzy;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;
                float3 n = normalize(input.normalWS);
                half4 m = input.masks;

                // Layer weights: masks from the generator, steep slopes always rock
                float rockSlope = smoothstep(_RockSlope.x, _RockSlope.y, 1.0 - n.y);
                float wRock = saturate(max(m.r, rockSlope));
                float wSilt = m.g * (1.0 - wRock);
                float wGravel = m.b * (1.0 - wRock);
                float wSand = saturate(1.0 - wRock - wSilt - wGravel);

                float2 xz = positionWS.xz;

                // Sand, with a second rotated, larger sample mixed in by macro noise to hide tiling
                float2 sandUV = xz / _SandTiling;
                half4 sand = SAMPLE_TEXTURE2D(_SandMap, sampler_SandMap, sandUV);
                float2 rotated = float2(xz.x * 0.8 - xz.y * 0.6, xz.x * 0.6 + xz.y * 0.8);
                half4 sandLarge = SAMPLE_TEXTURE2D(_SandMap, sampler_SandMap, rotated / (_SandTiling * 2.7) + 0.37);
                sand = lerp(sand, sandLarge, smoothstep(0.35, 0.65, ValueNoise(xz * 0.04)) * 0.65);
                half3 sandN = UnpackScaled(TEXTURE2D_ARGS(_SandNormal, sampler_SandMap), sandUV);

                float2 siltUV = xz / _SiltTiling;
                half4 silt = SAMPLE_TEXTURE2D(_SiltMap, sampler_SandMap, siltUV);
                half3 siltN = UnpackScaled(TEXTURE2D_ARGS(_SiltNormal, sampler_SandMap), siltUV);

                float2 gravelUV = xz / _GravelTiling;
                half4 gravel = SAMPLE_TEXTURE2D(_GravelMap, sampler_SandMap, gravelUV);
                half3 gravelN = UnpackScaled(TEXTURE2D_ARGS(_GravelNormal, sampler_SandMap), gravelUV);

                // Rock: triplanar projection with whiteout normal blending
                float3 bw = pow(abs(n), 4.0);
                bw /= (bw.x + bw.y + bw.z);
                float3 rp = positionWS / _RockTiling;
                half4 rockX = SAMPLE_TEXTURE2D(_RockMap, sampler_SandMap, rp.zy);
                half4 rockY = SAMPLE_TEXTURE2D(_RockMap, sampler_SandMap, rp.xz);
                half4 rockZ = SAMPLE_TEXTURE2D(_RockMap, sampler_SandMap, rp.xy);
                half4 rock = rockX * bw.x + rockY * bw.y + rockZ * bw.z;
                half3 tnX = UnpackScaled(TEXTURE2D_ARGS(_RockNormal, sampler_SandMap), rp.zy);
                half3 tnY = UnpackScaled(TEXTURE2D_ARGS(_RockNormal, sampler_SandMap), rp.xz);
                half3 tnZ = UnpackScaled(TEXTURE2D_ARGS(_RockNormal, sampler_SandMap), rp.xy);
                tnX = half3(tnX.xy + n.zy, abs(tnX.z) * n.x);
                tnY = half3(tnY.xy + n.xz, abs(tnY.z) * n.y);
                tnZ = half3(tnZ.xy + n.xy, abs(tnZ.z) * n.z);
                float3 rockNormal = normalize(tnX.zyx * bw.x + tnY.xzy * bw.y + tnZ.xyz * bw.z);

                // Height-based blending: the layer that is both weighted and physically higher wins
                float4 h = float4(sand.a, silt.a, gravel.a, rock.a) * 0.6 + float4(wSand, wSilt, wGravel, wRock);
                float top = max(max(h.x, h.y), max(h.z, h.w)) - _BlendDepth;
                float4 b = max(h - top, 0.0) * step(0.001, float4(wSand, wSilt, wGravel, wRock));
                b /= max(b.x + b.y + b.z + b.w, 1e-4);

                half3 albedo = (sand.rgb * b.x + silt.rgb * b.y + gravel.rgb * b.z + rock.rgb * b.w) * _Tint.rgb;

                half3 planarTN = (sandN * b.x + siltN * b.y + gravelN * b.z) / max(b.x + b.y + b.z, 1e-4);
                float3 normalWS = normalize(lerp(TopDownToWorld(planarTN, n), rockNormal, b.w));

                // Lighting: sun with shadows, caustics riding on the sunlight, gradient ambient
                Light light = GetMainLight(TransformWorldToShadowCoord(positionWS));
                float NdotL = saturate(dot(normalWS, light.direction));
                float depth = max(_AquaticaSurfaceHeight - positionWS.y, 0.0);
                // Soft threshold: full strength on high ground, fading out towards the deep floor
                float causticFade = 1.0 - smoothstep(_CausticFullDepth, _CausticFadeDepth, depth);

                // Project along the light so caustics shift with slope like real ones
                float2 causticUV = (xz - light.direction.xz / max(light.direction.y, 0.2) * positionWS.y * 0.15) / _CausticSize;
                float time = _Time.y * _CausticSpeed;
                float caustic = Caustic(causticUV, time) * 0.6 + Caustic(causticUV * 1.37 + 0.31, time * 0.83) * 0.4;
                half3 causticLight = _CausticColor.rgb * caustic * _CausticStrength * causticFade * saturate(normalWS.y);

                half occlusion = m.a;
                half3 direct = light.color * (light.shadowAttenuation * light.distanceAttenuation) * NdotL;
                half3 color = albedo * (direct * (0.45 + causticLight) + SampleSH(normalWS) * occlusion);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
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
