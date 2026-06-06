using UnityEngine;
using UnityEngine.UI;
using VMG.Core;
using VMG.Svg;

namespace VMG.UI
{
    /// UGUI procedural vector renderer. MaskableGraphic so it interoperates
    /// with Mask / RectMask2D. All [SerializeField] fields below are keyframable
    /// from Animator / Timeline.
    [AddComponentMenu("UI/VMG/Vector Image", 11)]
    [RequireComponent(typeof(CanvasRenderer))]
    [ExecuteAlways]
    public sealed class VectorImageGraphic : MaskableGraphic
    {
        [Tooltip("Optional SVG asset. When set, procedural shape/modifiers/style are bypassed. Object reference: NOT keyframable from AnimationClip — swap via script.")]
        [SerializeField] private VMGShapeAsset m_SvgAsset;
        [SerializeField] private ShapeStack m_ShapeStack = ShapeStack.Default();
        [SerializeField] private StrokeStyle m_Stroke = StrokeStyle.Default;
        [SerializeField] private FillStyle m_Fill = new FillStyle { enabled = true, color = Color.white };
        [SerializeField] private RoundCornerModifier m_RoundCorners = RoundCornerModifier.Default();
        [SerializeField] private TrimPathModifier m_Trim = TrimPathModifier.Default();
        [Tooltip("Stretch the shape to fill the RectTransform. Keyframable.")]
        [SerializeField] private bool m_FitToRect = true;
        [Tooltip("Texture sampled across the renderer's bounds (UV 0..1). Object reference: NOT keyframable from AnimationClip — swap via script.")]
        [SerializeField] private Texture m_Texture;

        private readonly ShapePipeline m_Pipeline = new ShapePipeline();
        private readonly MeshBuffer m_StrokeBuf = new MeshBuffer();

        public VMGShapeAsset SvgAsset { get => m_SvgAsset; set { m_SvgAsset = value; SetVerticesDirty(); } }
        public ref ShapeStack ShapeStack => ref m_ShapeStack;
        public ref StrokeStyle Stroke => ref m_Stroke;
        public ref FillStyle Fill => ref m_Fill;
        public ref RoundCornerModifier RoundCornerModifier => ref m_RoundCorners;
        public ref TrimPathModifier TrimModifier => ref m_Trim;
        public Texture Texture { get => m_Texture; set { m_Texture = value; SetMaterialDirty(); } }

        public override Texture mainTexture => m_Texture != null ? m_Texture : base.mainTexture;

        protected override void OnEnable()
        {
            base.OnEnable();
            SetVerticesDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetVerticesDirty();
        }
#endif

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (m_FitToRect) SetVerticesDirty();
        }

        /// Animator/Timeline-driven [SerializeField] writes don't go through
        /// any setter, so the Graphic doesn't know to rebuild. Mark dirty
        /// every late frame; cost is bounded by OnPopulateMesh skipping a
        /// regen if nothing actually changed downstream.
        private void LateUpdate()
        {
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (m_SvgAsset != null)
            {
                PopulateFromSvg(vh);
                return;
            }

            if (m_FitToRect)
            {
                // Fit applies to every slot so the blend stays in sync
                // with the RectTransform — different per-slot sizes
                // would break the index-by-index lerp visually.
                Rect r = rectTransform.rect;
                m_ShapeStack.m_Slot0.shape.center = r.center; m_ShapeStack.m_Slot0.shape.size = r.size;
                m_ShapeStack.m_Slot1.shape.center = r.center; m_ShapeStack.m_Slot1.shape.size = r.size;
                m_ShapeStack.m_Slot2.shape.center = r.center; m_ShapeStack.m_Slot2.shape.size = r.size;
                m_ShapeStack.m_Slot3.shape.center = r.center; m_ShapeStack.m_Slot3.shape.size = r.size;
            }

            // Pipeline: ShapeStack -> RoundCorner -> Trim. Fill skips
            // Trim so the closed shape survives the slice.
            //
            // Direct calls (not an IPathModifier list) so the struct
            // modifiers don't get boxed every frame.

            // Fill pipeline.
            m_Pipeline.workingPath.Clear();
            m_Pipeline.mesh.Clear();
            m_ShapeStack.Build(m_Pipeline.workingPath);
            if (m_RoundCorners.Enabled) m_RoundCorners.Apply(m_Pipeline.workingPath);
            if (m_Fill.enabled)
            {
                var fill = m_Fill;
                fill.color *= color; // respect Graphic.color tint
                FillMeshBuilder.Build(m_Pipeline.workingPath, fill, m_Pipeline.mesh);
            }

            // Stroke pipeline: full modifier chain including trim.
            m_StrokeBuf.Clear();
            m_Pipeline.workingPath.Clear();
            m_ShapeStack.Build(m_Pipeline.workingPath);
            if (m_RoundCorners.Enabled) m_RoundCorners.Apply(m_Pipeline.workingPath);
            if (m_Trim.Enabled) m_Trim.Apply(m_Pipeline.workingPath);
            if (m_Stroke.enabled)
            {
                var stroke = m_Stroke;
                stroke.color *= color;
                StrokeMeshBuilder.Build(m_Pipeline.workingPath, stroke, m_StrokeBuf);
            }

            // Normalize UVs across the union of fill + stroke so a texture
            // lays continuously over the whole renderer.
            Rect uvRect = m_FitToRect ? rectTransform.rect : VertexUnionBounds(m_Pipeline.mesh, m_StrokeBuf);
            m_Pipeline.mesh.NormalizeUVsToRect(uvRect);
            m_StrokeBuf.NormalizeUVsToRect(uvRect);

            // Push fill mesh, then stroke mesh, into VertexHelper.
            AppendBufferToVH(m_Pipeline.mesh, vh);
            AppendBufferToVH(m_StrokeBuf, vh);
        }

        private static Rect VertexUnionBounds(MeshBuffer a, MeshBuffer b)
        {
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            Accumulate(a, ref minX, ref minY, ref maxX, ref maxY);
            Accumulate(b, ref minX, ref minY, ref maxX, ref maxY);
            if (float.IsPositiveInfinity(minX)) return new Rect(0f, 0f, 1f, 1f);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static void Accumulate(MeshBuffer mb, ref float minX, ref float minY, ref float maxX, ref float maxY)
        {
            for (int i = 0; i < mb.vertices.Count; i++)
            {
                var v = mb.vertices[i];
                if (v.x < minX) minX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.x > maxX) maxX = v.x;
                if (v.y > maxY) maxY = v.y;
            }
        }

        private readonly VectorPath m_SvgPath = new VectorPath();
        private void PopulateFromSvg(VertexHelper vh)
        {
            var asset = m_SvgAsset;
            Rect r = rectTransform.rect;
            // Uniform fit-to-rect with center origin. SVG viewBox starts at (0,0)
            // top-left; ScriptedImporter already flipped Y so (0,0) is now
            // bottom-left in SVG coords. Map [0,viewBoxSize] -> rect.
            float sx = asset.viewBoxSize.x > 0f ? r.width / asset.viewBoxSize.x : 1f;
            float sy = asset.viewBoxSize.y > 0f ? r.height / asset.viewBoxSize.y : 1f;
            float scale = m_FitToRect ? Mathf.Min(sx, sy) : 1f;
            Vector2 fitSize = new Vector2(asset.viewBoxSize.x * scale, asset.viewBoxSize.y * scale);
            Vector2 origin = r.center - fitSize * 0.5f;

            m_StrokeBuf.Clear();
            m_Pipeline.mesh.Clear();

            for (int s = 0; s < asset.subShapes.Count; s++)
            {
                var sub = asset.subShapes[s];
                if (sub == null || sub.nodes.Count < 2) continue;

                // Bezier-tessellate + apply uniform fit transform.
                BezierTessellator.Tessellate(sub.nodes, sub.closed,
                    Mathf.Max(4, m_ShapeStack.m_Slot0.shape.bezierSamplesPerSegment > 0
                                 ? m_ShapeStack.m_Slot0.shape.bezierSamplesPerSegment : 16),
                    m_SvgPath);
                for (int i = 0; i < m_SvgPath.nodes.Count; i++)
                {
                    var n = m_SvgPath.nodes[i];
                    n.position = origin + n.position * scale;
                    m_SvgPath.nodes[i] = n;
                }

                if (sub.fill.enabled)
                {
                    var fill = sub.fill; fill.color *= color;
                    FillMeshBuilder.Build(m_SvgPath, fill, m_Pipeline.mesh);
                }
                if (sub.stroke.enabled)
                {
                    var stroke = sub.stroke; stroke.color *= color;
                    stroke.width *= scale;
                    StrokeMeshBuilder.Build(m_SvgPath, stroke, m_StrokeBuf);
                }
            }

            // Normalize across the fit rect so the SVG's viewBox maps to [0,1].
            Rect uvRect = new Rect(origin.x, origin.y, fitSize.x, fitSize.y);
            m_Pipeline.mesh.NormalizeUVsToRect(uvRect);
            m_StrokeBuf.NormalizeUVsToRect(uvRect);

            AppendBufferToVH(m_Pipeline.mesh, vh);
            AppendBufferToVH(m_StrokeBuf, vh);
        }

        private static void AppendBufferToVH(MeshBuffer mb, VertexHelper vh)
        {
            int baseV = vh.currentVertCount;
            int vc = mb.vertices.Count;
            for (int i = 0; i < vc; i++)
            {
                var v = mb.vertices[i];
                var c = mb.colors[i];
                var uv = mb.uvs[i];
                vh.AddVert(v, c, uv);
            }
            int tc = mb.triangles.Count;
            for (int i = 0; i + 2 < tc; i += 3)
            {
                vh.AddTriangle(baseV + mb.triangles[i], baseV + mb.triangles[i + 1], baseV + mb.triangles[i + 2]);
            }
        }
    }
}
