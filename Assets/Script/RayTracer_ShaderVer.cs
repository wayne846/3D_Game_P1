using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.VisualScripting.Dependencies.Sqlite;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class RayTracer_ShaderVer : MonoBehaviour
{
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct AOParams
    {
        [Range(0, 1)]
        public int _AOUse;
        public int _AOSamples;
        public float _AORadius;
        public float _AOBias;
        public float _AOIntensity;
    };

    RenderTexture _target;   ///< Compute Shader 渲染在這個 texture，然後再顯示在 _displayQuad
    RenderTexture _target2;
    RenderTexture WorldPosTexture;
    RenderTexture NormalTexture;
    ComputeBuffer _aoBffer;
    Material _SSAOMat;
    GameObject _displayQuad; ///< 放在 Near Clip Plane 上，負責顯示渲染結果
    Camera _camera;          ///< 記錄相機 Component
    bool _firstRender;

    [Tooltip("使用第 0 個 kernel 進行渲染，RenderTexture 會綁定在 Result 變數")]
    public ComputeShader RayTracingShader;

    [Tooltip("只渲染一幀")]
    public bool OnlyRenderOneTime = true;

    [Tooltip("")]
    public bool DoSSAO = false;

    [Tooltip("AO 的設定")]
    public AOParams AoParameters = new AOParams { _AOUse = 0, _AOSamples = 4, _AORadius = 0.06f, _AOBias = 0.006f, _AOIntensity = 1f };

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        _firstRender = true;
        _SSAOMat = new Material(Shader.Find("Hidden/SSAO"));

        if (_aoBffer == null)
        {
            _aoBffer = new ComputeBuffer(1, 5 * sizeof(float));
        }
    }

    private void OnDisable()
    {
        Destroy(_displayQuad);
    }

    private void Update()
    {
        if (OnlyRenderOneTime && !_firstRender)
            return;
        _firstRender = false;

        Render();
    }

    [ContextMenu("Export Ray Tracing Result")]
    public void ExportTexture()
    {
        if (!Application.isPlaying)
            return;

        string FileName = DateTime.Now.ToString("yy-MM-dd_HH-mm") + "_ray.exr";
        Debug.Log(FileName);

        var prev_rt = RenderTexture.active;
        RenderTexture.active = _target;

        var tmp = new Texture2D(_target.width, _target.height, TextureFormat.RGBAFloat, false, true);
        tmp.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
        tmp.Apply();
        
        RenderTexture.active = prev_rt;
        
        if (Application.isEditor)
        {
            File.WriteAllBytes(Path.Combine(new string[] { Application.dataPath, "..", "Logs", FileName }), tmp.EncodeToEXR());
            DestroyImmediate(tmp);
        }
        else
        {
            File.WriteAllBytes(Path.Combine(new string[] { Application.dataPath, FileName }), tmp.EncodeToEXR());
            Destroy(tmp);
        }
    }

    public void Render()
    {
        InitRenderTexture();
        InitDisplayQuad();
        SetupBasicParameters(); // NOTE: RayTracingShader 會渲染到 _target2

        // Rendering
        RayTracingShader.Dispatch(0, Mathf.CeilToInt(Screen.width / 8), Mathf.CeilToInt(Screen.height / 8), 1);

        if (DoSSAO)
        {
            _SSAOMat.SetTexture("_WorldPosTex", WorldPosTexture);
            _SSAOMat.SetTexture("_NormalTex", NormalTexture);
            _SSAOMat.SetVector("_TexSize", new Vector2(Screen.width, Screen.height));
            _SSAOMat.SetVector("_CameraPos", _camera.transform.position);
            _SSAOMat.SetMatrix("_Camera_VP", GL.GetGPUProjectionMatrix(_camera.projectionMatrix, false) * _camera.worldToCameraMatrix);

            Graphics.Blit(_target2, _target, _SSAOMat);
        }
        else
            Graphics.Blit(_target2, _target);
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

        if (_target2 == null || _target2.width != _target.width || _target2.height != _target.height)
        {
            if (_target2 != null)
                _target2.Release();

            _target2 = new RenderTexture(_target.width, _target.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _target2.enableRandomWrite = true;
            _target2.Create();
        }

        if (WorldPosTexture == null || WorldPosTexture.width != _target.width || WorldPosTexture.height != _target.height)
        {
            if (WorldPosTexture != null)
                WorldPosTexture.Release();

            WorldPosTexture = new RenderTexture(_target.width, _target.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            WorldPosTexture.enableRandomWrite = true;
            WorldPosTexture.Create();
        }

        if (NormalTexture == null || NormalTexture.width != _target.width || NormalTexture.height != _target.height)
        {
            if (NormalTexture != null)
                NormalTexture.Release();

            NormalTexture = new RenderTexture(_target.width, _target.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            NormalTexture.enableRandomWrite = true;
            NormalTexture.Create();
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
        _aoBffer.SetData(new AOParams[] {AoParameters});
        RayTracingShader.SetConstantBuffer("AOParams", _aoBffer, 0, 5 * sizeof(float));

        RayTracingShader.SetTexture(0, "Result", _target2);
        RayTracingShader.SetTexture(0, "WorldPosTexture", WorldPosTexture);
        RayTracingShader.SetTexture(0, "NormalTexture", NormalTexture);
        RayTracingShader.SetMatrix("_CameraProjectionInverse", Matrix4x4.Perspective(_camera.fieldOfView, _camera.aspect, _camera.nearClipPlane, _camera.farClipPlane).inverse);
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        RayTracingShader.SetVector("_ScreenSize", new Vector2(Screen.width, Screen.height));
    }

    public void DestroyQuad()
    {
        if(_displayQuad != null)
        {
            Destroy(_displayQuad);
            _displayQuad = null;
        }
    }
}
