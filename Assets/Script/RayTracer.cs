using System.Linq;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class RayTracer : MonoBehaviour
{
    // 在 Inspector 中將 TextureDisplayShader.shader 拖入此欄位
    public Shader textureDisplayShader;

    private Texture2D _targetTexture;
    private Material _displayMaterial;
    private Camera _camera;

    private PbrtScene scene = null;

    void Start()
    {
        // PBRT 檔案路徑，相對於專案根目錄
        string sceneFileName = "sibenik-whitted.pbrt";
        string sceneFilePath = System.IO.Path.Combine(Application.streamingAssetsPath, sceneFileName);
        LoatScene(sceneFilePath);
        InitDisplayQuad();

        _camera.gameObject.transform.position = scene.camera.GetEyePosition();
        _camera.gameObject.transform.LookAt(scene.camera.GetEyePosition() + scene.camera.GetForward(), scene.camera.GetUp());
    }

    void Update()
    {
        // 這裡將是您執行光線追蹤計算的地方
        RenderSceneOnCPU();
    }

    void RenderSceneOnCPU()
    {
        // 迭代每個像素
        for (int y = 0; y < _targetTexture.height; y++)
        {
            for (int x = 0; x < _targetTexture.width; x++)
            {
                // TODO: 您的光線追蹤核心邏輯
                // Color pixelColor = YourRayTracingLogic(x, y);
                Color pixelColor = new Color((float)x / _targetTexture.width, (float)y / _targetTexture.height, 0); // 用一個漸層色作為範例
                _targetTexture.SetPixel(x, y, pixelColor);
                PbrtRay ray = scene.camera.GenerateRay(x, y);
                if (y == 0 && x == 0)
                {
                    Debug.DrawRay(ray.origin, ray.dir, Color.black, 50);
                }
            }
        }
        // 將所有像素顏色變更應用到 GPU
        _targetTexture.Apply();
    }

    void InitDisplayQuad()
    {
        _camera = GetComponent<Camera>();
        

        // Set camera
        Vector2Int resolution = scene.film.GetResolution();
        _camera.fieldOfView = scene.camera.GetFov();
        _camera.aspect = resolution.x / resolution.y;

        // 1. 建立用於顯示的 Texture 和 Material
        _targetTexture = new Texture2D(resolution.x, resolution.y, TextureFormat.RGBA32, false);
        _displayMaterial = new Material(textureDisplayShader);
        _displayMaterial.mainTexture = _targetTexture;

        // 2. 建立一個 Quad 作為攝影機的子物件，用於顯示畫面
        GameObject displayQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        displayQuad.transform.SetParent(_camera.transform);
        displayQuad.GetComponent<Renderer>().material = _displayMaterial;
        // 移除 Quad 的碰撞體，因為我們只需要它的顯示功能
        Destroy(displayQuad.GetComponent<MeshCollider>());

        // 3. 根據攝影機參數調整 Quad 的位置和大小，使其剛好填滿畫面
        // 將 Quad 放在近裁剪平面前方一點點的位置
        float quadPositionZ = _camera.nearClipPlane + 0.001f;

        // 計算在該距離下，攝影機視野的高度和寬度
        float quadHeight = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * quadPositionZ * 2f;
        float quadWidth = quadHeight * _camera.aspect;

        // 設定 Quad 的本地位置和縮放
        displayQuad.transform.localPosition = new Vector3(0, 0, quadPositionZ);
        displayQuad.transform.localScale = new Vector3(quadWidth, quadHeight, 1f);
    }

    void LoatScene(string sceneFilePath)
    {
        if (System.IO.File.Exists(sceneFilePath))
        {
            PbrtParser parser = new PbrtParser();
            scene = parser.Parse(sceneFilePath);
            scene.Init();

            LogSceneSummary(scene);
        }
        else
        {
            UnityEngine.Debug.LogError($"PBRT 場景檔案未找到: {sceneFilePath}");
        }
    }

    private void LogSceneSummary(PbrtScene scene)
    {
        if (scene == null) return;
        Debug.Log("--- PBRT Scene Summary ---");

        int sphereCount = scene.shapes.Count(s => s is PbrtSphere);
        int cylinderCount = scene.shapes.Count(s => s is PbrtCylinder);
        int meshCount = scene.shapes.Count(s => s is PbrtTriangleMesh);

        Debug.Log($"Total Shapes: {scene.shapes.Count}");
        Debug.Log($"  - Spheres: {sphereCount}");
        Debug.Log($"  - Cylinders: {cylinderCount}");
        Debug.Log($"  - Triangle Meshes: {meshCount}");

        Debug.Log("Camera");
        Debug.Log($"  - FOV: {scene.camera.GetFov()}");
        Debug.Log($"  - Eye: {scene.camera.GetEyePosition()}");
        Debug.Log($"  - Forward: {scene.camera.GetForward()}");
        Debug.Log($"  - Right: {scene.camera.GetRight()}");
        Debug.Log($"  - Up: {scene.camera.GetUp()}");

        //// --- NEW: Log material info for the first few shapes to verify ---
        //Debug.Log("--- Material Verification ---");
        //for (int i = 0; i < Mathf.Min(scene.shapes.Count, 5); i++)
        //{
        //    var shape = scene.shapes[i];
        //    string materialInfo = shape.material != null ? shape.material.ToString() : "No Material";
        //    Debug.Log($"Shape {i} ({shape.GetType().Name}) -> {materialInfo}");
        //}
    }
}