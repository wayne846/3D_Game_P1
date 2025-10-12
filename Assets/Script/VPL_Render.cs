using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;





public class VPL_Render : MonoBehaviour
{
    [Header("Options")]
    public bool includeInactiveLights = true;

    public int numberOfVPLs = 128; // �n�ͦ��� VPL �ƶq
    public float rayMaxDistance = 100f;

    private PbrtScene scene = null;

    [Header("Shadow Settings")]
    public int shadowMapResolution = 256; // ���v�K�Ϫ��ѪR��
    public LayerMask shadowCasterLayers; // ���w���ǹϼh������|���ͳ��v
    public float shadowNearClip = 0.1f;
    public float shadowFarClip = 100f;

    private List<VPL> vplList = new List<VPL>();
    private ComputeBuffer vplBuffer;
    private Texture2DArray shadowmapArray; // �x�s�Ҧ����v�K�Ϫ����z�}�C
    private Camera shadowCamera;

    public struct VPL
    {
        public Vector3 position;
        public Color color;
    }

    // �o�ӵ��c�N�Q�ǻ��� GPU
    private struct VPLDataForGPU
    {
        public Vector3 position;
        public Vector4 color;
    }

    void OnEnable()
    {
        GenerateVPLs();

        // ComputeBuffer �ݭn���D���c�j�p
        // Vector3 (3*4 bytes) + Color (4*4 bytes) = 12 + 16 = 28 bytes
        vplBuffer = new ComputeBuffer(numberOfVPLs, 28);
        vplBuffer.SetData(vplList);

        // �N VPL �ƾکM�ƶq�]�� Shader �����ܼ�
        Shader.SetGlobalBuffer("_VPLs", vplBuffer);
        Shader.SetGlobalInt("_VPLCount", vplList.Count);
    }

    void OnDisable()
    {
        // ����w�İ�
        if (vplBuffer != null)
        {
            vplBuffer.Release();
            vplBuffer = null;
        }
    }

    void Start()
    {
        // PBRT �ɮ׸��|�A�۹��M�׮ڥؿ�
        string sceneFileName = "sibenik-whitted.pbrt";
        string sceneFilePath = System.IO.Path.Combine(Application.streamingAssetsPath, sceneFileName);
        LoatScene(sceneFilePath);

        // 1. �إߤ@�ӥΩ��V���v�����ì۾�
        CreateShadowCamera();

        // 2. �ͦ� VPLs
        GenerateVPLs();

        // 3. ���Ҧ� VPL ��V���v�K��
        RenderAllShadowMaps();

        // 4. �N VPL �ƾکM���v�K�϶ǻ����Ҧ� Shader
        SetupShaderGlobals();
    }

    void CreateShadowCamera()
    {
        GameObject camGO = new GameObject("ShadowCamera");
        //camGO.hideFlags = HideFlags.HideAndDontSave; // �קK�b�������ݨ���x�s��
        shadowCamera = camGO.AddComponent<Camera>();
        shadowCamera.enabled = false; // �ڭ̥u��ʩI�s���� Render()
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
                // �q������m�o�g�H����V�����u
                Vector3 randomDirection = Random.onUnitSphere;
                PbrtRay ray;
                ray.origin = new Vector3(lights[j].x, lights[j].y, lights[j].z);
                ray.dir = randomDirection;
                PbrtHitInfo hitInfo;

                if (scene.intersect(ray, new Interval(0, float.PositiveInfinity), out hitInfo));
                {
                    // �b�I���I�إߤ@�� VPL
                    VPL newVpl = new VPL();
                    newVpl.position = hitInfo.position + hitInfo.normal * 0.001f; // �y�L�����H�קK z-fighting

                    // �C��i�H���Υ����C��A����i�H�ھڧ���M�I��p��
                    newVpl.color = new Color(lightColors[j].x, lightColors[j].y, lightColors[j].z);

                    vplList.Add(newVpl);
                }
            }
        }
        
    }

    void RenderAllShadowMaps()
    {
        if (vplList.Count == 0) return;

        // �إ� Texture2DArray ���x�s�Ҧ� Cubemap ��6�ӭ�
        // �C�� VPL �ݭn6�� slice (�@�� Cubemap ��6�ӭ�)
        shadowmapArray = new Texture2DArray(
            shadowMapResolution, shadowMapResolution,
            vplList.Count * 6, TextureFormat.RFloat, false);

        shadowmapArray.filterMode = FilterMode.Bilinear;
        shadowmapArray.wrapMode = TextureWrapMode.Clamp;

        RenderTexture rt = new RenderTexture(shadowMapResolution, shadowMapResolution, 24, RenderTextureFormat.Depth);
        rt.Create();
        shadowCamera.targetTexture = rt;

        // Cubemap 6�ӭ�����V��V
        Vector3[] directions = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        Quaternion[] rotations = {
            Quaternion.LookRotation(Vector3.right), Quaternion.LookRotation(Vector3.left),
            Quaternion.LookRotation(Vector3.up, Vector3.back), Quaternion.LookRotation(Vector3.down, Vector3.forward),
            Quaternion.LookRotation(Vector3.forward), Quaternion.LookRotation(Vector3.back)
        };
        shadowCamera.enabled = true; // �ڭ̥u��ʩI�s���� Render()
        for (int i = 0; i < vplList.Count; i++)
        {
            shadowCamera.transform.position = vplList[i].position;
            for (int j = 0; j < 6; j++)
            {
                shadowCamera.transform.rotation = rotations[j];
                shadowCamera.Render();

                // �N��V�X���`�׹Ͻƻs�쯾�z�}�C������ slice
                int sliceIndex = i * 6 + j;
                Graphics.CopyTexture(rt, 0, 0, shadowmapArray, sliceIndex, 0);
            }
        }
        shadowCamera.enabled = false; // �ڭ̥u��ʩI�s���� Render()

        rt.Release(); // �����{�ɪ� Render Texture
    }

    void SetupShaderGlobals()
    {
        if (vplList.Count == 0) return;

        // �ǳƭn�ǵ� GPU ���ƾ�
        VPLDataForGPU[] vplData = new VPLDataForGPU[vplList.Count];
        for (int i = 0; i < vplList.Count; i++)
        {
            vplData[i] = new VPLDataForGPU { position = vplList[i].position, color = vplList[i].color };
        }

        // �إߨó]�w ComputeBuffer
        vplBuffer = new ComputeBuffer(vplList.Count, sizeof(float) * 7); // Vector3 (3 floats) + Vector4 (4 floats)
        vplBuffer.SetData(vplData);

        // �N�w�İϡB���z�}�C�M�p�ƾ��]�������ܼơA�ѩҦ� Shader �ϥ�
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
            UnityEngine.Debug.LogError($"PBRT �����ɮץ����: {sceneFilePath}");
        }
    }

    void OnDrawGizmos()
    {
        if (vplList != null)
        {
            foreach (var vpl in vplList)
            {
                Gizmos.color = vpl.color;
                Gizmos.DrawSphere(vpl.position, 0.05f); // ø�s�@�Ӥp�y��N�� VPL
            }
        }
    }
}
