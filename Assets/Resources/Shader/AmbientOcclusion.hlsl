#ifndef AMBIENT_OCCLUSION
#define AMBIENT_OCCLUSION

#include "SceneTrace.hlsl"

// AO parameters
cbuffer AOParams
{
    int _AOUse = 0;
    int _AOSamples = 4;
    float _AORadius = 0.6;
    float _AOBias = 0.006;
    float _AOIntensity = 1;
}

float Hash12(float2 p)
{
    float h = dot(p, float2(127.1, 311.7));
    return frac(sin(h) * 43758.5453123);
}

float3 SampleCosHemisphere(float2 u, float3 N)
{
    float r = sqrt(u.x);
    float phi = 6.2831853 * u.y;
    float x = r * cos(phi), y = r * sin(phi);
    float z = sqrt(saturate(1.0 - x * x - y * y));

    float3 up = (abs(N.z) < 0.999) ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 T = normalize(cross(up, N));
    float3 B = cross(N, T);
    return normalize(x * T + y * B + z * N);
}

float ComputeAmbientOcclusion(ExtraHitInfo info)
{
    if (_AOUse == 0)
        return 1.0;
    
    if (!HasHit(info))
        return 1.0;

    float3 N = GetNormal(info);
    float3 P = info.hitInfo.position + N * _AOBias;
    float occ = 0.0;

    float seed = Hash12(P.xy + N.xy);

    [loop]
    for (int i = 0; i < _AOSamples; ++i)
    {
        float2 u = float2(frac(i * 0.754877666 + seed), frac(i * 0.5698403 + seed * 0.37));
        float3 dir = SampleCosHemisphere(u, N);

        Ray aoRay;
        aoRay.origin = P;
        aoRay.dir = dir;

        ExtraHitInfo hit = Trace(aoRay, _AORadius);
        if (HasHit(hit))
            occ += 1.0;
    }

    float visibility = 1.0 - occ / max(1, _AOSamples);
    visibility = saturate(visibility);
    return pow(visibility, max(0.0001, _AOIntensity));
}

#endif
