using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

    RenderTexture _target;   ///< 一般：直接渲染在 _target, SSAO: 先渲染在 _target2 再渲染到 _target
    RenderTexture _target2;
    RenderTexture WorldPosTexture;
    RenderTexture NormalTexture;
    ComputeBuffer _aoBffer;
    Material _SSAOMat;
    Camera _camera;          ///< 記錄相機 Component
    bool _firstRender;
    int _kernel;

    [Tooltip("使用第 0 個 kernel 進行渲染，RenderTexture 會綁定在 Result 變數")]
    public ComputeShader RayTracingShader;

    [Tooltip("只渲染一幀")]
    public bool OnlyRenderOneTime = true;

    [Tooltip("全域調整 Bump Map 的使用")]
    public bool UseBumpMap = true;

    [Tooltip("")]
    public bool DoSSAO = false;

    [Tooltip("AO 的設定")]
    public AOParams AoParameters = new AOParams { _AOUse = 0, _AOSamples = 4, _AORadius = 0.06f, _AOBias = 0.006f, _AOIntensity = 1f };

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        _firstRender = true;
        _SSAOMat = new Material(Shader.Find("Custom/SSAO"));
        _aoBffer = new ComputeBuffer(1, 5 * sizeof(float));
        _kernel = RayTracingShader.FindKernel("CSMain");
    }

    private void OnDisable()
    {
        _target.Release(); _target = null;
        WorldPosTexture.Release(); WorldPosTexture = null;
        NormalTexture.Release(); NormalTexture = null;
        GameObject.Destroy(_SSAOMat); _SSAOMat = null;
        _aoBffer.Release(); _aoBffer = null;
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

    public void Render(ref CommandBuffer cmd)
    {
        if (!Application.isPlaying)
            return;
        if (OnlyRenderOneTime && !_firstRender)
        {
            cmd.Blit(_target, BuiltinRenderTextureType.CameraTarget);
            return;
        }
        _firstRender = false;

        InitRenderTexture();
        SetupBasicParameters(); // NOTE: RayTracingShader 一般：直接渲染在 _target, SSAO: 先渲染在 _target2 再渲染到 _target

        // Rendering
        cmd.DispatchCompute(RayTracingShader, _kernel, Mathf.CeilToInt(Screen.width / 8.0f), Mathf.CeilToInt(Screen.height / 8.0f), 1);

        if (DoSSAO)
        {
            _SSAOMat.SetTexture("_WorldPosTex", WorldPosTexture);
            _SSAOMat.SetTexture("_NormalTex", NormalTexture);
            _SSAOMat.SetVector("_TexSize", new Vector2(Screen.width, Screen.height));
            _SSAOMat.SetVector("_CameraPos", _camera.transform.position);
            _SSAOMat.SetMatrix("_Camera_VP", GL.GetGPUProjectionMatrix(_camera.projectionMatrix, false) * _camera.worldToCameraMatrix);

            cmd.Blit(_target2, _target, _SSAOMat);
        }
        
        cmd.Blit(_target, BuiltinRenderTextureType.CameraTarget);
    }

    private void InitRenderTexture()
    {
        RenderTextureDescriptor desc = new(Screen.width, Screen.height, RenderTextureFormat.ARGBFloat
                                            , /* depth */ 0, /* mipmap */ 0, RenderTextureReadWrite.Linear);
        desc.enableRandomWrite = true;

        if (_target == null || _target.width != desc.width || _target.height != desc.height)
        {
            // Release render texture if we already have one
            if (_target != null)
                _target.Release();

            // Get a render target for Ray Tracing
            _target = new RenderTexture(desc);
            _target.Create();
        }

        if (_target2 == null || _target2.width != _target.width || _target2.height != _target.height)
        {
            if (_target2 != null)
                _target2.Release();

            _target2 = new RenderTexture(desc);
            _target2.Create();
        }

        if (WorldPosTexture == null || WorldPosTexture.width != _target.width || WorldPosTexture.height != _target.height)
        {
            if (WorldPosTexture != null)
                WorldPosTexture.Release();

            WorldPosTexture = new RenderTexture(desc);
            WorldPosTexture.Create();
        }

        if (NormalTexture == null || NormalTexture.width != _target.width || NormalTexture.height != _target.height)
        {
            if (NormalTexture != null)
                NormalTexture.Release();

            NormalTexture = new RenderTexture(desc);
            NormalTexture.Create();
        }
    }

    /// <summary>
    /// 設置 BasicRay.hlsl 中相機相關參數 以及 Result Texture
    /// </summary>
    private void SetupBasicParameters()
    {
        _aoBffer.SetData(new AOParams[] {AoParameters});
        RayTracingShader.SetConstantBuffer("AOParams", _aoBffer, 0, 5 * sizeof(float));

        if (DoSSAO)
            RayTracingShader.SetTexture(_kernel, "Result", _target2);
        else
            RayTracingShader.SetTexture(_kernel, "Result", _target);
        RayTracingShader.SetTexture(_kernel, "WorldPosTexture", WorldPosTexture);
        RayTracingShader.SetTexture(_kernel, "NormalTexture", NormalTexture);
        RayTracingShader.SetMatrix("_CameraProjectionInverse", Matrix4x4.Perspective(_camera.fieldOfView, _camera.aspect, _camera.nearClipPlane, _camera.farClipPlane).inverse);
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        RayTracingShader.SetVector("_ScreenSize", new Vector2(Screen.width, Screen.height));
        RayTracingShader.SetBool("_GlobalUseBumpMap", UseBumpMap);
    }
}
