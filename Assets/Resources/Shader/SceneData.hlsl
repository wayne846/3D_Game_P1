// 這個檔案定義了 PbrtScene 中的資料怎麼傳給 shader
// 要傳 Texture Array, Mesh, Lights
#ifndef SCENE_DATA
#define SCENE_DATA

// 每個 Mesh Object 的材質 //////////////////////////////////////////////////////////////
// 由 MeshToCompute 傳入

Texture2DArray _Textures;            // 傳貼圖的陣列
SamplerState sampler_Textures; // 不用傳

struct Material
{
    float4 Kd; // diffuse,      (r, g, b, 0) or (TextureIndex, *, *, -1), 特定顏色 or 從特定 texture sample
    float4 Ks; // specular,     (r, g, b, 0)
    float4 Kt; // transmission, (r, g, b, 0)
    int bumpMapLayer; // -1 -> 沒有 bump map, >= 0 -> Textures[bumpMapLayer] 是 bump map
};

StructuredBuffer<Material> _Materials;

// 每個 Mesh Object 的幾何資訊 /////////////////////////////////////////////////////////
// 由 SceneBVH 傳入

struct MeshObject
{
    float4x4 localToWorldMatrix; // Local Coordinate -> Global Coordinate
    float4x4 worldToLocalMatrix; // Global Coordinate -> Local
    int indices_offset; // 一個 Mesh 由 _Indices 中 [indices_offset, indices_offset + indices_count) 的頂點組成
    int indices_count;
    int bvhRoot;
};

struct BVHNode
{
    // AABB 中 xyz 最小和最大
    float4 minAABB;
    float4 maxAABB;
    // 1. 當 indices_count > 0  -> _Indices [indices_offset, indices_offset + indices_count) 為這個節點包含的所有三角形
    // 2. 當 indices_count <= 0 -> _BVHs[indices_offset] 是左子樹，_BVHs[indices_offset + 1] 是右子樹
    int indices_offset, indices_count;
};

StructuredBuffer<MeshObject> _MeshObjects; // 所有的 Mesh
StructuredBuffer<int> _Indices; // 哪幾個頂點構成一個面
StructuredBuffer<BVHNode> _BVHs; // 將所有 BVH 樹的節點存成陣列

StructuredBuffer<float3> _Vertices; // 每個頂點的位置    (x, y, z)
StructuredBuffer<float3> _Normals;  // 每個頂點的法向量   (x, y, z)
StructuredBuffer<float2> _UVs;      // 每個頂點的貼圖座標 (u, v), 沒貼圖座標填 (0, 0)


// 光源 /////////////////////////////////////////////////////////////////////////////////////////////////////////////
// 由 MeshToCompute 傳入

StructuredBuffer<float4> _Lights;      // 每個光都是 (x, y, z, w), 若 w == 0 -> 方向光的方向（物體 -> 光）, 若 w == 1 -> 點光的位置
StructuredBuffer<float3> _LightColors; // 每個光的顏色 (r, g, b)

#endif
