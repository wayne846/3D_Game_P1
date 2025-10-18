// 包含如何在 SceneData.hlsl 描述的場景中 Trace Ray 打到的物件
// 計算 打到的面的法向量、Kd
#ifndef SCENE_TRACE
#define SCENE_TRACE

#include "BasicRay.hlsl"
#include "SceneData.hlsl"

bool _GlobalUseBumpMap; // 是否使用 bump map

struct ExtraHitInfo
{
    HitInfo hitInfo;
    int hitMesh;
    int hitIndexOffset;
    float2 hitUV; // 重心座標，打到的點可以用 (1-u-v) * v0 + u * v1 + v * v2 內插而得
};

ExtraHitInfo CreateEmptyExtraHitInfo()
{
    ExtraHitInfo extra;
    extra.hitInfo = CreateEmptyHitInfo();
    extra.hitMesh = -1;
    extra.hitIndexOffset = -1;
    extra.hitUV = float2(-1, -1);
    return extra;
}

bool HasHit(ExtraHitInfo h)
{
    return h.hitIndexOffset != -1;
}

/// <summary>
/// 求解 ray 是否通過 AABB，如有通過回傳 true
/// </summary>
bool IntersectAABB(Ray ray, float rayDistance, float4 minAABB, float4 maxAABB)
{
    // Note: ray上的點 = origin + t * dir, https://www.rose-hulman.edu/class/cs/csse451/AABB/
    //       t = (ray上的點 - origin) / dir
    float tenter = -INF, tout = INF;

    // 在每個維度中，t 較小的為 Ray 的入射平面，t 較大的為 Ray 的射出平面
    // 在所有維度中的「入射」平面中選 t 「最大」的那個為 tenter、在所有維度的「射出」平面選 t 「最小」的那個為 tout
    // tenter <= tout && tout > 0    <=>   Ray 在 [tenter, tout] 的範圍位於 AABB 內   <=>  Ray 穿過 AABB

    // X axis
    if (ray.dir.x != 0.0f)
    {
        float tx1 = (minAABB.x - ray.origin.x) / ray.dir.x;
        float tx2 = (maxAABB.x - ray.origin.x) / ray.dir.x;
        tenter = max(tenter, min(tx1, tx2));
        tout = min(tout, max(tx1, tx2));
    }
    else if (ray.origin.x < minAABB.x || ray.origin.x > maxAABB.x)
    {
        return false; // 平行且在盒子外
    }

    // Y axis
    if (ray.dir.y != 0.0f)
    {
        float ty1 = (minAABB.y - ray.origin.y) / ray.dir.y;
        float ty2 = (maxAABB.y - ray.origin.y) / ray.dir.y;
        tenter = max(tenter, min(ty1, ty2));
        tout = min(tout, max(ty1, ty2));
    }
    else if (ray.origin.y < minAABB.y || ray.origin.y > maxAABB.y)
    {
        return false;
    }

    // Z axis
    if (ray.dir.z != 0.0f)
    {
        float tz1 = (minAABB.z - ray.origin.z) / ray.dir.z;
        float tz2 = (maxAABB.z - ray.origin.z) / ray.dir.z;
        tenter = max(tenter, min(tz1, tz2));
        tout = min(tout, max(tz1, tz2));
    }
    else if (ray.origin.z < minAABB.z || ray.origin.z > maxAABB.z)
    {
        return false;
    }

    // Ray 直線在 distanceT 的位置為 Ray 的結尾
    float distanceT = rayDistance / length(ray.dir);
    // distanceT > tenter -> Ray 在結束以前有進入 AABB 的範圍內
    return tout >= tenter && tout > 0 && distanceT > tenter;
}

ExtraHitInfo TraceBVH(int BVHroot, Ray ray, float rayDistance)
{
    ExtraHitInfo bestHit = CreateEmptyExtraHitInfo();
    bestHit.hitInfo.distance = rayDistance;
    if (BVHroot < 0)
        return bestHit;

    int stack[64];
    int len = 0;
    stack[len++] = BVHroot;     // 把根節點放入 stack

    while (len > 0)
    {
        BVHNode root = _BVHs[stack[--len]];
        
        // 如果 ray 打到 AABB
        if (IntersectAABB(ray, rayDistance, root.minAABB, root.maxAABB))
        {
            // 是葉子
            if (root.indices_count > 0)
            {
                // 對於包含的每一個三角形
                for (int i = root.indices_offset; i < root.indices_offset + root.indices_count; i += 3)
                {
                    float u, v;
                    if (IntersectTriangle_MT97(ray, _Vertices[_Indices[i]], _Vertices[_Indices[i + 1]], _Vertices[_Indices[i + 2]]
                                                , bestHit.hitInfo, u, v))
                    {
                        bestHit.hitIndexOffset = i;
                        bestHit.hitUV = float2(u, v);
                    }
                }
            }
            // 是中間節點
            else
            {
                // 拜訪左右子樹
                stack[len++] = root.indices_offset;
                stack[len++] = root.indices_offset + 1;
            }
        }
    }

    return bestHit;
}

ExtraHitInfo Trace(Ray ray, float rayDistance)
{
    ExtraHitInfo bestHit = CreateEmptyExtraHitInfo();
    bestHit.hitInfo.distance = rayDistance;

    uint meshCount, meshStride;
    _MeshObjects.GetDimensions(meshCount, meshStride);
    for (uint i = 0;i < meshCount; ++i)
    {
        // 將 Ray 轉到該 Mesh Object 的區域座標系下，然後和 BVH 樹比較
        float4x4 worldToLocal = _MeshObjects[i].worldToLocalMatrix;
        Ray localRay;
        localRay.origin = mul(worldToLocal, float4(ray.origin, 1)).xyz;
        localRay.dir = normalize(mul((float3x3)worldToLocal, ray.dir));

        float localDistance = bestHit.hitInfo.distance;
        if (localDistance != INF)
        {
            float3 rayEnd = ray.origin + normalize(ray.dir) * bestHit.hitInfo.distance;
            localDistance = distance(localRay.origin, mul(worldToLocal, float4(rayEnd, 1)).xyz);
        }

        // 每個 MeshObject 自己的 BVH 存放在 Local Space 下，所以 ray 要轉到區域座標
        ExtraHitInfo bvhHit = TraceBVH(_MeshObjects[i].bvhRoot, localRay, localDistance);

        if (HasHit(bvhHit))
        {
            // 將 bvhHit 轉回世界座標
            bvhHit.hitInfo.position = mul(_MeshObjects[i].localToWorldMatrix, float4(bvhHit.hitInfo.position, 1)).xyz;
            bvhHit.hitInfo.normal = mul((float3x3)_MeshObjects[i].localToWorldMatrix, bvhHit.hitInfo.normal);
            bvhHit.hitInfo.distance = distance(ray.origin, bvhHit.hitInfo.position);
            bvhHit.hitMesh = i;
            
             // 如果在這個 Mesh 上打得更近
            if (bvhHit.hitInfo.distance < bestHit.hitInfo.distance)
                bestHit = bvhHit;
        }
    }

    return bestHit;
}


float2 GetTexcoord(ExtraHitInfo extraHitInfo)
{
    uint offset = extraHitInfo.hitIndexOffset;
    int v0 = _Indices[offset], v1 = _Indices[offset + 1], v2 = _Indices[offset + 2];
    float2 hitUV = extraHitInfo.hitUV;
    float2 texcoord = (1 - hitUV.x - hitUV.y) * _UVs[v0] + hitUV.x * _UVs[v1] + hitUV.y * _UVs[v2];
    return texcoord;
}

float3 GetKd(ExtraHitInfo extraHitInfo)
{
    float4 Kd = _Materials[extraHitInfo.hitMesh].Kd;
    
    float3 color = Kd.xyz;
    
    // 計算 texcoord
    float2 texcoord = GetTexcoord(extraHitInfo);
    
    // sample (Note: Sample 不能放在 if 分支內)
    float3 colorFromTexture = _Textures.SampleLevel(sampler_Textures, float3(texcoord, Kd.x), 0).xyz;
    
    // Kd.w == 0 -> color; Kd.w == -1 -> colorFromTexture
    return lerp(color, colorFromTexture, -Kd.w);
}

// Phong Interpolation of Normal || Bump Map
float3 GetNormal(ExtraHitInfo extraHitInfo)
{
    int Bump = _Materials[extraHitInfo.hitMesh].bumpMapLayer;
    if (_GlobalUseBumpMap && Bump >= 0)
    {
        int width, height, elements, numberOfLevel;
        _Textures.GetDimensions(0, width, height, elements, numberOfLevel);
        float3 texcoord = float3(GetTexcoord(extraHitInfo), Bump);
    
        float du = 1.f / height, dv = 1.f / width;
        float CenterHeight = _Textures.SampleLevel(sampler_Textures, texcoord, 0);
        float duHeight = _Textures.SampleLevel(sampler_Textures, texcoord + float3(du, 0, 0), 0);
        float dvHeight = _Textures.SampleLevel(sampler_Textures, texcoord + float3(0, dv, 0), 0);

        float3 normalInTBN = normalize(cross(float3(1, 0, duHeight - CenterHeight), float3(0, 1, dvHeight - CenterHeight)));
    
        /// TBN -> world
        float3 oldNormal = normalize(extraHitInfo.hitInfo.normal);
        float3 up = abs(oldNormal.y) < 0.999 ? float3(0, 1, 0) : float3(0, 0, -1);
        float3 B = normalize(cross(up, oldNormal)); // Right ?
        float3 T = normalize(cross(B, oldNormal));
        float3x3 TBNtoWorld = float3x3(T.x, B.x, oldNormal.x,
                                   T.y, B.y, oldNormal.y,
                                   T.z, B.z, oldNormal.z);
    
        return mul(TBNtoWorld, normalInTBN);
    }
    else
    {
        float3x3 rot = _MeshObjects[extraHitInfo.hitMesh].localToWorldMatrix;
    
        uint offset = extraHitInfo.hitIndexOffset;
        int v0 = _Indices[offset], v1 = _Indices[offset + 1], v2 = _Indices[offset + 2];
        float2 hitUV = extraHitInfo.hitUV;
    
        float3 localN = normalize((1.f - hitUV.x - hitUV.y) * normalize(_Normals[v0]) + hitUV.x * normalize(_Normals[v1]) + hitUV.y * normalize(_Normals[v2]));
        float3 N = normalize(mul(rot, localN));
    
        if (length(N) < 0.5)
            return extraHitInfo.hitInfo.normal;
    
        if (dot(N, extraHitInfo.hitInfo.normal) < 0)
            return -N;
        else
            return N;
    }
}

#endif
