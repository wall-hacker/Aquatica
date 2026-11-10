// Water flooding the submarine cabin, seen from above: a translucent sheet with small animated ripples and a fresnel
// sheen, lit only by the cabin's own lights (_CabinLight*, set by CabinFlood: position + range, colour x intensity),
// which also give it small soft highlights; no sun reaches in here.
Shader "Aquatica/CabinWater"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.30, 0.62, 0.62, 1)
        _DeepColor ("Deep Color", Color) = (0.04, 0.20, 0.26, 1)
        _SheenColor ("Sheen Color", Color) = (0.80, 0.95, 1.00, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.6
        _RippleScale ("Ripple Scale (1/m)", Float) = 7
        _RippleSpeed ("Ripple Speed", Float) = 1.1
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.35
        _Agitation ("Agitation (0-1, inflow churn)", Range(0, 1)) = 0
        _Ambient ("Ambient Light", Range(0, 1)) = 0.5
        _GlintStrength ("Highlight Strength", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "CabinWater"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _SheenColor;
                float _Opacity;
                float _RippleScale;
                float _RippleSpeed;
                float _RippleStrength;
                float _Agitation;
                float _Ambient;
                float _GlintStrength;
            CBUFFER_END

            float4 _CabinLightPos[4];
            float4 _CabinLightColor[4];
            int _CabinLightCount;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // Height field of small travelling ripples (a few crossed sine waves)
            float Ripples(float2 p, float t)
            {
                float h = 0;
                h += sin(dot(p, float2(0.8, 0.6)) * _RippleScale + t * 1.3);
                h += sin(dot(p, float2(-0.5, 0.9)) * _RippleScale * 1.37 + t * 1.7);
                h += sin(dot(p, float2(0.95, -0.3)) * _RippleScale * 0.71 - t * 1.1);
                h += sin(dot(p, float2(-0.2, -1.0)) * _RippleScale * 2.1 + t * 2.3) * 0.5;
                return h;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _RippleSpeed * (1 + _Agitation * 1.5);
                float2 p = i.positionWS.xz;

                // Normal from the ripple height field (finite differences), livelier while water pours in or out
                float e = 0.01;
                float strength = _RippleStrength * (1 + _Agitation * 1.5);
                float h0 = Ripples(p, t);
                float hx = Ripples(p + float2(e, 0), t);
                float hz = Ripples(p + float2(0, e), t);
                float3 n = normalize(float3(-(hx - h0) / e * 0.02 * strength, 1, -(hz - h0) / e * 0.02 * strength));

                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                float fresnel = pow(1 - saturate(abs(dot(n, view))), 4);

                // The cabin lights: soft diffuse tint and small highlights, fading with distance
                float3 lit = 0;
                float3 highlight = 0;
                [loop] for (int k = 0; k < 4; k++)
                {
                    if (k >= _CabinLightCount)
                        break;
                    float3 toLight = _CabinLightPos[k].xyz - i.positionWS;
                    float d = length(toLight);
                    float3 l = toLight / max(d, 1e-3);
                    float fall = saturate(1 - d / max(_CabinLightPos[k].w, 1e-3));
                    float3 c = _CabinLightColor[k].rgb * fall * fall;
                    lit += c * (0.4 + 0.6 * saturate(dot(n, l)));
                    highlight += c * pow(saturate(dot(n, normalize(l + view))), 90);
                }

                half3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, 0.25);
                col *= _Ambient + lit * 0.5;
                col += _SheenColor.rgb * fresnel * 0.35 * (0.3 + lit * 0.4);
                col += highlight * _GlintStrength;
                float alpha = saturate(_Opacity + fresnel * 0.3);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
