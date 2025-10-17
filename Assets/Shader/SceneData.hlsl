// 這個檔案定義了 PbrtScene 中的資料怎麼傳給 shader
// 要傳 Texture Array, Mesh, Lights
#ifndef SCENE_DATA
#define SCENE_DATA

Texture2DArray _Textures;            // 傳貼圖的陣列
SamplerState sampler_Textures; // 不用傳

struct MeshObject
{
    float4x4 localToWorldMatrix; // Local Coordinate -> Global Coordinate
    int indices_offset; // 一個 Mesh 由 _Indices 中 [indices_offset, indices_offset + indices_count) 的頂點組成
    int indices_count;
    float4 Kd; // diffuse,      (r, g, b, 0) or (TextureIndex, *, *, -1), 特定顏色 or 從特定 texture sample
    float4 Ks; // specular,     (r, g, b, 0)
    float4 Kt; // transmission, (r, g, b, 0)
};

struct BVHNode
{
    float3 boundsMin;
    float3 boundsMax;
    uint leftFirst; // leaf: first triangle index; not leaf: left child node index
    uint count;     // leaf: number of triangles; not leaf: 0
};

StructuredBuffer<MeshObject> _MeshObjects; // 所有的 Mesh
StructuredBuffer<int> _Indices; // 哪幾個頂點構成一個面

StructuredBuffer<float3> _Vertices; // 每個頂點的位置    (x, y, z)
StructuredBuffer<float3> _Normals;  // 每個頂點的法向量   (x, y, z)
StructuredBuffer<float2> _UVs;      // 每個頂點的貼圖座標 (u, v), 沒貼圖座標填 (0, 0)

StructuredBuffer<float4> _Lights;      // 每個光都是 (x, y, z, w), 若 w == 0 -> 方向光的方向（物體 -> 光）, 若 w == 1 -> 點光的位置
StructuredBuffer<float3> _LightColors; // 每個光的顏色 (r, g, b)

//BVH
StructuredBuffer<BVHNode> _BVHNodes;
StructuredBuffer<uint> _PrimIndices;
StructuredBuffer<uint2> _PrimMap; // primitive index -> mesh index

#endif
