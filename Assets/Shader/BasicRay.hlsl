// 包含基本的 Ray 和 Ray Hit 計算
#include "HLSLSupport.cginc"

struct Ray
{
    float3 origin;
    float3 dir;
};

struct HitInfo
{
    float3 position;
    float3 normal;
    float distance;
};

// Camera 相關參數，用來計算 CameraRay 用的
float4x4 _CameraProjectionInverse; // Projection Matrix 的反矩陣
float4x4 _CameraToWorld;           // View Matrix 的反矩陣
float2 _ScreenSize;                // 渲染在的 RenderTexture 有多大，（width, height）

// 生成 Ray //////////////////////////////////////////////

/// <summary>
/// 根據像素座標產生一條光線
/// </summary>
/// <param name="x">像素的 x 座標 (左下角為 0)</param>
/// <param name="y">像素的 y 座標 (左下角為 0)</param>
/// <returns>一條從相機原點出發，穿過像素中心的光線</returns>
Ray CreateCameraRay(float x, float y)
{
    Ray r;
    r.origin = mul(_CameraToWorld, float4(0, 0, 0, 1)).xyz;
    
    // 將像素座標轉換到 [-1, 1] 的正規化裝置座標 (NDC)
    float ndcX = (2.0f * (x + 0.5f) / _ScreenSize.x) - 1.0f;
    float ndcY = (2.0f * (y + 0.5f) / _ScreenSize.y) - 1.0f;
    
    float3 viewSpaceDir = mul(_CameraProjectionInverse, float4(ndcX, ndcY, UNITY_NEAR_CLIP_VALUE, 1.0f)).xyz;
    r.dir = mul(_CameraToWorld, float4(viewSpaceDir, 0.0f)).xyz;
    r.dir = normalize(r.dir);

    return r;
}

// Intersection /////////////////////////////////////////////

HitInfo CreateEmptyHitInfo()
{
    HitInfo info;
    info.distance = 1.#INF; // inf
    return info;
}

void IntersectSphere(Ray ray, float4 sphere, inout HitInfo hitInfo)
{
    float3 center = sphere.xyz;
    float radius = sphere.w;

    float3 oc = center - ray.origin;
    float a = dot(ray.dir, ray.dir); // 距離平方
    float h = dot(ray.dir, oc);
    float c = dot(oc, oc) - radius * radius;

    float discriminant = h * h - a * c;

    if (discriminant < 0)
    {
        return;
    }

    float sqrtd = sqrt(discriminant);

    // Find the nearest root that lies in the acceptable range.
    float root = (h - sqrtd) / a;
    if (root < 0)
        root = (h + sqrtd) / a;
    if (root < 0)
        return;

    if (root < hitInfo.distance)
    {
        hitInfo.distance = root;
        hitInfo.position = ray.origin + normalize(ray.dir) * hitInfo.distance;
        hitInfo.normal = (hitInfo.position - center) / radius;
    }

    return;
}

/// 計算 Ray 和 三角型的交點，vert0, vert1, vert2 為三角型的三個頂點
/// (u, v) 為交點的重心座標，交點 = (1-u-v) * vert0 + u * vert1 + v * vert2
void IntersectTriangle_MT97(Ray ray, float3 vert0, float3 vert1, float3 vert2,
                            inout HitInfo hitInfo, out float u, out float v)
{   
    // find vectors for two edges sharing vert0
    float3 edge1 = vert1 - vert0;
    float3 edge2 = vert2 - vert0;

    // begin calculating determinant - also used to calculate U parameter
    float3 pvec = cross(ray.dir, edge2);

    // if determinant is near zero, ray lies in plane of triangle
    float det = dot(edge1, pvec);

    // 將 abs 刪掉 -> Back Face Culling
    const float EPSILON = 1e-8;
    if (abs(det) < EPSILON)
        return;
    float inv_det = 1.0f / det;

    // calculate distance from vert0 to ray origin
    float3 tvec = ray.origin - vert0;

    // calculate U parameter and test bounds
    u = dot(tvec, pvec) * inv_det;
    if (u < 0.0 || u > 1.0f)
        return;

    // prepare to test V parameter
    float3 qvec = cross(tvec, edge1);

    // calculate V parameter and test bounds
    v = dot(ray.dir, qvec) * inv_det;
    if (v < 0.0 || u + v > 1.0f)
        return;

    // calculate distance, ray intersects triangle
    float t = dot(edge2, qvec) * inv_det;
    if (0 < t && t < hitInfo.distance)
    {
        hitInfo.distance = t;
        hitInfo.position = (1.0f - u - v) * vert0 + u * vert1 + v * vert2;
        hitInfo.normal = normalize(cross(edge1, edge2));
        if (dot(ray.dir, hitInfo.normal) > 0)
            hitInfo.normal = -hitInfo.normal;
    }
    
    return;
}


