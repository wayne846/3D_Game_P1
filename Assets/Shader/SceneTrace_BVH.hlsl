#ifndef SCENE_TRACE_BVH_ADDONS
#define SCENE_TRACE_BVH_ADDONS

// 這個檔不 include 任何東西！
// 前提：在 .compute 內，先後順序要是：
//   #include "BasicRay.hlsl"
//   #include "SceneData.hlsl"        // 舊場景資料（guard 名稱例如 SCENE_DATA）
//   #include "SceneData_BVH.hlsl"    // BVH 資料（guard 名稱例如 SCENE_DATA_BVH，不能和上面重名）
//   #include "SceneTrace.hlsl"       // IntersectTriangle_MT97 / IntersectAABB / GetNormal 等
//   #include "SceneTrace_BVH.hlsl"   // ★本檔（只放新增函式）

// 跨平台安全的無窮大
//static const float INF = 3.402823e+38f;

// --- 前置宣告（避免某些後端對“先用後定義”的問題） ---
bool IntersectTriangle_MT97(Ray ray, float3 v0, float3 v1, float3 v2,
                            inout HitInfo hitInfo, out float u, out float v);
bool IntersectAABB(in Ray ray, in float3 bMin, in float3 bMax, in float tMin, in float tMax, out float tNear, out float tFar);

// 用距離判斷是否命中（比 isfinite 跨平台穩）
bool HasHit(ExtraHitInfo h);

// 用 GetDimensions 判斷 BVH 是否存在
inline bool HasBVHData()
{
    uint count, stride;
    _BVHNodes.GetDimensions(count, stride);
    return count > 0;
}

// primID -> 世界座標三角形 + meshId（_PrimMap: x=meshId, y=firstIndex）
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

// --- 主角：TraceBVH（非遞迴、堆疊遍歷、近遠排序） ---
ExtraHitInfo TraceBVH(Ray ray, float rayDistance)
{
    // 初始化最佳命中
    HitInfo bestHit = CreateEmptyHitInfo();
    bestHit.distance = rayDistance;
    uint   bestMesh = 0xffffffff;
    uint   bestFirstIndex = 0xffffffff;
    float2 bestUV = float2(-1, -1);

    // 沒有 BVH 資料就直接回傳 miss
    if (!HasBVHData())
    {
        ExtraHitInfo r;
        r.hitInfo = bestHit; r.hitMesh = bestMesh; r.hitIndexOffset = bestFirstIndex; r.hitUV = bestUV;
        return r;
    }

    // 小心：本地陣列不要太大，64 對大多數場景足夠
    uint stack[64];
    int  sp = 0;
    stack[sp++] = 0; // root = 0

    while (sp > 0)
    {
        uint ni = stack[--sp];
        BVHNode node = _BVHNodes[ni];

        float tNear, tFar;
        if (!IntersectAABB(ray, node.boundsMin, node.boundsMax, 0.0, bestHit.distance, tNear, tFar))
            continue;

        if (node.count > 0)
        {
            // 葉節點：測這一段 primitives
            for (uint k = 0; k < node.count; ++k)
            {
                uint primID = _PrimIndices[node.leftFirst + k];

                float3 v0, v1, v2; uint meshId;
                FetchTriangleFromPrim(primID, v0, v1, v2, meshId);

                float u, v;
                HitInfo cand = bestHit; // 以目前最近距離作為 tMax
                if (IntersectTriangle_MT97(ray, v0, v1, v2, cand, u, v))
                {
                    bestHit = cand;
                    bestMesh = meshId;
                    bestFirstIndex = _PrimMap[primID].y; // 保留 _Indices 的起點
                    bestUV = float2(u, v);
                }
            }
        }
        else
        {
            // 內部節點：近遠排序（後 push 的先 pop）
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

    // 組回 ExtraHitInfo
    ExtraHitInfo result;
    result.hitInfo        = bestHit;
    result.hitMesh        = bestMesh;
    result.hitIndexOffset = bestFirstIndex;
    result.hitUV          = bestUV;
    return result;
}

#endif // SCENE_TRACE_BVH_ADDONS
