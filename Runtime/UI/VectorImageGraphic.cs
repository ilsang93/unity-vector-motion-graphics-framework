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
        public VMGShapeAsset SvgAsset;
        public ShapeStack ShapeStack = ShapeStack.Default();
        public StrokeStyle Stroke = StrokeStyle.Default;
        public FillStyle Fill = new FillStyle { enabled = true, color = Color.white };
        public RoundCornerModifier RoundCorners = RoundCornerModifier.Default();
        public TrimPathModifier Trim = TrimPathModifier.Default();
        [Tooltip("Stretch the shape to fill the RectTransform. When true, ShapeStack slot center/size channels are overwritten every frame from the RectTransform — animate RectTransform.sizeDelta instead. Keyframable.")]
        public bool FitToRect = true;
        [Tooltip("Texture sampled across the renderer's bounds (UV 0..1). Object reference: NOT keyframable from AnimationClip — swap via script.")]
        public Texture Texture;

        private readonly ShapePipeline m_Pipeline = new ShapePipeline();
        private readonly MeshBuffer m_StrokeBuf = new MeshBuffer();

        public override Texture mainTexture => Texture != null ? Texture : base.mainTexture;

        // The package's SDF-AA shader makes vector edges antialias regardless
        // of zoom — without it Canvas falls back to UI/Default's binary
        // alpha cutoff which produces stairsteps on diagonals. User can
        // still override by assigning their own material in the inspector;
        // this only kicks in when m_Material is null (the default state).
        private static Material s_VmgDefaultMat;
        public override Material defaultMaterial
        {
            get
            {
                if (s_VmgDefaultMat == null)
                {
                    var shader = Shader.Find("VMG/UI/VectorSDF");
                    if (shader != null) s_VmgDefaultMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                return s_VmgDefaultMat != null ? s_VmgDefaultMat : base.defaultMaterial;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureCanvasUv1Channel();
            SetVerticesDirty();
        }

        // UGUI strips UV1 from the vertex stream by default — the SDF AA
        // distance channel travels through UV1, so enable it on the owning
        // Canvas. Idempotent; the bitmask write is cheap and authoritative.
        private void EnsureCanvasUv1Channel()
        {
            var c = canvas;
            if (c == null) return;
            var root = c.rootCanvas != null ? c.rootCanvas : c;
            root.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
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
            if (FitToRect) SetVerticesDirty();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnsureCanvasUv1Channel();
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

            if (SvgAsset != null)
            {
                PopulateFromSvg(vh);
                return;
            }

            if (FitToRect)
            {
                // Fit applies to every slot so the blend stays in sync
                // with the RectTransform — different per-slot sizes
                // would break the index-by-index lerp visually.
                Rect r = rectTransform.rect;
                ShapeStack.Slot0.shape.center = r.center; ShapeStack.Slot0.shape.size = r.size;
                ShapeStack.Slot1.shape.center = r.center; ShapeStack.Slot1.shape.size = r.size;
                ShapeStack.Slot2.shape.center = r.center; ShapeStack.Slot2.shape.size = r.size;
                ShapeStack.Slot3.shape.center = r.center; ShapeStack.Slot3.shape.size = r.size;
            }

            // Pipeline: ShapeStack -> RoundCorner -> Trim. Fill skips
            // Trim so the closed shape survives the slice.
            //
            // Direct calls (not an IPathModifier list) so the struct
            // modifiers don't get boxed every frame.

            // Fill pipeline.
            m_Pipeline.workingPath.Clear();
            m_Pipeline.mesh.Clear();
            ShapeStack.Build(m_Pipeline.workingPath);
            if (RoundCorners.Enabled) RoundCorners.Apply(m_Pipeline.workingPath);
            if (Fill.enabled)
            {
                var fill = Fill;
                fill.color *= color; // respect Graphic.color tint
                FillMeshBuilder.Build(m_Pipeline.workingPath, fill, m_Pipeline.mesh);
            }

            // Stroke pipeline: full modifier chain including trim.
            m_StrokeBuf.Clear();
            m_Pipeline.workingPath.Clear();
            ShapeStack.Build(m_Pipeline.workingPath);
            if (RoundCorners.Enabled) RoundCorners.Apply(m_Pipeline.workingPath);
            if (Trim.Enabled) Trim.Apply(m_Pipeline.workingPath);
            if (Stroke.enabled)
            {
                var stroke = Stroke;
                stroke.color *= color;
                StrokeMeshBuilder.Build(m_Pipeline.workingPath, stroke, m_StrokeBuf);
            }

            // Normalize UVs across the union of fill + stroke so a texture
            // lays continuously over the whole renderer.
            Rect uvRect = FitToRect ? rectTransform.rect : VertexUnionBounds(m_Pipeline.mesh, m_StrokeBuf);
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
            var asset = SvgAsset;
            Rect r = rectTransform.rect;
            // Uniform fit-to-rect with center origin. SVG viewBox starts at (0,0)
            // top-left; ScriptedImporter already flipped Y so (0,0) is now
            // bottom-left in SVG coords. Map [0,viewBoxSize] -> rect.
            float sx = asset.viewBoxSize.x > 0f ? r.width / asset.viewBoxSize.x : 1f;
            float sy = asset.viewBoxSize.y > 0f ? r.height / asset.viewBoxSize.y : 1f;
            float scale = FitToRect ? Mathf.Min(sx, sy) : 1f;
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
                    Mathf.Max(4, ShapeStack.Slot0.shape.bezierSamplesPerSegment > 0
                                 ? ShapeStack.Slot0.shape.bezierSamplesPerSegment : 16),
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
            bool hasUv1 = mb.uv1s.Count == vc;
            for (int i = 0; i < vc; i++)
            {
                var v = mb.vertices[i];
                var c = mb.colors[i];
                var uv = mb.uvs[i];
                Vector2 uv1 = hasUv1 ? mb.uv1s[i] : new Vector2(1f, 0f);
                // VertexHelper has no (pos, color, uv0, uv1) overload, so
                // populate a UIVertex and push that — same allocation cost
                // as the 3-arg form (vh keeps internal SoA arrays).
                var ui = new UIVertex
                {
                    position = v,
                    color = c,
                    uv0 = uv,
                    uv1 = uv1,
                    normal = new Vector3(0f, 0f, -1f),
                    tangent = new Vector4(1f, 0f, 0f, -1f)
                };
                vh.AddVert(ui);
            }
            int tc = mb.triangles.Count;
            for (int i = 0; i + 2 < tc; i += 3)
            {
                vh.AddTriangle(baseV + mb.triangles[i], baseV + mb.triangles[i + 1], baseV + mb.triangles[i + 2]);
            }
        }
    }
}
