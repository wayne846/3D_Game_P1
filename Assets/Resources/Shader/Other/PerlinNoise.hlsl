#ifndef PERLIN_NOISE
#define PERLIN_NOISE

#include "UnityShaderVariables.cginc"

float2 GenerateRandomVector(float2 uv)
{
    uv = uv + 0.01;
    float x = dot(uv, float2(123.4, 234.5));
    float y = dot(uv, float2(234.5, 345.6));
    float2 gradient = sin(float2(x, y));
    gradient *= 43758.5453;
    
    return sin(gradient + 1.2 * _Time);
}

/**
https://youtu.be/7fd331zsie0?si=MKI_tUMCcYzecbJY
*/
float perlinNoise(float2 uv)
{
    // split uv coordinate into 4 * 4 grids
    uv *= 16; // 16 * 16 的 Grid
    float2 gridId = floor(uv);
    float2 gridUV = frac(uv);
    
    // Find the grid corner
    float2 bl = gridId;
    float2 br = gridId + float2(1, 0);
    float2 tl = gridId + float2(0, 1);
    float2 tr = gridId + float2(1, 1);
    
    // Generate random vector at four coordinate
    float2 gradientBL = GenerateRandomVector(bl);
    float2 gradientBR = GenerateRandomVector(br);
    float2 gradientTL = GenerateRandomVector(tl);
    float2 gradientTR = GenerateRandomVector(tr);
    
    // Calculate dot product between gradient and the offset
    float dotBL = dot(gradientBL, uv - bl);
    float dotBR = dot(gradientBR, uv - br);
    float dotTL = dot(gradientTL, uv - tl);
    float dotTR = dot(gradientTR, uv - tr);
    
    // interpolate
    gridUV = smoothstep(0, 1, gridUV);
    float b = lerp(dotBL, dotBR, gridUV.x);
    float t = lerp(dotTL, dotTR, gridUV.x);
    return lerp(b, t, gridUV.y);
}

#endif
