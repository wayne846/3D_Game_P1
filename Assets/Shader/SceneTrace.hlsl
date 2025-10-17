// 包含如何在 SceneData.hlsl 描述的場景中 Trace Ray 打到的物件
// 計算 打到的面的法向量、Kd
#ifndef SCENE_TRACE
#define SCENE_TRACE

#include "BasicRay.hlsl"
#include "SceneData.hlsl"

struct ExtraHitInfo
{
    HitInfo hitInfo;
    uint hitMesh;
    uint hitIndexOffset;
    float2 hitUV; // 重心座標，打到的點可以用 (1-u-v) * v0 + u * v1 + v * v2 內插而得
};

ExtraHitInfo Trace(Ray ray, float rayDistance)
{
    HitInfo hitInfo = CreateEmptyHitInfo();
    hitInfo.distance = rayDistance;
    uint hitMesh, hitIndexOffset;
    float2 hitUV = float2(-1, -1);
    
    // 對每個 Mesh 的每個三角面比較
    uint meshCount, stride;
    _MeshObjects.GetDimensions(meshCount, stride);
    for (uint mesh = 0; mesh < meshCount; ++mesh)
    {
        MeshObject MeshObj = _MeshObjects[mesh];
        
        // 對於每個三角面
        for (int offset = MeshObj.indices_offset; offset < MeshObj.indices_offset + MeshObj.indices_count; offset += 3)
        {
            int v0 = _Indices[offset], v1 = _Indices[offset + 1], v2 = _Indices[offset + 2];
            float4 globalV0 = mul(MeshObj.localToWorldMatrix, float4(_Vertices[v0], 1));
            float4 globalV1 = mul(MeshObj.localToWorldMatrix, float4(_Vertices[v1], 1));
            float4 globalV2 = mul(MeshObj.localToWorldMatrix, float4(_Vertices[v2], 1));
            float u = -1, v = -1;
            
            if (IntersectTriangle_MT97(ray, globalV0.xyz, globalV1.xyz, globalV2.xyz, hitInfo, u, v))
            {
                hitMesh = mesh;
                hitIndexOffset = offset;
                hitUV = float2(u, v);
            }
        }
    }

    ExtraHitInfo result;
    result.hitInfo = hitInfo;
    result.hitMesh = hitMesh;
    result.hitIndexOffset = hitIndexOffset;
    result.hitUV = hitUV;
    return result;
}

bool HasHit(ExtraHitInfo h)
{
    return all(isfinite(h.hitInfo.position));
}

float3 GetKd(ExtraHitInfo extraHitInfo)
{
    float4 Kd = _MeshObjects[extraHitInfo.hitMesh].Kd;
    
    float3 color = Kd.xyz;
    
    // 計算 texcoord
    uint offset = extraHitInfo.hitIndexOffset;
    int v0 = _Indices[offset], v1 = _Indices[offset + 1], v2 = _Indices[offset + 2];
    float2 hitUV = extraHitInfo.hitUV;
    float2 texcoord = (1 - hitUV.x - hitUV.y) * _UVs[v0] + hitUV.x * _UVs[v1] + hitUV.y * _UVs[v2];
    
    // sample (Note: Sample 不能放在 if 分支內)
    float3 colorFromTexture = _Textures.SampleLevel(sampler_Textures, float3(texcoord, Kd.x), 0).xyz;
    
    // Kd.w == 0 -> color; Kd.w == -1 -> colorFromTexture
    return lerp(color, colorFromTexture, -Kd.w);
}

// Phong Interpolation of Normal
float3 GetNormal(ExtraHitInfo extraHitInfo)
{
    float3x3 rot = _MeshObjects[extraHitInfo.hitMesh].localToWorldMatrix;
    
    uint offset = extraHitInfo.hitIndexOffset;
    int v0 = _Indices[offset], v1 = _Indices[offset + 1], v2 = _Indices[offset + 2];
    float2 hitUV = extraHitInfo.hitUV;
    
    float3 localN = normalize((1.f - hitUV.x - hitUV.y) * normalize(_Normals[v0]) + hitUV.x * normalize(_Normals[v1]) + hitUV.y * normalize(_Normals[v2]));
    float3 N = mul(rot, localN);
    
    if (dot(N, extraHitInfo.hitInfo.normal) < 0)
        return -N;
    else
        return N;
}

inline bool HasBVHData()
{
    uint count, stride;
    _BVHNodes.GetDimensions(count, stride);
    return count > 0;
}

inline void FetchTriangleFromPrim(uint primID, out float3 v0, out float3 v1, out float3 v2, out uint meshId)
{
    uint2 m = _PrimMap[primID];
    meshId = m.x;
    uint firstIndex = m.y;

    int i0 = _Indices[firstIndex + 0];
    int i1 = _Indices[firstIndex + 1];
    int i2 = _Indices[firstIndex + 2];

    MeshObject M = _MeshObjects[meshId];

    float4 g0 = mul(M.localToWorldMatrix, float4(_Vertices[i0], 1));
    float4 g1 = mul(M.localToWorldMatrix, float4(_Vertices[i1], 1));
    float4 g2 = mul(M.localToWorldMatrix, float4(_Vertices[i2], 1));

    v0 = g0.xyz; v1 = g1.xyz; v2 = g2.xyz;
}

ExtraHitInfo TraceBVH(Ray ray, float rayDistance)
{
    HitInfo bestHit = CreateEmptyHitInfo();
    bestHit.distance = rayDistance;
    uint   bestMesh = 0xffffffff;
    uint   bestFirstIndex = 0xffffffff;
    float2 bestUV = float2(-1, -1);

    if (!HasBVHData())
    {
        ExtraHitInfo r;
        r.hitInfo = bestHit; r.hitMesh = bestMesh; r.hitIndexOffset = bestFirstIndex; r.hitUV = bestUV;
        return r;
    }

    uint stack[64];
    int  sp = 0;
    stack[sp++] = 0; 

    while (sp > 0)
    {
        uint ni = stack[--sp];
        BVHNode node = _BVHNodes[ni];

        float tNear, tFar;
        if (!IntersectAABB(ray, node.boundsMin, node.boundsMax, 0.0, bestHit.distance, tNear, tFar))
            continue;

        if (node.count > 0)
        {
            // leaf node
            for (uint k = 0; k < node.count; ++k)
            {
                uint primID = _PrimIndices[node.leftFirst + k];

                float3 v0, v1, v2; uint meshId;
                FetchTriangleFromPrim(primID, v0, v1, v2, meshId);

                float u, v;
                HitInfo cand = bestHit; 
                if (IntersectTriangle_MT97(ray, v0, v1, v2, cand, u, v))
                {
                    bestHit = cand;
                    bestMesh = meshId;
                    bestFirstIndex = _PrimMap[primID].y; 
                    bestUV = float2(u, v);
                }
            }
        }
        else
        {
            // internal node
            uint left  = node.leftFirst;
            uint right = node.leftFirst + 1;

            float tNL, tFL, tNR, tFR;
            bool hitL = IntersectAABB(ray, _BVHNodes[left].boundsMin,  _BVHNodes[left].boundsMax,  0.0, bestHit.distance, tNL, tFL);
            bool hitR = IntersectAABB(ray, _BVHNodes[right].boundsMin, _BVHNodes[right].boundsMax, 0.0, bestHit.distance, tNR, tFR);

            if (hitL && hitR)
            {
                if (tNL < tNR) { stack[sp++] = right; stack[sp++] = left;  }
                else           { stack[sp++] = left;  stack[sp++] = right; }
            }
            else if (hitL) stack[sp++] = left;
            else if (hitR) stack[sp++] = right;
        }
    }

    ExtraHitInfo result;
    result.hitInfo        = bestHit;
    result.hitMesh        = bestMesh;
    result.hitIndexOffset = bestFirstIndex;
    result.hitUV          = bestUV;
    return result;
}

#endif
