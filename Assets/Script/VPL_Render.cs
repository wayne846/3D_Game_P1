using System.Collections.Generic;
using UnityEngine;





public class VPL_Render : MonoBehaviour
{
    [Header("Options")]
    public bool includeInactiveLights = true;

    public int numberOfVPLs = 128; // 要生成的 VPL 數量
    public float rayMaxDistance = 100f;

    PbrtScene scene = null;

    [Header("Shadow Settings")]
    public int shadowMapResolution = 256; // 陰影貼圖的解析度
    public LayerMask shadowCasterLayers; // 指定哪些圖層的物件會產生陰影
    public float shadowNearClip = 0.1f;
    public float shadowFarClip = 100f;

    List<VPL> vplList = new List<VPL>();
    List<GameObject> vplVisualizeSphere = new List<GameObject>();
    ComputeBuffer vplBuffer;
    Texture2DArray shadowmapArray; // 儲存所有陰影貼圖的紋理陣列
    Camera shadowCamera;

    bool isVisualizeVpl = false;
    bool isOnlyOneVpl = false;
    int onlyOneVplIndex = 0;

    public struct VPL
    {
        public Vector3 position;
        public Color color;
    }

    // 這個結構將被傳遞給 GPU
    private struct VPLDataForGPU
    {
        public Vector3 position;
        public Vector4 color;
    }

    void OnDisable()
    {
        // 釋放緩衝區
        if (vplBuffer != null)
        {
            vplBuffer.Release();
            vplBuffer = null;
        }
    }

    void Start()
    {
        // PBRT 檔案路徑，相對於專案根目錄
        string sceneFileName = "sibenik-whitted.pbrt";
        string sceneFilePath = System.IO.Path.Combine(Application.streamingAssetsPath, sceneFileName);
        LoatScene(sceneFilePath);

        // 1. 建立一個用於渲染陰影的隱藏相機
        CreateShadowCamera();

        // 2. 生成 VPLs
        GenerateVPLs();

        // ComputeBuffer 需要知道結構大小
        // Vector3 (3*4 bytes) + Color (4*4 bytes) = 12 + 16 = 28 bytes
        vplBuffer = new ComputeBuffer(numberOfVPLs, 28);
        vplBuffer.SetData(vplList);

        // 將 VPL 數據和數量設為 Shader 全域變數
        Shader.SetGlobalBuffer("_VPLs", vplBuffer);
        Shader.SetGlobalInt("_VPLCount", vplList.Count);

        // 3. 為所有 VPL 渲染陰影貼圖
        RenderAllShadowMaps();

        // 4. 將 VPL 數據和陰影貼圖傳遞給所有 Shader
        SetupShaderGlobals();
    }

    void CreateShadowCamera()
    {
        GameObject camGO = new GameObject("ShadowCamera");
        //camGO.hideFlags = HideFlags.HideAndDontSave; // 避免在場景中看到或儲存它
        shadowCamera = camGO.AddComponent<Camera>();
        shadowCamera.enabled = false; // 我們只手動呼叫它的 Render()
        shadowCamera.cullingMask = shadowCasterLayers;
        shadowCamera.nearClipPlane = shadowNearClip;
        shadowCamera.farClipPlane = shadowFarClip;
        shadowCamera.aspect = 1.0f;
        shadowCamera.fieldOfView = 90.0f;
    }

    void GenerateVPLs()
    {
        var lights = new List<Vector4>();
        var lightColors = new List<Vector3>();
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
            lightColors.Add(new Vector3(c.r, c.g, c.b) * lt.intensity / numberOfVPLs);
        }

        vplList.Clear();
        for (int j = 0; j < lights.Count; j++)
        {

            for (int i = 0; i < numberOfVPLs; i++)
            {
                // 從光源位置發射隨機方向的光線
                Vector3 randomDirection = Random.onUnitSphere;
                PbrtRay ray;
                ray.origin = new Vector3(lights[j].x, lights[j].y, lights[j].z);
                ray.dir = randomDirection;
                PbrtHitInfo hitInfo;

                if (scene.intersect(ray, new Interval(0, float.PositiveInfinity), out hitInfo));
                {
                    // 在碰撞點建立一個 VPL
                    VPL newVpl = new VPL();
                    newVpl.position = hitInfo.position + hitInfo.normal * 0.001f; // 稍微偏移以避免 z-fighting

                    // 顏色可以先用光源顏色，之後可以根據材質和衰減計算
                    newVpl.color = new Color(lightColors[j].x, lightColors[j].y, lightColors[j].z);

                    vplList.Add(newVpl);

                    // 建立可視化VPL球體
                    GameObject sphere = CreateVplVisualizeSphere(newVpl.position, 0.05f, Color.yellow);
                    sphere.SetActive(false);
                    vplVisualizeSphere.Add(sphere);
                }
            }
        }
        
    }

    void RenderAllShadowMaps()
    {
        if (vplList.Count == 0)
        {
            // 額外處理：如果沒有 VPL，也應該確保舊的 Texture2DArray 被清理
            if (shadowmapArray != null)
            {
                UnityEngine.Object.Destroy(shadowmapArray);
                shadowmapArray = null;
            }
            return;
        }

        // *** 關鍵修復點：在創建新的 Texture2DArray 之前，銷毀舊的 ***
        if (shadowmapArray != null)
        {
            // 使用 UnityEngine.Object.Destroy 銷毀 Unity 原生資源
            UnityEngine.Object.Destroy(shadowmapArray);
        }

        // 建立 Texture2DArray 來儲存所有 Cubemap 的6個面
        // 每個 VPL 需要6個 slice (一個 Cubemap 的6個面)
        shadowmapArray = new Texture2DArray(
            shadowMapResolution, shadowMapResolution,
            vplList.Count * 6, TextureFormat.RFloat, false);

        shadowmapArray.filterMode = FilterMode.Bilinear;
        shadowmapArray.wrapMode = TextureWrapMode.Clamp;

        RenderTexture rt = new RenderTexture(shadowMapResolution, shadowMapResolution, 24, RenderTextureFormat.Depth);
        rt.Create();
        shadowCamera.targetTexture = rt;

        // Cubemap 6個面的渲染方向
        Vector3[] directions = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        Quaternion[] rotations = {
            Quaternion.LookRotation(Vector3.right), Quaternion.LookRotation(Vector3.left),
            Quaternion.LookRotation(Vector3.up, Vector3.back), Quaternion.LookRotation(Vector3.down, Vector3.forward),
            Quaternion.LookRotation(Vector3.forward), Quaternion.LookRotation(Vector3.back)
        };

        for (int i = 0; i < vplList.Count; i++)
        {
            shadowCamera.transform.position = vplList[i].position;
            for (int j = 0; j < 6; j++)
            {
                shadowCamera.transform.rotation = rotations[j];
                shadowCamera.Render();

                // 將渲染出的深度圖複製到紋理陣列的對應 slice
                int sliceIndex = i * 6 + j;
                Graphics.CopyTexture(rt, 0, 0, shadowmapArray, sliceIndex, 0);
            }
        }

        rt.Release(); // 釋放臨時的 Render Texture
    }

    void SetupShaderGlobals()
    {
        if (vplList.Count == 0) return;

        // 準備要傳給 GPU 的數據
        VPLDataForGPU[] vplData = new VPLDataForGPU[vplList.Count];
        for (int i = 0; i < vplList.Count; i++)
        {
            if (isOnlyOneVpl)
            {
                // 只有特定的vpl才會傳
                if(i == onlyOneVplIndex)
                {
                    vplData[i] = new VPLDataForGPU { position = vplList[i].position, color = vplList[i].color };
                }
            }
            else
            {
                vplData[i] = new VPLDataForGPU { position = vplList[i].position, color = vplList[i].color };
            }
        }

        // 建立並設定 ComputeBuffer
        vplBuffer = new ComputeBuffer(vplList.Count, sizeof(float) * 7); // Vector3 (3 floats) + Vector4 (4 floats)
        vplBuffer.SetData(vplData);

        // 將緩衝區、紋理陣列和計數器設為全域變數，供所有 Shader 使用
        Shader.SetGlobalBuffer("_VPLs", vplBuffer);
        Shader.SetGlobalTexture("_ShadowMapArray", shadowmapArray);
        Shader.SetGlobalInt("_VPLCount", vplList.Count);
        Shader.SetGlobalFloat("_ShadowFarPlane", shadowFarClip);
    }

    void LoatScene(string sceneFilePath)
    {
        if (System.IO.File.Exists(sceneFilePath))
        {
            PbrtParser parser = new PbrtParser();
            scene = parser.Parse(sceneFilePath);
            scene.Init();
        }
        else
        {
            UnityEngine.Debug.LogError($"PBRT 場景檔案未找到: {sceneFilePath}");
        }
    }


    #region Visualize
    void OnDrawGizmos()
    {
        if (vplList != null)
        {
            foreach (var vpl in vplList)
            {
                Gizmos.color = vpl.color;
                Gizmos.DrawSphere(vpl.position, 0.05f); // 繪製一個小球體代表 VPL
            }
        }
    }

    /// <summary>
    /// 動態建立一個球體 GameObject
    /// </summary>
    /// <param name="position">球體在世界空間中的位置</param>
    /// <param name="radius">球體的半徑 (透過縮放來實現)</param>
    /// <param name="color">球體的顏色</param>
    /// <returns>建立的球體 GameObject</returns>
    public GameObject CreateVplVisualizeSphere(Vector3 position, float radius, Color color)
    {
        // 1. 建立一個預設的 3D 球體基本體 (Primitive)
        // Unity 內建的球體半徑預設為 0.5 (直徑為 1 單位)
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

        // 2. 指定位置
        sphere.transform.position = position;

        // 3. 指定半徑 (透過縮放 Sacle 來實現)
        // 由於預設球體的直徑為 1，要達到指定的半徑 R，
        // 則直徑為 2R，因此縮放因子為 (2 * radius)
        float scaleFactor = radius * 2f;
        sphere.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);

        // 4. 指定顏色 (設定材質)

        // 取得球體上的 MeshRenderer 元件
        MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();

        if (renderer != null)
        {
            // 為了不影響其他使用相同材質的物件，建議使用 .material 而非 .sharedMaterial
            // 註: 每次使用 .material 會建立一個新的材質實例
            Material material = renderer.material;

            // 將材質的顏色設定為指定顏色
            // 這是針對 Standard Shader 最常見的設定方式
            material.color = color;

            // 確保材質的渲染模式是支援透明度的 (如果顏色有透明度)
            // 如果只需要純色，可以忽略這一步
            // material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            // material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            // material.SetInt("_ZWrite", 1);
            // material.DisableKeyword("_ALPHABLEND_ON");
            // material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            // material.SetInt("_Mode", (int)UnityEngine.Rendering.SurfaceType.Opaque); 
        }

        // 5. 返回建立的球體物件
        return sphere;
    }

    public void SetVplVisualizeSphereActive(bool isActive)
    {
        isVisualizeVpl = isActive;

        for (int i = 0; i < vplVisualizeSphere.Count; i++)
        {
            if(isOnlyOneVpl)
            {
                bool b = (i == onlyOneVplIndex) ? isActive : false;
                vplVisualizeSphere[i].SetActive(b);
            }
            else
            {
                vplVisualizeSphere[i].SetActive(isActive);
            }
        }
    }

    public void SetIsOnlyOneVpl(bool b)
    {
        isOnlyOneVpl = b;

        SetupShaderGlobals();


        if (isVisualizeVpl)
        {
            SetVplVisualizeSphereActive(true);
        }
    }

    public void IncreaseOnlyOneVplIndex()
    {

        onlyOneVplIndex += 1;


        if (onlyOneVplIndex >= vplList.Count)
        {
            onlyOneVplIndex = vplList.Count - 1;
        }

        if (isOnlyOneVpl)
        {
            SetupShaderGlobals();
        }

        if (isVisualizeVpl)
        {
            SetVplVisualizeSphereActive(true);
        }
    }


    public void DecreseOnlyOneVplIndex()
    {
        onlyOneVplIndex -= 1;

        if (onlyOneVplIndex < 0)
        {
            onlyOneVplIndex = 0;
        }

        if (isOnlyOneVpl)
        {
            SetupShaderGlobals();
        }

        if (isVisualizeVpl)
        {
            SetVplVisualizeSphereActive(true);
        }
    }

    public int GetOnlyOneVplIndex()
    {
        return onlyOneVplIndex;
    }

    #endregion
}
