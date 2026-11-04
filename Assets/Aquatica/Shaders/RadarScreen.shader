// Round green radar display: range rings, crosshair, a clockwise sweep with a fading trail and blips that
// light up as the sweep passes them. _Contacts (set by RadarConsole) are the blips: xy = position (-1..1),
// z = kind (1 = contact dot, 2 = beacon: a pulsing diamond, always visible), w = brightness.
// _Fault (0-1) tears the image, adds static and flickers it red.
Shader "Aquatica/RadarScreen"
{
    Properties
    {
        [HDR] _GlowColor ("Glow Color", Color) = (0.3, 1.4, 0.55, 1)
        _BackColor ("Background", Color) = (0.005, 0.05, 0.025, 1)
        _SweepSpeed ("Sweep Speed (rad/s)", Float) = 1.6
        [HDR] _FaultColor ("Fault Color", Color) = (1.4, 0.15, 0.08, 1)
        _Fault ("Fault", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "RadarScreen"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half4 _BackColor;
                float _SweepSpeed;
                half4 _FaultColor;
                float _Fault;
            CBUFFER_END

            float4 _Contacts[8];

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            float Line(float distance, float width)
            {
                return 1.0 - smoothstep(width, width * 2.5, distance);
            }

            half4 frag(Varyings input) : SV_Target
            {
                const float TAU = 6.28318530718;
                float t = _Time.y;
                float2 uv = input.uv;

                // Fault: horizontal tearing
                float tear = (Hash(float2(floor(uv.y * 28.0), floor(t * 14.0))) - 0.5) * 0.12 * _Fault;
                uv.x += tear * step(0.6, Hash(float2(floor(uv.y * 9.0), floor(t * 6.0))));

                float2 p = uv * 2.0 - 1.0;
                float r = length(p);
                float a = atan2(p.y, p.x);
                float sweep = -t * _SweepSpeed;

                float rings = Line(abs(frac(r / 0.3 + 0.5) - 0.5) * 0.3, 0.003) * step(r, 0.9);
                float cross = Line(min(abs(p.x), abs(p.y)), 0.0025) * step(r, 0.9);
                float behind = frac((a - sweep) / TAU); // 0 at the sweep line, growing behind it
                float trail = exp(-behind * 9.0) * step(r, 0.9);
                float beam = Line(behind * r * TAU, 0.006) * step(r, 0.9);

                float blips = 0.0;
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float4 c = _Contacts[i];
                    if (c.z < 0.5)
                        continue;
                    float2 d = p - c.xy;
                    if (c.z < 1.5)
                    {
                        // Contact: a dot lit by the sweep, fading until it comes round again (never quite gone)
                        float age = frac((atan2(c.y, c.x) - sweep) / TAU);
                        blips += (1.0 - smoothstep(0.022, 0.045, length(d))) * (0.25 + 0.75 * exp(-age * 3.0)) * c.w;
                    }
                    else
                    {
                        // Beacon: a hollow diamond that pulses on its own
                        float diamond = abs(d.x) + abs(d.y);
                        float pulse = 0.6 + 0.4 * sin(t * 3.0);
                        blips += Line(abs(diamond - 0.05), 0.006) * pulse * c.w * 1.2;
                    }
                }

                float rim = Line(abs(r - 0.93), 0.008);
                float scan = 0.9 + 0.1 * sin(input.uv.y * 520.0);

                half3 col = _BackColor.rgb * step(r, 0.95)
                          + _GlowColor.rgb * (rings * 0.3 + cross * 0.22 + trail * 0.45 + beam * 0.9 + blips * 1.4) * scan
                          + _GlowColor.rgb * rim * 0.25;

                // Fault: static and red flicker take over
                float noise = Hash(floor(input.uv * 90.0) + floor(t * 24.0));
                float flicker = 0.6 + 0.4 * step(0.5, Hash(float2(floor(t * 9.0), 1.7)));
                half3 broken = _FaultColor.rgb * (noise * 0.35 + rings * 0.4 + cross * 0.3) * flicker * step(r, 0.95);
                col = lerp(col, broken + col * 0.25 * noise, _Fault);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
