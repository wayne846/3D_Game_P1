using System.Collections.Generic;
using UnityEngine;

public class VPL_Render_shadowMap : MonoBehaviour
{
    [Header("Options")]
    public bool includeInactiveLights = true;
    public int numberOfVPLs = 128;
    public float rayMaxDistance = 100f;

    private PbrtScene scene = null;

    [Header("Shadow Settings")]
    public int shadowMapResolution = 256;
    public LayerMask shadowCasterLayers = ~0;
    public float shadowNearClip = 0.1f;
    public float shadowFarClip = 100f;

    [Header("One-shot output")]
    public bool savePNGOnceAtStart = true;

    private List<VPL> vplList = new List<VPL>();
    private ComputeBuffer vplBuffer;
    private Texture2DArray shadowmapArray;
    private Camera shadowCamera;
    private Material depthCopyMat;

    private Shader vplDepthShader;

    public struct VPL { public Vector3 position; public Color color; }

    private struct VPLDataForGPU
    {
        public Vector3 position;
        public Vector4 color;
    }

    void Start()
    {
        string sceneFile = "sibenik-whitted.pbrt";
        string sceneFilePath = System.IO.Path.Combine(Application.streamingAssetsPath, sceneFile);
        LoadScene(sceneFilePath);

        CreateShadowCamera();

        vplDepthShader = Shader.Find("Hidden/VPLDepth");
        if (!vplDepthShader)
        {
            Debug.LogError("Missing shader Hidden/VPLDepth. Put the shader into project.");
            enabled = false; return;
        }

        GenerateVPLs();

        RenderAllShadowMaps();

        SetupShaderGlobals();

        if (savePNGOnceAtStart) SaveShadowMapsToDisk();
    }

    void OnDisable()
    {
        if (vplBuffer != null) { vplBuffer.Release(); vplBuffer = null; }
    }

    void CreateShadowCamera()
    {
        GameObject camGO = new GameObject("ShadowCamera");
        shadowCamera = camGO.AddComponent<Camera>();
        shadowCamera.enabled = false;
        shadowCamera.cullingMask = shadowCasterLayers;
        shadowCamera.nearClipPlane = shadowNearClip;
        shadowCamera.farClipPlane = shadowFarClip;
        shadowCamera.clearFlags = CameraClearFlags.SolidColor;
        shadowCamera.backgroundColor = Color.white;
        shadowCamera.allowMSAA = false;
        shadowCamera.allowHDR = false;
        shadowCamera.aspect = 1.0f;
        shadowCamera.fieldOfView = 90.0f;
    }

    void GenerateVPLs()
    {
        vplList.Clear();

        var inactive = includeInactiveLights ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
        var lights = FindObjectsByType<Light>(inactive, FindObjectsSortMode.None);

        foreach (var lt in lights)
        {
            Vector3 Lpos = lt.transform.position;

            for (int i = 0; i < numberOfVPLs; i++)
            {
                Vector3 dir = Random.onUnitSphere;

                if (scene != null)
                {
                    PbrtRay ray; ray.origin = Lpos; ray.dir = dir;
                    PbrtHitInfo hit;
                    if (scene.intersect(ray, new Interval(0, float.PositiveInfinity), out hit))
                    {
                        VPL v;
                        v.position = hit.position + hit.normal * 0.001f;
                        var c = lt.color * lt.intensity / Mathf.Max(1, numberOfVPLs);
                        v.color = new Color(c.r, c.g, c.b, 1);
                        vplList.Add(v);
                    }
                }
                else
                {
                    VPL v;
                    v.position = Lpos + dir * 0.5f;
                    var c = lt.color * lt.intensity / Mathf.Max(1, numberOfVPLs);
                    v.color = new Color(c.r, c.g, c.b, 1);
                    vplList.Add(v);
                }
            }
        }

        if (vplList.Count == 0)
            Debug.LogWarning("No VPL generated.");
        else
            Debug.Log($"Generated {vplList.Count} VPLs.");
    }

    void RenderAllShadowMaps()
    {
        if (vplList.Count == 0) return;

        int sliceCount = vplList.Count * 6;
        shadowmapArray = new Texture2DArray(
            shadowMapResolution, shadowMapResolution,
            sliceCount, TextureFormat.RFloat,  false,  true);
        shadowmapArray.filterMode = FilterMode.Bilinear;
        shadowmapArray.wrapMode = TextureWrapMode.Clamp;

        var colorRT = new RenderTexture(
            shadowMapResolution, shadowMapResolution, 24,
            RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        colorRT.useMipMap = false;
        colorRT.autoGenerateMips = false;
        colorRT.antiAliasing = 1;
        colorRT.Create();
        shadowCamera.targetTexture = colorRT;

        shadowCamera.SetReplacementShader(vplDepthShader, "RenderType");

        Quaternion[] faceRot =
        {
            Quaternion.LookRotation(Vector3.right,   Vector3.up),
            Quaternion.LookRotation(Vector3.left,    Vector3.up),
            Quaternion.LookRotation(Vector3.up,      Vector3.back),
            Quaternion.LookRotation(Vector3.down,    Vector3.forward),
            Quaternion.LookRotation(Vector3.forward, Vector3.up),
            Quaternion.LookRotation(Vector3.back,    Vector3.up),
        };

        int slice = 0;
        for (int i = 0; i < vplList.Count; i++)
        {
            shadowCamera.transform.position = vplList[i].position;

            for (int f = 0; f < 6; f++, slice++)
            {
                shadowCamera.transform.rotation = faceRot[f];

                GL.Clear(true, true, Color.white);

                shadowCamera.Render();

                Graphics.CopyTexture(colorRT, 0, 0, shadowmapArray, slice, 0);
            }
        }

        shadowCamera.ResetReplacementShader();
        shadowCamera.targetTexture = null;
        colorRT.Release();
        DestroyImmediate(colorRT);

        Debug.Log($"Rendered VPL shadow maps: {sliceCount} slices ({vplList.Count} VPL × 6 faces).");
    }

    void SetupShaderGlobals()
    {
        if (vplList.Count == 0) return;

        var data = new VPLDataForGPU[vplList.Count];
        for (int i = 0; i < vplList.Count; i++)
            data[i] = new VPLDataForGPU { position = vplList[i].position, color = vplList[i].color };

        vplBuffer = new ComputeBuffer(vplList.Count, sizeof(float) * 7);
        vplBuffer.SetData(data);

        Shader.SetGlobalBuffer("_VPLs", vplBuffer);
        Shader.SetGlobalInt("_VPLCount", vplList.Count);
        Shader.SetGlobalTexture("_VPLShadowMaps", shadowmapArray);
        Shader.SetGlobalFloat("_VPLShadowFarPlane", shadowFarClip);
    }

    void LoadScene(string sceneFilePath)
    {
        if (System.IO.File.Exists(sceneFilePath))
        {
            var parser = new PbrtParser();
            scene = parser.Parse(sceneFilePath);
            scene.Init();
        }
        else
        {
            Debug.LogWarning($"PBRT scene file not found: {sceneFilePath}. VPLs will be placed near light instead.");
        }
    }

    void SaveShadowMapsToDisk()
    {
        if (shadowmapArray == null) return;

        System.IO.Directory.CreateDirectory($"{Application.dataPath}/VPL_ShadowMaps");

        int width  = shadowmapArray.width;
        int height = shadowmapArray.height;
        int depth  = shadowmapArray.depth;

        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = false,
            useMipMap = false,
            autoGenerateMips = false
        };
        rt.Create();

        for (int i = 0; i < depth; i++)
        {
            Graphics.CopyTexture(shadowmapArray, i, 0, rt, 0, 0);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RFloat, false, true);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            byte[] bytes = tex.EncodeToPNG();
            string path = $"{Application.dataPath}/VPL_ShadowMaps/vpl_{i:D3}.png";
            System.IO.File.WriteAllBytes(path, bytes);

            DestroyImmediate(tex);
        }

        rt.Release();
        DestroyImmediate(rt);

        Debug.Log($"Saved {depth} slices to Assets/VPL_ShadowMaps/");
    }

    void OnDrawGizmos()
    {
        if (vplList == null) return;
        foreach (var vpl in vplList)
        {
            Gizmos.color = vpl.color;
            Gizmos.DrawSphere(vpl.position, 0.05f);
        }
    }
}
