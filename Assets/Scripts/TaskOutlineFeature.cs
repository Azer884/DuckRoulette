using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Screen-space outline for TaskHighlighter's objects, added to the URP renderer(s) by
// Editor/TaskOutlineFeatureInstaller.
//
// 1. Every highlighted renderer is drawn flat white into a one-channel mask, ignoring depth, so
//    the shape shows through walls.
// 2. A full-screen pass dilates that mask by Width pixels and draws the ring around it - pixels
//    near the mask but not on it - over the camera image, blinking.
//
// Working on the finished silhouette rather than pushing each mesh out along its normals means
// every edge gets exactly the same thickness, on any mesh: split normals, open meshes, flat decals
// (the campfire's dirt patch) and many-piece props all outline cleanly.
public class TaskOutlineFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader maskShader;
    [SerializeField] private Shader compositeShader;

    [SerializeField, ColorUsage(true, true)] private Color color = new(1f, 0.82f, 0.1f, 1f);
    [SerializeField, Range(1f, 12f), Tooltip("Outline thickness in pixels (at 1080p; scales with resolution).")]
    private float width = 4f;
    [SerializeField, Tooltip("Blink rate in radians per second (3 = one blink about every 2 s).")]
    private float blinkSpeed = 3f;
    [SerializeField, Range(0f, 1f), Tooltip("Lowest opacity in the blink.")] private float minAlpha = 0.2f;
    [SerializeField] private RenderPassEvent passEvent = RenderPassEvent.AfterRenderingTransparents;

    private Material maskMaterial;
    private Material compositeMaterial;
    private TaskOutlinePass pass;

    private static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int BlinkSpeedId = Shader.PropertyToID("_BlinkSpeed");
    private static readonly int MinAlphaId = Shader.PropertyToID("_MinAlpha");
    private static readonly int TexelSizeId = Shader.PropertyToID("_MaskTexelSize");

    public override void Create()
    {
        pass = new TaskOutlinePass { renderPassEvent = passEvent };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (TaskHighlighter.ActiveRenderers.Count == 0)
        {
            return;
        }

        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
        {
            return;
        }

        if (maskMaterial == null && maskShader != null)
        {
            maskMaterial = CoreUtils.CreateEngineMaterial(maskShader);
        }

        if (compositeMaterial == null && compositeShader != null)
        {
            compositeMaterial = CoreUtils.CreateEngineMaterial(compositeShader);
        }

        if (maskMaterial == null || compositeMaterial == null)
        {
            return;
        }

        compositeMaterial.SetColor(ColorId, color);
        compositeMaterial.SetFloat(BlinkSpeedId, blinkSpeed);
        compositeMaterial.SetFloat(MinAlphaId, minAlpha);

        // Same apparent thickness at any resolution.
        float scale = Mathf.Max(0.5f, renderingData.cameraData.cameraTargetDescriptor.height / 1080f);
        compositeMaterial.SetFloat(WidthId, width * scale);

        pass.renderPassEvent = passEvent;
        pass.Setup(maskMaterial, compositeMaterial);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(maskMaterial);
        CoreUtils.Destroy(compositeMaterial);
        maskMaterial = null;
        compositeMaterial = null;
    }

    private class TaskOutlinePass : ScriptableRenderPass
    {
        private Material maskMaterial;
        private Material compositeMaterial;

        private class MaskPassData
        {
            public Material Material;
            public readonly List<(Renderer renderer, int subMeshes)> Renderers = new();
        }

        private class CompositePassData
        {
            public TextureHandle Mask;
            public Material Material;
        }

        public void Setup(Material mask, Material composite)
        {
            maskMaterial = mask;
            compositeMaterial = composite;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
            descriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;

            TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor,
                "_TaskOutlineMask", true, FilterMode.Point);

            compositeMaterial.SetVector(TexelSizeId,
                new Vector4(1f / descriptor.width, 1f / descriptor.height, descriptor.width, descriptor.height));

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Task Outline Mask", out MaskPassData data))
            {
                data.Material = maskMaterial;
                data.Renderers.Clear();
                foreach (Renderer r in TaskHighlighter.ActiveRenderers)
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    data.Renderers.Add((r, SubMeshCount(r)));
                }

                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((MaskPassData d, RasterGraphContext context) =>
                {
                    foreach ((Renderer r, int subMeshes) in d.Renderers)
                    {
                        if (r == null)
                        {
                            continue;
                        }

                        for (int s = 0; s < subMeshes; s++)
                        {
                            context.cmd.DrawRenderer(r, d.Material, s, 0);
                        }
                    }
                });
            }

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Task Outline Composite", out CompositePassData data))
            {
                data.Mask = mask;
                data.Material = compositeMaterial;

                builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((CompositePassData d, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, d.Mask, new Vector4(1f, 1f, 0f, 0f), d.Material, 0);
                });
            }
        }

        private static int SubMeshCount(Renderer r)
        {
            Mesh mesh = r switch
            {
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                _ => r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null
            };

            return mesh != null ? Mathf.Max(1, mesh.subMeshCount) : 1;
        }
    }
}
