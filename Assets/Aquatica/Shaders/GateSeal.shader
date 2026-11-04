// The exit gateway's seal: a murky, slowly swirling membrane across the opening. It glows brighter with each Tide
// Crystal brought aboard (_Charge 0-1) and dissolves from the middle outwards into a bright passage when the gate
// opens (_Open 0-1). Works in object space (the opening's mesh needs no UVs): _Centre / _Extent describe the
// opening in the mesh's units. Fogged like everything else: its light reaches the surroundings through the
// gateway's lamp (ExitGateway), not by shining through the murk.
Shader "Aquatica/GateSeal"
{
    Properties
    {
        [HDR] _SealColor ("Seal Color", Color) = (0.008, 0.03, 0.045, 0.92)
        [HDR] _GlowColor ("Glow Color", Color) = (0.2, 0.75, 1.0, 1)
        [HDR] _PassageColor ("Open Passage Color", Color) = (0.3, 0.85, 1.1, 0.5)
        _Centre ("Opening Centre (object space)", Vector) = (0, 0.88, 0, 0)
        _Extent ("Opening Half Size (object space)", Vector) = (0.51, 0.88, 0, 0)
        _Charge ("Charge", Range(0, 1)) = 0
        _Open ("Open", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "GateSeal"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SealColor, _GlowColor, _PassageColor;
                float4 _Centre, _Extent;
                float _Charge, _Open;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 p : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.p = (input.positionOS.xy - _Centre.xy) / _Extent.xy;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    v += ValueNoise(p) * a;
                    p = p * 2.03 + 7.1;
                    a *= 0.5;
                }
                return v;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float2 p = input.p;
                float r = length(p);

                // Slow swirl around the middle: the noise is twisted more towards the edge (a rotation, not an
                // angle, so there's no seam where the angle wraps)
                float twist = t * 0.12 + r * 1.8;
                float2 q = float2(cos(twist) * p.x - sin(twist) * p.y, sin(twist) * p.x + cos(twist) * p.y);
                float n = Fbm(q * 2.2 + float2(0.0, t * 0.05));
                float bands = 0.5 + 0.5 * sin(n * 9.0 + t * 0.6);

                // Closed: murky membrane, brighter veins and edge as it charges
                float edge = smoothstep(0.75, 1.0, max(abs(p.x), abs(p.y) * 0.9));
                half3 seal = _SealColor.rgb + _GlowColor.rgb * (bands * n * (0.04 + 0.25 * _Charge) + edge * (0.05 + 0.3 * _Charge));
                half sealAlpha = _SealColor.a;

                // Opening: a ragged hole spreading out from the middle, a bright rim on its edge
                float reach = _Open * 1.6;
                float front = r + (n - 0.5) * 0.5;
                float hole = smoothstep(reach - 0.05, reach - 0.25, front);
                float rim = (1.0 - smoothstep(0.0, 0.12, abs(front - (reach - 0.12)))) * step(0.001, _Open);
                half3 passage = _PassageColor.rgb * (0.7 + 0.3 * bands);

                half3 col = lerp(seal, passage, hole) + _GlowColor.rgb * rim * 1.2;
                half alpha = lerp(sealAlpha, _PassageColor.a * (0.6 + 0.4 * bands), hole);
                alpha = saturate(alpha + rim * 0.6);

                return half4(MixFog(col, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
