// Light from one glowing object in the world (the exit gateway's seal) for the custom-lit Aquatica shaders, which
// otherwise only take the sun: set globally by ExitGateway. _AquaticaGlowPosition.w is the range (0 = off).
#ifndef AQUATICA_GLOW_INCLUDED
#define AQUATICA_GLOW_INCLUDED

float4 _AquaticaGlowPosition;
half4 _AquaticaGlowColor;

half3 AquaticaGlow(float3 positionWS, float3 normalWS)
{
    if (_AquaticaGlowPosition.w <= 0.0)
        return 0;
    float3 toGlow = _AquaticaGlowPosition.xyz - positionWS;
    float distance = length(toGlow);
    float falloff = saturate(1.0 - distance / _AquaticaGlowPosition.w);
    falloff *= falloff;
    // Wrapped, so ground below and beside the opening still catches some
    float facing = saturate(dot(normalWS, toGlow / max(distance, 1e-3)) * 0.75 + 0.25);
    return _AquaticaGlowColor.rgb * falloff * facing;
}

#endif
