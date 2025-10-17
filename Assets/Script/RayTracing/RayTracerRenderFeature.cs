using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 在 URP 中你可以加入 Render Feature，每個 Render Feature 又能加入多個 Render Pass
// Render Feature 一次管理多組 Render Pass，可以一次進行開啟或關閉
// Render Pass 依據 RenderPassEvent 的值決定執行順序（小的先，大的後）
public class RayTracerRenderFeature : ScriptableRendererFeature
{
    class CustomRenderPass : ScriptableRenderPass
    {
        RayTracer_ShaderVer rayTracer;

        // This method is called before executing the render pass.
        // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
        // When empty this render pass will render to the active camera render target.
        // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
        // The render pipeline will ensure target setup and clearing happens in a performant manner.
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            rayTracer = GameObject.FindAnyObjectByType<RayTracer_ShaderVer>();
        }

        // Here you can implement the rendering logic.
        // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
        // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
        // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (rayTracer != null && rayTracer.enabled)
            {
                CommandBuffer cmd = CommandBufferPool.Get("RayTracer_ShaderVer");
                rayTracer.Render(ref cmd);
                context.ExecuteCommandBuffer(cmd);
            }
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }
    }

    CustomRenderPass m_ScriptablePass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new CustomRenderPass();

        // Configures where the render pass should be injected.
        // 最後一個執行
        m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRendering + 1000;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_ScriptablePass);
    }
}


