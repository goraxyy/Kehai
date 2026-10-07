using NUnit.Framework;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The GPU Resident Drawer draws the shop (IDEAS.md, "Scaling", step 1: 18.9 ms of rendering
// down to 4.5). Its settings live in the project's local settings, which aren't in git, so
// this is what notices if one gets switched off.
public class PerformanceSettingsTests
{
    static UniversalRenderPipelineAsset PcPipeline()
    {
        int pc = System.Array.IndexOf(QualitySettings.names, "PC");
        Assume.That(pc >= 0, "this project has no PC quality level");
        var asset = QualitySettings.GetRenderPipelineAssetAt(pc) as UniversalRenderPipelineAsset;
        return asset != null ? asset : GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
    }

    [Test]
    public void TheGpuResidentDrawer_IsOn_WithOcclusionCulling()
    {
        UniversalRenderPipelineAsset pipeline = PcPipeline();
        Assert.IsNotNull(pipeline, "the PC quality level has no URP asset");
        Assert.AreEqual(GPUResidentDrawerMode.InstancedDrawing, pipeline.gpuResidentDrawerMode, pipeline.name);
        Assert.IsTrue(pipeline.gpuResidentDrawerEnableOcclusionCullingInCameras, $"{pipeline.name}: GPU occlusion culling");
    }

    // Without these the editor quietly leaves the drawer off, and a build has no shaders for it.
    [Test]
    public void BatchRendererGroupVariants_AreKept()
    {
        Assert.AreEqual(BatchRendererGroupStrippingMode.KeepAll, EditorGraphicsSettings.batchRendererGroupShaderStrippingMode);
    }
}
