using System.Collections.Generic;
using UnityEngine;





public class VPL_Render : MonoBehaviour
{
    [Header("Options")]
    public bool includeInactiveLights = true;

    public int numberOfVPLs = 128; // �n�ͦ��� VPL �ƶq
    public float rayMaxDistance = 100f;

    PbrtScene scene = null;

    [Header("Shadow Settings")]
    public int shadowMapResolution = 256; // ���v�K�Ϫ��ѪR��
    public LayerMask shadowCasterLayers; // ���w���ǹϼh������|���ͳ��v
    public float shadowNearClip = 0.1f;
    public float shadowFarClip = 100f;

    List<VPL> vplList = new List<VPL>();
    List<GameObject> vplVisualizeSphere = new List<GameObject>();
    ComputeBuffer vplBuffer;
    Texture2DArray shadowmapArray; // �x�s�Ҧ����v�K�Ϫ����z�}�C
    Camera shadowCamera;

    bool isVisualizeVpl = false;
    bool isOnlyOneVpl = false;
    int onlyOneVplIndex = 0;

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

        // ComputeBuffer �ݭn���D���c�j�p
        // Vector3 (3*4 bytes) + Color (4*4 bytes) = 12 + 16 = 28 bytes
        vplBuffer = new ComputeBuffer(numberOfVPLs, 28);
        vplBuffer.SetData(vplList);

        // �N VPL �ƾکM�ƶq�]�� Shader �����ܼ�
        Shader.SetGlobalBuffer("_VPLs", vplBuffer);
        Shader.SetGlobalInt("_VPLCount", vplList.Count);

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

                    // �إߥi����VPL�y��
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
            // �B�~�B�z�G�p�G�S�� VPL�A�]���ӽT�O�ª� Texture2DArray �Q�M�z
            if (shadowmapArray != null)
            {
                UnityEngine.Object.Destroy(shadowmapArray);
                shadowmapArray = null;
            }
            return;
        }

        // *** ����״_�I�G�b�Ыطs�� Texture2DArray ���e�A�P���ª� ***
        if (shadowmapArray != null)
        {
            // �ϥ� UnityEngine.Object.Destroy �P�� Unity ��͸귽
            UnityEngine.Object.Destroy(shadowmapArray);
        }

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

        rt.Release(); // �����{�ɪ� Render Texture
    }

    void SetupShaderGlobals()
    {
        if (vplList.Count == 0) return;

        // �ǳƭn�ǵ� GPU ���ƾ�
        VPLDataForGPU[] vplData = new VPLDataForGPU[vplList.Count];
        for (int i = 0; i < vplList.Count; i++)
        {
            if (isOnlyOneVpl)
            {
                // �u���S�w��vpl�~�|��
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


    #region Visualize
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

    /// <summary>
    /// �ʺA�إߤ@�Ӳy�� GameObject
    /// </summary>
    /// <param name="position">�y��b�@�ɪŶ�������m</param>
    /// <param name="radius">�y�骺�b�| (�z�L�Y��ӹ�{)</param>
    /// <param name="color">�y�骺�C��</param>
    /// <returns>�إߪ��y�� GameObject</returns>
    public GameObject CreateVplVisualizeSphere(Vector3 position, float radius, Color color)
    {
        // 1. �إߤ@�ӹw�]�� 3D �y����� (Primitive)
        // Unity ���ت��y��b�|�w�]�� 0.5 (���|�� 1 ���)
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

        // 2. ���w��m
        sphere.transform.position = position;

        // 3. ���w�b�| (�z�L�Y�� Sacle �ӹ�{)
        // �ѩ�w�]�y�骺���|�� 1�A�n�F����w���b�| R�A
        // �h���|�� 2R�A�]���Y��]�l�� (2 * radius)
        float scaleFactor = radius * 2f;
        sphere.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);

        // 4. ���w�C�� (�]�w����)

        // ���o�y��W�� MeshRenderer ����
        MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();

        if (renderer != null)
        {
            // ���F���v�T��L�ϥάۦP���誺����A��ĳ�ϥ� .material �ӫD .sharedMaterial
            // ��: �C���ϥ� .material �|�إߤ@�ӷs��������
            Material material = renderer.material;

            // �N���誺�C��]�w�����w�C��
            // �o�O�w�� Standard Shader �̱`�����]�w�覡
            material.color = color;

            // �T�O���誺��V�Ҧ��O�䴩�z���ת� (�p�G�C�⦳�z����)
            // �p�G�u�ݭn�¦�A�i�H�����o�@�B
            // material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            // material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            // material.SetInt("_ZWrite", 1);
            // material.DisableKeyword("_ALPHABLEND_ON");
            // material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            // material.SetInt("_Mode", (int)UnityEngine.Rendering.SurfaceType.Opaque); 
        }

        // 5. ��^�إߪ��y�骫��
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
