using System.Collections.Generic;
using UnityEngine;





public class VPL_Render : MonoBehaviour
{
    [Header("Move Light")]
    public Light pointLight = null;
    Vector3 postion1 = new Vector3(0, 9.2f, 1);
    Vector3 postion2 = new Vector3(0, 9.2f, -7);
    Vector3 postion3 = new Vector3(0, 3, -7);
    Vector3 postion4 = new Vector3(3, 3, -7);
    Vector3 postion5 = new Vector3(3, 3, 1);
    Vector3 postion6 = new Vector3(0, 3, 1);
    float speed = 0.02f;
    int moveStage = 0;
    GameObject visualizeLight = null;

    [Header("Options")]
    public bool includeInactiveLights = true;

    public int numberOfVPLs = 128; // 要生成的 VPL 數量
    public float rayMaxDistance = 100f;

    PbrtScene scene = null;

    [Header("Shadow Settings")]
    public int shadowMapResolution = 256; // 陰影貼圖的解析度
    public LayerMask shadowCasterLayers; // 指定哪些圖層的物件會產生陰影
    public float shadowNearClip = 0.01f;
    public float shadowFarClip = 100f;

    public bool isDynamic = true;
    public bool isMoveLight = false;

    List<VPL> vplList = new List<VPL>();
    List<GameObject> vplVisualizeSphere = new List<GameObject>();
    ComputeBuffer vplBuffer;
    Texture2DArray shadowmapArray; // 儲存所有陰影貼圖的紋理陣列
    List<Matrix4x4> shadowCameraVP = new List<Matrix4x4>(); // 渲染 shadow map 時，shadow camera 的 Projection * View Matrix
    ComputeBuffer shadowCameraVPBuffer;
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
        if (shadowCameraVPBuffer != null)
        {
            shadowCameraVPBuffer.Release();
            shadowCameraVPBuffer = null;
        }
    }

    void Start()
    {
        // PBRT 檔案路徑，相對於專案根目錄
        string sceneFileName = "sibenik-whitted.pbrt";
        string sceneFilePath = System.IO.Path.Combine(Application.streamingAssetsPath, sceneFileName);
        LoatScene(sceneFilePath);

        visualizeLight = CreateVplVisualizeSphere(pointLight.transform.position, 0.1f, Color.red);
        visualizeLight.transform.SetParent(pointLight.transform);
        visualizeLight.transform.localPosition = Vector3.zero;
        visualizeLight.layer = 2;
        visualizeLight.SetActive(false);

        // 1. 建立一個用於渲染陰影的隱藏相機
        CreateShadowCamera();

        // 2. 生成 VPLs
        GenerateVPLs();

        // 3. 為所有 VPL 渲染陰影貼圖
        RenderAllShadowMaps();

        // 4. 將 VPL 數據和陰影貼圖傳遞給所有 Shader
        SetupShaderGlobals();
    }

    void Update()
    {
        // 移動光源
        if (isMoveLight)
        {
            Vector3 target = Vector3.zero;
            switch (moveStage)
            {
                case 0:
                    target = postion1;
                    break;
                case 1:
                    target = postion2;
                    break;
                case 2:
                    target = postion3;
                    break;
                case 3:
                    target = postion4;
                    break;
                case 4:
                    target = postion5;
                    break;
                case 5:
                    target = postion6;
                    break;
            }
            Vector3 newPos = Vector3.MoveTowards(pointLight.transform.position, target, speed);
            pointLight.transform.position = newPos;
            if (newPos == target) moveStage = (moveStage + 1) % 6;
        }
        

        DeleteInvalidVPL();
        GenerateVPLs(!isDynamic);
        RenderAllShadowMaps();
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

    void GenerateVPLs(bool forceClearVPL = false)
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

        if (forceClearVPL) {
            vplList.Clear();
            ClearAllVplVisualizeSphere();
        }
        
        for (int j = 0; j < lights.Count; j++)
        {

            for (int i = vplList.Count; i < numberOfVPLs; i++)
            {
                // 從光源位置發射隨機方向的光線
                Vector3 randomDirection = Random.onUnitSphere;
                PbrtRay ray;
                ray.origin = new Vector3(lights[j].x, lights[j].y, lights[j].z);
                ray.dir = randomDirection;
                PbrtHitInfo hitInfo;

                if (scene.intersect(ray, new Interval(0, float.PositiveInfinity), out hitInfo))
                {
                    // 在碰撞點建立一個 VPL
                    VPL newVpl = new VPL();
                    newVpl.position = hitInfo.position + hitInfo.normal * 0.1f; // 稍微偏移以避免 z-fighting

                    // 顏色可以先用光源顏色，之後可以根據材質和衰減計算
                    newVpl.color = new Color(lightColors[j].x, lightColors[j].y, lightColors[j].z);

                    vplList.Add(newVpl);

                    // 建立可視化VPL球體
                    GameObject sphere = CreateVplVisualizeSphere(newVpl.position, 0.05f, Color.yellow);
                    sphere.SetActive(isVisualizeVpl);
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

        if (shadowmapArray != null)
        {
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
        shadowCameraVP.Clear();
        for (int i = 0; i < vplList.Count; i++)
        {
            shadowCamera.transform.position = vplList[i].position;
            for (int j = 0; j < 6; j++)
            {
                shadowCamera.transform.rotation = rotations[j];
                shadowCamera.Render();

                // 將渲染出的深度圖複製到紋理陣列的對應 slice
                int sliceIndex = i * 6 + j;
                Graphics.ConvertTexture(rt, 0, shadowmapArray, sliceIndex);
                shadowCameraVP.Add(GL.GetGPUProjectionMatrix(shadowCamera.projectionMatrix, false) * shadowCamera.worldToCameraMatrix);
                //Debug.Log(shadowCameraVP[^1] * new Vector4(0, 0, 0 ,1));
            }
        }

        // *** 修復點 4：在結束時銷毀 rt ***
        // 必須先移除 Camera 的引用，再銷毀 RenderTexture
        shadowCamera.targetTexture = null;
        rt.Release(); // 釋放臨時的 Render Texture 佔用的 GPU 記憶體
        UnityEngine.Object.Destroy(rt); // 銷毀 Unity 原生物件

        
    }

    void SetupShaderGlobals()
    {
        if (vplList.Count == 0) return;

        if (vplBuffer != null) vplBuffer.Release();
        if (shadowCameraVPBuffer != null) shadowCameraVPBuffer.Release();

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
        shadowCameraVPBuffer = new ComputeBuffer(shadowCameraVP.Count, sizeof(float) * 16);
        shadowCameraVPBuffer.SetData(shadowCameraVP);

        // 將緩衝區、紋理陣列和計數器設為全域變數，供所有 Shader 使用
        Shader.SetGlobalBuffer("_VPLs", vplBuffer);
        Shader.SetGlobalBuffer("_ShadowCamera_VP", shadowCameraVPBuffer);
        Shader.SetGlobalTexture("_ShadowMapArray", shadowmapArray);
        Shader.SetGlobalInt("_VPLCount", vplList.Count);
    }

    void DeleteInvalidVPL()
    {
        for(int i = vplList.Count - 1; i >= 0; i--)
        {
            if (IsObstructed(vplList[i].position, pointLight.transform.position))
            {
                vplList.RemoveAt(i);

                // *** 修復點 6：在銷毀 GameObject 之前，銷毀 material 實例 ***
                GameObject sphere = vplVisualizeSphere[i];
                MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    UnityEngine.Object.Destroy(renderer.material);
                }

                Destroy(vplVisualizeSphere[i]);
                vplVisualizeSphere.RemoveAt(i);
            }
        }
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

    /// <summary>
    /// 檢查 StartPoint 和 EndPoint 之間是否有任何物體遮擋。
    /// </summary>
    /// <param name="startPoint">射線的起點。</param>
    /// <param name="endPoint">射線的終點。</param>
    /// <param name="layerMask">要檢查的圖層遮罩。預設為 Default（一切可見的物體）。</param>
    /// <returns>如果有物體阻擋，返回 true；如果路徑暢通，返回 false。</returns>
    public static bool IsObstructed(Vector3 startPoint, Vector3 endPoint, int layerMask = ~0)
    {
        // 1. 計算方向和距離
        Vector3 direction = endPoint - startPoint;
        float distance = direction.magnitude;

        // 將方向向量正規化
        direction.Normalize();

        // 2. 執行射線投射
        // Physics.Raycast(起點, 方向, 距離, 圖層遮罩)
        // Raycast 會忽略其起點所在的 Collider。

        // 為了避免射線在起點的物體內部被立即阻擋，建議將起點稍微往外推（微小的偏移，例如 0.01f），
        // 尤其當 startPoint 位於物體的表面或邊緣時。
        // 但由於 Unity 的 Raycast 本身設計上會忽略起點所在 Collider，通常可以省略微移。

        // 執行 Raycast，如果有碰撞發生，Physics.Raycast 會回傳 true
        if (Physics.Raycast(startPoint, direction, distance - 0.5f, layerMask))
        {
            // 有物體被擊中，表示有遮擋
            return true;
        }

        // 沒有物體被擊中，路徑暢通
        return false;
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
    GameObject CreateVplVisualizeSphere(Vector3 position, float radius, Color color)
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
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color);

            // 確保材質的渲染模式是支援透明度的 (如果顏色有透明度)
            // 如果只需要純色，可以忽略這一步
            // material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            // material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            // material.SetInt("_ZWrite", 1);
            // material.DisableKeyword("_ALPHABLEND_ON");
            // material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            // material.SetInt("_Mode", (int)UnityEngine.Rendering.SurfaceType.Opaque); 
        }

        sphere.layer = 2;

        // 5. 返回建立的球體物件
        return sphere;
    }

    public void ClearAllVplVisualizeSphere()
    {
        for(int i = 0; i < vplVisualizeSphere.Count; i++)
        {
            GameObject sphere = vplVisualizeSphere[i];
            // *** 修復點 5：銷毀 material 實例 ***
            MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                // material 會返回一個新的實例或現有的實例，這是必須被銷毀的原生資源
                UnityEngine.Object.Destroy(renderer.material);
            }

            Destroy(sphere);
        }
        vplVisualizeSphere.Clear();
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

        visualizeLight.SetActive(isActive);
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

        Camera.main.transform.position = vplList[onlyOneVplIndex].position;
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

        Camera.main.transform.position = vplList[onlyOneVplIndex].position;
    }

    public int GetOnlyOneVplIndex()
    {
        return onlyOneVplIndex;
    }

    public void IncreaseVplNum()
    {
        numberOfVPLs = numberOfVPLs * 2;
        onlyOneVplIndex = Mathf.Clamp(onlyOneVplIndex, 0, numberOfVPLs - 1);
        GenerateVPLs(true);
        RenderAllShadowMaps();
        SetupShaderGlobals();
    }

    public void DecreaseVplNum()
    {
        if(numberOfVPLs > 1)
        {
            numberOfVPLs = numberOfVPLs / 2;
            onlyOneVplIndex = Mathf.Clamp(onlyOneVplIndex, 0, numberOfVPLs - 1);
            GenerateVPLs(true);
            RenderAllShadowMaps();
            SetupShaderGlobals();
        }
        
    }

    #endregion
}
