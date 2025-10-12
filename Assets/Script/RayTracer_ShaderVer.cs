using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class RayTracer_ShaderVer : MonoBehaviour
{
    RenderTexture _target;   ///< Compute Shader 渲染在這個 texture，然後再顯示在 _displayQuad
    GameObject _displayQuad; ///< 放在 Near Clip Plane 上，負責顯示渲染結果
    Camera _camera;          ///< 記錄相機 Component
    int _renderTime = 0;

    [Tooltip("使用第 0 個 kernel 進行渲染，RenderTexture 會綁定在 Result 變數")]
    public ComputeShader RayTracingShader;

    [Tooltip("只渲染一幀")]
    public bool OnlyRenderOneTime = true;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _renderTime = 0;
    }

    private void Update()
    {
        if (OnlyRenderOneTime && _renderTime > 0)
            return;
        ++_renderTime;

        InitRenderTexture();
        InitDisplayQuad();
        SetupBasicParameters();

        // Rendering
        RayTracingShader.Dispatch(0, Mathf.CeilToInt(Screen.width / 8), Mathf.CeilToInt(Screen.height / 8), 1);
    }

    private void InitRenderTexture()
    {
        if (_target == null || _target.width != Screen.width || _target.height != Screen.height)
        {
            // Release render texture if we already have one
            if (_target != null)
                _target.Release();

            // Get a render target for Ray Tracing
            _target = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _target.enableRandomWrite = true;
            _target.Create();

            if (_displayQuad != null)
                _displayQuad.GetComponent<MeshRenderer>().material.mainTexture = _target;
        }
    }

    /// <summary>
    /// 設置顯示平面，顯示平面會放在 Near Clip Plane 的位置，顯示 _target 的內容
    /// </summary>
    void InitDisplayQuad()
    {
        if (_displayQuad == null)
        {
            // 1. 建立用於顯示的 Material
            Material displayMaterial = new Material(Shader.Find("Unlit/Texture"));
            displayMaterial.mainTexture = _target;

            // 2. 建立一個 Quad 作為攝影機的子物件，用於顯示畫面
            _displayQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _displayQuad.GetComponent<MeshRenderer>().material = displayMaterial;
            _displayQuad.transform.parent = _camera.transform;
        }

        // 3. 根據攝影機參數調整 Quad 的位置和大小，使其剛好填滿畫面
        // 將 Quad 放在近裁剪平面前方一點點的位置
        float quadPositionZ = _camera.nearClipPlane + 0.001f;

        // 計算在該距離下，攝影機視野的高度和寬度
        float quadHeight = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * quadPositionZ * 2f;
        float quadWidth = quadHeight * _camera.aspect;

        // 設定 Quad 的本地位置和縮放
        _displayQuad.transform.localPosition = new Vector3(0, 0, quadPositionZ);
        _displayQuad.transform.localScale = new Vector3(quadWidth, quadHeight, 1f);
        _displayQuad.transform.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// 設置 BasicRay.hlsl 中相機相關參數 以及 Result Texture
    /// </summary>
    private void SetupBasicParameters()
    {
        RayTracingShader.SetTexture(0, "Result", _target);
        RayTracingShader.SetMatrix("_CameraProjectionInverse", Matrix4x4.Perspective(_camera.fieldOfView, _camera.aspect, _camera.nearClipPlane, _camera.farClipPlane).inverse);
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        RayTracingShader.SetVector("_ScreenSize", new Vector2(Screen.width, Screen.height));
    }
}
