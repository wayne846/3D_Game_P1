// 這個檔案定義了 PbrtScene 中的資料怎麼傳給 shader
// 要傳 Texture Array, Mesh, Lights
#ifndef SCENE_DATA_BVH
#define SCENE_DATA_BVH

#include "SceneData.hlsl"


// BVH node structure
struct BVHNode
{
    float3 boundsMin;
    float3 boundsMax;
    uint leftFirst; // leaf: first triangle index; not leaf: left child node index
    uint count;     // leaf: number of triangles; not leaf: 0
};

StructuredBuffer<BVHNode> _BVHNodes;
StructuredBuffer<uint> _PrimIndices;
StructuredBuffer<uint2> _PrimMap; // primitive index -> mesh index

bool IntersectAABB(in Ray ray, in float3 bMin, in float3 bMax, in float tMin, in float tMax, out float tNear, out float tFar)
{
    tNear = -1e20;
    tFar = 1e20;

    float3 invD = 1.0 / ray.dir;

    // x slab
    float t0 = (bMin.x - ray.origin.x) * invD.x;
    float t1 = (bMax.x - ray.origin.x) * invD.x;

    float tEnter = min(t0, t1);
    float tExit = max(t0, t1);

    tNear = max(tNear, tEnter);
    tFar = min(tFar, tExit);

    if(tNear > tFar) return false;

    // y slab
    t0 = (bMin.y - ray.origin.y) * invD.y;
    t1 = (bMax.y - ray.origin.y) * invD.y;

    tEnter = min(t0, t1);
    tExit = max(t0, t1);

    tNear = max(tNear, tEnter);
    tFar = min(tFar, tExit);

    if(tNear > tFar) return false;

    // z slab
    t0 = (bMin.z - ray.origin.z) * invD.z;
    t1 = (bMax.z - ray.origin.z) * invD.z;

    tEnter = min(t0, t1);
    tExit = max(t0, t1);

    tNear = max(tNear, tEnter);
    tFar = min(tFar, tExit);

    if(tNear > tFar) return false;

    //tmin tmax check
    if(tFar < tMin) return false;
    if(tNear > tMax) return false;
    
    tNear = max(tNear, tMin);
    tFar = min(tFar, tMax);

    return true;
}

#endif
