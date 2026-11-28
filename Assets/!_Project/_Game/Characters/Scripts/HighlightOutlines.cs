using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// The highlight outline, drawn on the screen (28 November). It replaced the inverted-hull shells,
// which stood out from thin boards and split at sharp corners: the highlighted renderers are
// drawn into a mask, and a line of even width is drawn just outside the mask's edge. It follows
// any shape exactly, costs nothing while nothing is highlighted, and needs no renderer asset
// change: the pass is added to the player's camera from code each frame something is lit.
public static class HighlightOutlines
{
    static readonly HashSet<OutlineHighlight> shown = new HashSet<OutlineHighlight>();
    static OutlinePass pass;
    static Material material;

    // The line's width at 1080 lines; it scales with the screen.
    public const float PixelsAt1080 = 3f;

    public static bool Any => shown.Count > 0;
    public static bool IsShown(OutlineHighlight outline) => shown.Contains(outline);

    public static void Show(OutlineHighlight outline) { if (outline != null) shown.Add(outline); }
    public static void Hide(OutlineHighlight outline) => shown.Remove(outline);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        shown.Clear();
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
    }

    static void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        if (shown.Count == 0 || camera.cameraType != CameraType.Game || camera != Camera.main) return;
        if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.scriptableRenderer == null) return;
        if (material == null)
        {
            Shader shader = Resources.Load<Shader>("KehaiHighlightOutline");
            if (shader == null || !shader.isSupported) return;
            material = CoreUtils.CreateEngineMaterial(shader);
        }
        pass ??= new OutlinePass();
        data.scriptableRenderer.EnqueuePass(pass);
    }

    // What the pass draws this frame: each shown outline's live renderers, by colour.
    static readonly List<(Renderer renderer, int submeshes, Color colour)> drawList = new List<(Renderer, int, Color)>();

    static void Collect()
    {
        drawList.Clear();
        shown.RemoveWhere(o => o == null);
        foreach (OutlineHighlight outline in shown)
            foreach (Renderer r in outline.Targets)
            {
                if (r == null || !r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                int submeshes = r is SkinnedMeshRenderer s && s.sharedMesh != null ? s.sharedMesh.subMeshCount
                              : r.TryGetComponent(out MeshFilter f) && f.sharedMesh != null ? f.sharedMesh.subMeshCount : 0;
                if (submeshes > 0) drawList.Add((r, submeshes, outline.outlineColor));
            }
    }

    sealed class MaskData
    {
        public List<(Renderer renderer, int submeshes, Color colour)> renderers;
        public Material material;
    }

    sealed class LineData
    {
        public TextureHandle mask;
        public Material material;
    }

    sealed class OutlinePass : ScriptableRenderPass
    {
        static readonly int ColourId = Shader.PropertyToID("_OutlineColor");
        static readonly int PixelsId = Shader.PropertyToID("_OutlinePixels");
        static readonly int TexelId = Shader.PropertyToID("_MaskTexel");

        public OutlinePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Collect();
            if (drawList.Count == 0 || material == null) return;
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (resources.isActiveTargetBackBuffer) return;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            // The mask tests against the scene's depth, so what stands in front of the thing hides
            // its line; with MSAA the depth can't be shared, and the line shows through instead.
            bool depthTest = desc.msaaSamples <= 1;
            desc.graphicsFormat = GraphicsFormat.R8_UNorm;
            desc.depthStencilFormat = GraphicsFormat.None;
            desc.msaaSamples = 1;
            TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "Highlight mask", true);

            using (var builder = renderGraph.AddRasterRenderPass<MaskData>("Highlight mask", out var data))
            {
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                if (depthTest) builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                data.renderers = drawList;
                data.material = material;
                builder.SetRenderFunc((MaskData d, RasterGraphContext ctx) =>
                {
                    foreach (var (renderer, submeshes, _) in d.renderers)
                        for (int i = 0; i < submeshes; i++) ctx.cmd.DrawRenderer(renderer, d.material, i, 0);
                });
            }

            // One colour per frame: the first shown outline's (they are all the store's yellow).
            material.SetColor(ColourId, drawList[0].colour);
            material.SetFloat(PixelsId, Mathf.Max(2f, PixelsAt1080 * desc.height / 1080f));
            material.SetVector(TexelId, new Vector4(1f / Mathf.Max(1, desc.width), 1f / Mathf.Max(1, desc.height), 0f, 0f));

            using (var builder = renderGraph.AddRasterRenderPass<LineData>("Highlight outline", out var data))
            {
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.AllowPassCulling(false);
                data.mask = mask;
                data.material = material;
                builder.SetRenderFunc((LineData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.mask, new Vector4(1f, 1f, 0f, 0f), d.material, 1));
            }
        }
    }
}
