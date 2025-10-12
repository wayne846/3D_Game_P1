using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

[DefaultExecutionOrder(-10)]
public class MeshToCompute_FromSceneMeshes_AndPbrtTextures : MonoBehaviour
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

    // GPU buffers
    ComputeBuffer _meshObjBuf, _indicesBuf, _vBuf, _nBuf, _uvBuf, _lightBuf, _lightColorsBuf;
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
    }

    public void BuildAndUpload()
    {
        // texture array
        var textures = new List<Texture2D>();
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

            // n v uv
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
            else
            {
                allUV.AddRange(FillVec2(V.Length, Vector2.zero));
            }

            //index and texture
            int subMeshCount = mesh.subMeshCount;
            var mats = mr.sharedMaterials;

            for (int sm = 0; sm < subMeshCount; sm++)
            {
                // index
                int indicesOffset = allIndices.Count;
                var ids = mesh.GetTriangles(sm);
                for (int i = 0; i < ids.Length; i++)
                    allIndices.Add(vtxOffset + ids[i]);

                // kd ks kt
                Vector4 Kd = new Vector4(1, 1, 1, 0);
                Vector4 Ks = new Vector4(0, 0, 0, 0);
                Vector4 Kt = new Vector4(0, 0, 0, 0);

                var mat = (sm < mats.Length) ? mats[sm] : null;
                if (mat)
                {
                    // base color
                    var baseCol = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                               : mat.HasProperty("_Color") ? mat.color
                               : Color.white;

                    // smoothness / glossiness
                    float smooth = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness")
                                : mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness")
                                : 0f;

                    // specular color
                    var specCol = mat.HasProperty("_SpecColor") ? mat.GetColor("_SpecColor") : Color.black;

                    // texture
                    Texture2D mainTex = mat.mainTexture as Texture2D;
                    //if (mat.HasProperty("_BaseMap")) mainTex = mat.GetTexture("_BaseMap");
                    //else if (mat.HasProperty("_MainTex")) mainTex = mat.GetTexture("_MainTex");

                    if (mainTex)
                    {
                        for (int layer = 0; layer < textures.Count; ++layer)
                        {
                            if (textures[layer] == mainTex)
                            {
                                Kd = new Vector4(layer, 0, 0, -1);
                                break;
                            }
                        }

                        if (Kd.w == 0)
                        {
                            textures.Add(mainTex);
                            Kd = new Vector4(textures.Count - 1, 0, 0, -1);
                        }
                    }
                    else
                        Kd = new Vector4(baseCol.r, baseCol.g, baseCol.b, 0);

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

        // light
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

                Color c = lt.color;
                lightColors.Add(new Vector3(c.r, c.g, c.b) * lt.intensity);
            }

        }

        // upload
        BuildTextureArray(textures);
        Upload(ref _meshObjBuf, meshObjs);
        Upload(ref _indicesBuf, allIndices);
        Upload(ref _vBuf, allV);
        Upload(ref _nBuf, allN);
        Upload(ref _uvBuf, allUV);
        Upload(ref _lightBuf, lights);
        Upload(ref _lightColorsBuf, lightColors);

        var cs = rayTracingCompute;
        cs.SetBuffer(_kernel, "_MeshObjects", _meshObjBuf);
        cs.SetBuffer(_kernel, "_Indices", _indicesBuf);
        cs.SetBuffer(_kernel, "_Vertices", _vBuf);
        cs.SetBuffer(_kernel, "_Normals", _nBuf);
        cs.SetBuffer(_kernel, "_UVs", _uvBuf);
        cs.SetBuffer(_kernel, "_Lights", _lightBuf);
        cs.SetBuffer(_kernel, "_LightColors", _lightColorsBuf);

        if (_texArray)
        {
            cs.SetTexture(_kernel, "_Textures", _texArray);
            cs.SetInt("_TextureCount", _texArray.depth);
        }
        else cs.SetInt("_TextureCount", 0);

        // Debug
        meshObjectCount = meshObjs.Count;
        vertexCount = allV.Count;
        indexCount = allIndices.Count;
        lightCount = lights.Count;
        textureLayerCount = _texArray ? _texArray.depth : 0;

        Debug.Log($"[MeshToCompute] objs:{meshObjectCount}, v:{vertexCount}, i:{indexCount}, lights:{lightCount}, texLayers:{textureLayerCount}");
        
    }

    void BuildTextureArray(List<Texture2D> list)
    {
        if (_texArray) { Destroy(_texArray); _texArray = null; }

        if (list.Count == 0) return;


        const int w = 640, h = 640;
        // 將所有 texture 縮放至 640 * 640
        for (int j = 0; j < list.Count; j++)
        {
            if (list[j].format != TextureFormat.RGBA32 || list[j].width != w || list[j].height != h)
            {
                var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(list[j], rt);
                var tmp = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tmp.Apply();
                RenderTexture.active = prev;
                rt.Release();
                list[j] = tmp;
            }
        }

        _texArray = new Texture2DArray(w, h, list.Count, TextureFormat.RGBA32, true, true);
        _texArray.filterMode = FilterMode.Trilinear;
        _texArray.wrapMode = TextureWrapMode.Clamp;

        for (int layer = 0; layer < list.Count; layer++)
        {
            _texArray.SetPixels(list[layer].GetPixels(), layer);
        }
        _texArray.Apply(false, false);
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
    
}
