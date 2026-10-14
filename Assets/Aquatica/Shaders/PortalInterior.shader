// Surfaces of the world behind the gateway. Stencil-tested against PortalMask so they only appear
// through the portal opening (Comp Equal); PortalTraversal flips this to NotEqual once the viewer
// has stepped inside. Simple fixed-direction lighting, animated caustics and distance fog towards
// deep blue sell the depth of the corridor.
Shader "Aquatica/PortalInterior"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.30, 0.42, 0.52, 1)
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _CausticColor ("Caustic Color", Color) = (0.50, 0.90, 1.00, 1)
        _CausticStrength ("Caustic Strength", Range(0, 2)) = 0.5
        _CausticScale ("Caustic Scale", Float) = 3
        _FogColor ("Fog Color", Color) = (0.02, 0.06, 0.18, 1)
        _FogStart ("Fog Start (m from camera)", Float) = 1
        _FogEnd ("Fog End (m from camera)", Float) = 5
        _FogAmount ("Fog Amount", Range(0, 1)) = 1
        [IntRange] _StencilRef ("Stencil Ref", Range(0, 255)) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comp", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "PortalInterior"
            Tags { "LightMode" = "UniversalForward" }

            Stencil
            {
                Ref [_StencilRef]
                Comp [_StencilComp]
                Pass Keep
            }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
                half4 _CausticColor;
                float _CausticStrength;
                float _CausticScale;
                half4 _FogColor;
                float _FogStart;
                float _FogEnd;
                float _FogAmount;
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
            };

            // Interfering sine waves; bright where they cancel out -> thin caustic lines
            float Caustics(float2 p, float t)
            {
                float v = sin(p.x + t * 0.9)
                        + sin(p.y * 1.13 - t * 1.1)
                        + sin((p.x + p.y) * 0.71 + t * 0.7)
                        + sin(length(p - float2(3.0, 1.0)) * 1.3 - t * 1.4);
                v *= 0.25;
                return pow(saturate(1.0 - abs(v) * 2.0), 4.0);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 lightDir = normalize(float3(0.25, 1.0, -0.35));
                float diffuse = 0.45 + 0.55 * saturate(dot(n, lightDir));

                // Caustics projected from above, strongest on upward-facing surfaces
                float2 p = (input.positionWS.xz + input.positionWS.y * 0.35) * _CausticScale;
                float caustic = Caustics(p, _Time.y) * saturate(n.y * 0.5 + 0.6);

                half3 col = _BaseColor.rgb * diffuse
                          + _CausticColor.rgb * caustic * _CausticStrength
                          + _EmissionColor.rgb;

                float dist = distance(_WorldSpaceCameraPos, input.positionWS);
                float fog = saturate((dist - _FogStart) / max(_FogEnd - _FogStart, 1e-3)) * _FogAmount;
                col = lerp(col, _FogColor.rgb, fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
