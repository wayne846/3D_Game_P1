using System.Linq;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class RayTracer : MonoBehaviour
{
    // �b Inspector ���N TextureDisplayShader.shader ��J�����
    public Shader textureDisplayShader;

    private Texture2D _targetTexture;
    private Material _displayMaterial;
    private Camera _camera;

    void Start()
    {
        _camera = GetComponent<Camera>();

        // 1. �إߥΩ���ܪ� Texture �M Material
        _targetTexture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        _displayMaterial = new Material(textureDisplayShader);
        _displayMaterial.mainTexture = _targetTexture;

        // 2. �إߤ@�� Quad �@����v�����l����A�Ω���ܵe��
        GameObject displayQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        displayQuad.transform.SetParent(_camera.transform);
        displayQuad.GetComponent<Renderer>().material = _displayMaterial;
        // ���� Quad ���I����A�]���ڭ̥u�ݭn������ܥ\��
        Destroy(displayQuad.GetComponent<MeshCollider>());

        // 3. �ھ���v���Ѽƽվ� Quad ����m�M�j�p�A�Ϩ��n�񺡵e��
        // �N Quad ��b����ť����e��@�I�I����m
        float quadPositionZ = _camera.nearClipPlane + 0.01f;

        // �p��b�ӶZ���U�A��v�����������שM�e��
        float quadHeight = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * quadPositionZ * 2f;
        float quadWidth = quadHeight * _camera.aspect;

        // �]�w Quad �����a��m�M�Y��
        displayQuad.transform.localPosition = new Vector3(0, 0, quadPositionZ);
        displayQuad.transform.localScale = new Vector3(quadWidth, quadHeight, 1f);

        // PBRT �ɮ׸��|�A�۹��M�׮ڥؿ�
        string sceneFileName = "sibenik-whitted.pbrt";
        string sceneFilePath = System.IO.Path.Combine(Application.streamingAssetsPath, sceneFileName);

        if (System.IO.File.Exists(sceneFilePath))
        {
            PbrtParser parser = new PbrtParser();
            PbrtScene scene = parser.Parse(sceneFilePath);
            LogSceneSummary(scene);
        }
        else
        {
            UnityEngine.Debug.LogError($"PBRT �����ɮץ����: {sceneFilePath}");
        }
    }

    void Update()
    {
        // �o�̱N�O�z������u�l�ܭp�⪺�a��
        RenderSceneOnCPU();
    }

    void RenderSceneOnCPU()
    {
        // ���N�C�ӹ���
        for (int y = 0; y < _targetTexture.height; y++)
        {
            for (int x = 0; x < _targetTexture.width; x++)
            {
                // TODO: �z�����u�l�ܮ֤��޿�
                // Color pixelColor = YourRayTracingLogic(x, y);
                Color pixelColor = new Color((float)x / _targetTexture.width, (float)y / _targetTexture.height, 0); // �Τ@�Ӻ��h��@���d��
                _targetTexture.SetPixel(x, y, pixelColor);
            }
        }
        // �N�Ҧ������C���ܧ����Ψ� GPU
        _targetTexture.Apply();
    }

    private void LogSceneSummary(PbrtScene scene)
    {
        if (scene == null) return;
        Debug.Log("--- PBRT Scene Summary ---");

        int sphereCount = scene.Shapes.Count(s => s is PbrtSphere);
        int cylinderCount = scene.Shapes.Count(s => s is PbrtCylinder);
        int meshCount = scene.Shapes.Count(s => s is PbrtTriangleMesh);

        Debug.Log($"Total Shapes: {scene.Shapes.Count}");
        Debug.Log($"  - Spheres: {sphereCount}");
        Debug.Log($"  - Cylinders: {cylinderCount}");
        Debug.Log($"  - Triangle Meshes: {meshCount}");

        // --- NEW: Log material info for the first few shapes to verify ---
        Debug.Log("--- Material Verification ---");
        for (int i = 0; i < Mathf.Min(scene.Shapes.Count, 5); i++)
        {
            var shape = scene.Shapes[i];
            string materialInfo = shape.Material != null ? shape.Material.ToString() : "No Material";
            Debug.Log($"Shape {i} ({shape.GetType().Name}) -> {materialInfo}");
        }
    }
}