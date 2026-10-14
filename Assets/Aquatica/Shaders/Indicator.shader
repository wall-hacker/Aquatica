// Glowing LED dome. Colour comes from _Color (set per light through a property block by IndicatorLight);
// the centre burns brightest and a small specular glint keeps it reading as glass.
Shader "Aquatica/Indicator"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.2, 2, 0.4, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Indicator"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
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

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float facing = saturate(dot(n, v));
                Light light = GetMainLight();
                float glint = pow(saturate(dot(reflect(-v, n), light.direction)), 60.0);
                half3 col = _Color.rgb * (0.45 + 0.55 * facing * facing) + glint * 0.6;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
