using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

[StructLayout(LayoutKind.Sequential)]
struct MeshObjectCS
{
    public Matrix4x4 localToWorldMatrix; // 64
    public int indices_offset;           // 4
    public int indices_count;            // 4
    public Vector4 Kd;                   // (r,g,b,0) or (layer,*,*,-1)
    public Vector4 Ks;                   // (r,g,b, smoothness)
    public Vector4 Kt;                   // (r,g,b,0)
}

struct BVHNodeCPU
{
    public Vector3 boundsMin;
    public Vector3 boundsMax;
    public uint leftFirst; // leaf: first triangle index; not leaf: left child node index
    public uint count;     // leaf: number of triangles; not leaf: 0
}

struct AABB
{
    public Vector3 min;
    public Vector3 max;
    public static AABB FromTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        var mn = Vector3.Min(a, Vector3.Min(b, c));
        var mx = Vector3.Max(a, Vector3.Max(b, c));
        return new AABB { min = mn, max = mx };
    }
    public void Encapsulate(AABB other)
    {
        min = Vector3.Min(min, other.min);
        max = Vector3.Max(max, other.max);
    }
}

struct Prim
{
    public int meshObjId;
    public int i0, i1, i2; // vertex indices in allV
    public Vector3 c0, c1, c2; // vertex positions
    public Vector3 centroid;
    public AABB aabb;
}

[StructLayout(LayoutKind.Sequential)]
struct UInt2
{
    public uint x;
    public uint y;

    public UInt2(uint x, uint y)
    {
        this.x = x;
        this.y = y;
    }
}



[DefaultExecutionOrder(-10)]
public class MeshToCompute_BVH : MonoBehaviour
{
    [Header("Target Compute")]
    public ComputeShader rayTracingCompute;
    public string kernelName = "CSMain";

    [Header("Options")]
    public bool includeInactiveMeshes = true;
    public bool gatherLights = true;
    public bool includeInactiveLights = true;
    public bool flipUV_V = false;

    [Header("Debug (read-only)")]
    public int meshObjectCount;
    public int vertexCount;
    public int indexCount;
    public int lightCount;
    public int textureLayerCount;
    public int nodeCount;
    public int leafCount;
    public int primCount;
    public int maxLeafSize;
    //public int bvhDepth;



    // GPU buffers
    ComputeBuffer _meshObjBuf, _indicesBuf, _vBuf, _nBuf, _uvBuf, _lightBuf, _lightColorsBuf, _bvhNodeBuf, _primIdxBuf, _primMapBuf;
    Texture2DArray _texArray;
    int _kernel;

    void OnEnable()
    {
        if (!rayTracingCompute) { Debug.LogError("[MeshToCompute] Assign ComputeShader."); enabled = false; return; }

        _kernel = rayTracingCompute.FindKernel(kernelName);
        BuildAndUpload();
    }

    void OnDisable()
    {
        _meshObjBuf?.Release(); _meshObjBuf = null;
        _indicesBuf?.Release(); _indicesBuf = null;
        _vBuf?.Release(); _vBuf = null;
        _nBuf?.Release(); _nBuf = null;
        _uvBuf?.Release(); _uvBuf = null;
        _lightBuf?.Release(); _lightBuf = null;
        _lightColorsBuf?.Release(); _lightColorsBuf = null;
        if (_texArray) { Destroy(_texArray); _texArray = null; }
        _bvhNodeBuf?.Release();  _bvhNodeBuf = null;
        _primIdxBuf?.Release();  _primIdxBuf = null;
        _primMapBuf?.Release();  _primMapBuf = null;
    }

    public void BuildAndUpload()
    {
        var layerMap = BuildTextureArray();

        var meshObjs = new List<MeshObjectCS>();
        var allV = new List<Vector3>();
        var allN = new List<Vector3>();
        var allUV = new List<Vector2>();
        var allIndices = new List<int>();

        var inactivePolicy = includeInactiveMeshes ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
        var renderers = FindObjectsByType<MeshRenderer>(inactivePolicy, FindObjectsSortMode.None);

        foreach (var mr in renderers)
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) continue;
            var mesh = mf.sharedMesh;

            // v n uv
            int vtxOffset = allV.Count;
            var V = mesh.vertices;
            var N = mesh.normals;
            var UV = mesh.uv;

            bool hasN = N != null && N.Length == V.Length;
            bool hasUV = UV != null && UV.Length == V.Length;

            allV.AddRange(V);
            allN.AddRange(hasN ? N : FillVec3(V.Length, Vector3.up));
            if (hasUV)
            {
                if (flipUV_V) { for (int i = 0; i < UV.Length; i++) UV[i].y = 1 - UV[i].y; }
                allUV.AddRange(UV);
            }
            else allUV.AddRange(FillVec2(V.Length, Vector2.zero));

            var mats = mr.sharedMaterials;
            int subMeshCount = mesh.subMeshCount;

            for (int sm = 0; sm < subMeshCount; sm++)
            {
                // indices
                int indicesOffset = allIndices.Count;
                var ids = mesh.GetTriangles(sm);
                for (int i = 0; i < ids.Length; i++) allIndices.Add(vtxOffset + ids[i]);

                // Kd / Ks / Kt
                Vector4 Kd = new Vector4(1, 1, 1, 0);
                Vector4 Ks = new Vector4(0, 0, 0, 0);
                Vector4 Kt = new Vector4(0, 0, 0, 0);

                var mat = (sm < mats.Length) ? mats[sm] : null;
                if (mat)
                {
                    var baseCol = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                                : mat.HasProperty("_Color") ? mat.color
                                : Color.white;

                    float smooth = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness")
                                : mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness")
                                : 0f;

                    var specCol = mat.HasProperty("_SpecColor") ? mat.GetColor("_SpecColor") : Color.black;

                    Texture tex = null;
                    if (mat.HasProperty("_BaseMap")) tex = mat.GetTexture("_BaseMap");
                    else if (mat.HasProperty("_MainTex")) tex = mat.GetTexture("_MainTex");

                    bool foundLayer = false;
                    if (tex && layerMap != null && layerMap.TryGetValue(tex, out int layer))
                    {
                        Kd = new Vector4(layer, 0, 0, -1);
                        foundLayer = true;
                    }
                    if (!foundLayer)
                    {
                        Kd = new Vector4(baseCol.r, baseCol.g, baseCol.b, 0);
                    }

                    Ks = new Vector4(specCol.r, specCol.g, specCol.b, Mathf.Clamp01(smooth));
                }

                meshObjs.Add(new MeshObjectCS
                {
                    localToWorldMatrix = mr.localToWorldMatrix,
                    indices_offset = indicesOffset,
                    indices_count = ids.Length,
                    Kd = Kd,
                    Ks = Ks,
                    Kt = Kt
                });
            }
        }

        // lights
        var lights = new List<Vector4>();
        var lightColors = new List<Vector3>();
        if (gatherLights)
        {
            var inactive = includeInactiveLights ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            foreach (var lt in FindObjectsByType<Light>(inactive, FindObjectsSortMode.None))
            {
                if (lt.type == LightType.Point)
                {
                    var p = lt.transform.position;
                    lights.Add(new Vector4(p.x, p.y, p.z, 1));
                }
                else if (lt.type == LightType.Directional)
                {
                    var d = -lt.transform.forward.normalized;
                    lights.Add(new Vector4(d.x, d.y, d.z, 0));
                }
                else continue;

                var c = lt.color;
                lightColors.Add(new Vector3(c.r, c.g, c.b) * lt.intensity);
            }
        }

        //BVH
        var objLocalToWorld = new List<Matrix4x4>(meshObjs.Count);
        var objIndexRanges = new List<(int indicesOffset, int indeicesCount)>(meshObjs.Count);

        foreach (var mo in meshObjs)
        {
            objLocalToWorld.Add(mo.localToWorldMatrix);
            objIndexRanges.Add((mo.indices_offset, mo.indices_count));
        }

        var prims = new List<Prim>(allIndices.Count / 3);
        for (int objId = 0; objId < objIndexRanges.Count; objId++)
        {
            var (off, cnt) = objIndexRanges[objId];
            var M = objLocalToWorld[objId];

            for (int t = 0; t < cnt; t += 3)
            {
                int i0 = allIndices[off + t + 0];
                int i1 = allIndices[off + t + 1];
                int i2 = allIndices[off + t + 2];

                var p0 = M.MultiplyPoint3x4(allV[i0]);
                var p1 = M.MultiplyPoint3x4(allV[i1]);
                var p2 = M.MultiplyPoint3x4(allV[i2]);

                var aabb = AABB.FromTriangle(p0, p1, p2);
                var cent = (p0 + p1 + p2) * (1f / 3f);
                var prim = new Prim
                {
                    meshObjId = objId,
                    i0 = i0,
                    i1 = i1,
                    i2 = i2,
                    c0 = p0,
                    c1 = p1,
                    c2 = p2,
                    centroid = cent,
                    aabb = aabb
                };
                prims.Add(prim);
            }
        }

        var nodes = new List<BVHNodeCPU>();
        var primIndices = new List<uint>(prims.Count);
        BuildBVH(prims, nodes, primIndices, 4);

        var primMap = new List<UInt2>();

        for (uint mesh = 0; mesh < meshObjs.Count; mesh++)
        {
            var mo = meshObjs[(int)mesh];
            for (uint offset = (uint)mo.indices_offset; offset < (uint)mo.indices_offset + (uint)mo.indices_count; offset += 3)
            {
                primMap.Add(new UInt2(mesh, offset));
            }
        }

        // upload
        Upload(ref _meshObjBuf, meshObjs);
        Upload(ref _indicesBuf, allIndices);
        Upload(ref _vBuf, allV);
        Upload(ref _nBuf, allN);
        Upload(ref _uvBuf, allUV);
        Upload(ref _lightBuf, lights);
        Upload(ref _lightColorsBuf, lightColors);
        Upload(ref _bvhNodeBuf, nodes);
        Upload(ref _primIdxBuf, primIndices);
        Upload(ref _primMapBuf, primMap);

        var cs = rayTracingCompute;
        cs.SetBuffer(_kernel, "_MeshObjects", _meshObjBuf);
        cs.SetBuffer(_kernel, "_Indices", _indicesBuf);
        cs.SetBuffer(_kernel, "_Vertices", _vBuf);
        cs.SetBuffer(_kernel, "_Normals", _nBuf);
        cs.SetBuffer(_kernel, "_UVs", _uvBuf);
        cs.SetBuffer(_kernel, "_Lights", _lightBuf);
        cs.SetBuffer(_kernel, "_LightColors", _lightColorsBuf);
        cs.SetBuffer(_kernel, "_BVHNodes", _bvhNodeBuf);
        cs.SetBuffer(_kernel, "_PrimIndices", _primIdxBuf);
        cs.SetBuffer(_kernel, "_PrimMap", _primMapBuf);

        if (_texArray) { cs.SetTexture(_kernel, "_Textures", _texArray); cs.SetInt("_TextureCount", _texArray.depth); }
        else { cs.SetInt("_TextureCount", 0); }

        meshObjectCount = meshObjs.Count;
        vertexCount = allV.Count;
        indexCount = allIndices.Count;
        lightCount = lights.Count;
        textureLayerCount = _texArray ? _texArray.depth : 0;
        nodeCount = nodes.Count;
        foreach (var n in nodes)
        {
            if (n.count > 0)
            {
                leafCount++;
                maxLeafSize = Mathf.Max(maxLeafSize, (int)n.count);
            }
        }
        primCount = prims.Count;


        Debug.Log($"[MeshToCompute] objs:{meshObjectCount}, v:{vertexCount}, i:{indexCount}, lights:{lightCount}, texLayers:{textureLayerCount}");
    }


    Dictionary<Texture, int> BuildTextureArray()
    {
        if (_texArray) { Destroy(_texArray); _texArray = null; }

        var origList = new List<Texture2D>();
        var packList = new List<Texture2D>();
        var seen = new HashSet<Texture>();

        var inactivePolicy = includeInactiveMeshes ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
        var renderers = FindObjectsByType<MeshRenderer>(inactivePolicy, FindObjectsSortMode.None);

        foreach (var mr in renderers)
        {
            foreach (var mat in mr.sharedMaterials)
            {
                if (!mat) continue;

                Texture t = null;
                if (mat.HasProperty("_BaseMap")) t = mat.GetTexture("_BaseMap");
                else if (mat.HasProperty("_MainTex")) t = mat.GetTexture("_MainTex");

                var t2d = t as Texture2D;
                if (!t2d) continue;
                if (seen.Add(t2d)) { origList.Add(t2d); packList.Add(t2d); }
            }
        }

        var map = new Dictionary<Texture, int>();
        if (packList.Count == 0) return map;

        int w = packList[0].width, h = packList[0].height;
        TextureFormat fmt = packList[0].format;
        bool needConvert = false;
        for (int i = 1; i < packList.Count; i++)
            if (packList[i].width != w || packList[i].height != h || packList[i].format != fmt) { needConvert = true; break; }

        if (needConvert || fmt != TextureFormat.RGBA32)
        {
            for (int i = 0; i < packList.Count; i++)
                packList[i] = ToRGBA32(packList[i], w, h);
            fmt = TextureFormat.RGBA32;
        }

        _texArray = new Texture2DArray(w, h, packList.Count, fmt, true, true);
        _texArray.filterMode = FilterMode.Trilinear;
        _texArray.wrapMode = TextureWrapMode.Repeat;

        for (int layer = 0; layer < packList.Count; layer++)
        {
            _texArray.SetPixels(packList[layer].GetPixels(), layer);
            map[origList[layer]] = layer;
        }
        _texArray.Apply(false, false);

        return map;
    }

    static Texture2D ToRGBA32(Texture2D src, int w, int h)
    {
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        tex.Apply();
        RenderTexture.active = prev;
        rt.Release();
        return tex;
    }


    static List<Vector3> FillVec3(int n, Vector3 v) { var L = new List<Vector3>(n); for (int i = 0; i < n; i++) L.Add(v); return L; }
    static List<Vector2> FillVec2(int n, Vector2 v) { var L = new List<Vector2>(n); for (int i = 0; i < n; i++) L.Add(v); return L; }

    static void Upload<T>(ref ComputeBuffer buf, List<T> data) where T : struct
    {
        buf?.Release();
        int count = Math.Max(1, data?.Count ?? 0);
        int stride = Marshal.SizeOf<T>();
        buf = new ComputeBuffer(count, stride);
        if (data != null && data.Count > 0) buf.SetData(data);
    }

    void BuildBVH(List<Prim> prims, List<BVHNodeCPU> nodes, List<uint> primIndices, int leafSize)
    {
        nodes.Clear();
        primIndices.Clear();

        var indices = new int[prims.Count];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;

        int root = BuildRecursive(prims, indices, 0, prims.Count, nodes, primIndices, leafSize);
    }

    int BuildRecursive(List<Prim> prims, int[] ids, int begin, int end, List<BVHNodeCPU> nodes, List<uint> primIndices, int leafSize)
    {
        int nodeIndex = nodes.Count;
        nodes.Add(default);

        //compute bounding box and centroid box
        AABB bounds = prims[ids[begin]].aabb;
        Vector3 cMin = prims[ids[begin]].centroid;
        Vector3 cMax = cMin;
        for (int i = begin + 1; i < end; i++)
        {
            var p = prims[ids[i]];
            bounds.Encapsulate(p.aabb);
            cMin = Vector3.Min(cMin, p.centroid);
            cMax = Vector3.Max(cMax, p.centroid);
        }
        Vector3 cSize = cMax - cMin;

        //leaf node
        int count = end - begin;
        if (count <= leafSize || (cSize.x < 1e-6f && cSize.y <= 1e-6f && cSize.z <= 1e-6f))
        {
            uint first = (uint)primIndices.Count;
            for (int i = begin; i < end; i++)
                primIndices.Add((uint)ids[i]);
            nodes[nodeIndex] = new BVHNodeCPU
            {
                boundsMin = bounds.min,
                boundsMax = bounds.max,
                leftFirst = first,
                count = (uint)count
            };
            return nodeIndex;
        }

        // decide axis to cut
        int axis = 0;
        if (cSize.y > cSize.x && cSize.y > cSize.z) axis = 1;
        else if (cSize.z > cSize.x && cSize.z > cSize.y) axis = 2;

        // cut (median split)
        System.Array.Sort(ids, begin, count, new CentroidComparer(prims, axis));
        int mid = (begin + end) >> 1;

        int leftChild = BuildRecursive(prims, ids, begin, mid, nodes, primIndices, leafSize);
        int rightChild = BuildRecursive(prims, ids, mid, end, nodes, primIndices, leafSize);

        //internal node
        nodes[nodeIndex] = new BVHNodeCPU
        {
            boundsMin = bounds.min,
            boundsMax = bounds.max,
            leftFirst = (uint)leftChild,
            count = 0
        };
        return nodeIndex;
    }

}

class CentroidComparer : IComparer<int>
{
    readonly List<Prim> P;
    readonly int Axis;
    public CentroidComparer(List<Prim> p, int axis) { P = p; Axis = axis; }
    public int Compare(int a, int b)
    {
        float ca = Axis == 0 ? P[a].centroid.x : (Axis == 1 ? P[a].centroid.y : P[a].centroid.z);
        float cb = Axis == 0 ? P[b].centroid.x : (Axis == 1 ? P[b].centroid.y : P[b].centroid.z);
        return ca.CompareTo(cb);
    }
}
