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
        [Tooltip("AE-style wiggle: time-varying Perlin shake of every path node. Rebuilds every frame while enabled. Keyframable (intensity / frequency / seed).")]
        public WiggleModifier Wiggle = WiggleModifier.Default();
        [Tooltip("Stretch the shape to fill the RectTransform. When true, ShapeStack slot center/size channels are overwritten every frame from the RectTransform — animate RectTransform.sizeDelta instead. Keyframable.")]
        public bool FitToRect = true;
        [Tooltip("Texture sampled across the renderer's bounds (UV 0..1). Object reference: NOT keyframable from AnimationClip — swap via script.")]
        public Texture Texture;

        private readonly ShapePipeline m_Pipeline = new ShapePipeline();
        private readonly MeshBuffer m_StrokeBuf = new MeshBuffer();

        // Dirty-flag snapshot. LateUpdate compares the current mesh
        // inputs against this and skips SetVerticesDirty when nothing
        // changed. Captured AFTER each rebuild so the steady-state
        // (animator-idle) frame compares equal and the rebuild is
        // skipped. m_HasSnapshot stays false until the first rebuild
        // so the very first LateUpdate always dirties.
        private ShapeStack m_PrevStack;
        private StrokeStyle m_PrevStroke;
        private FillStyle m_PrevFill;
        private RoundCornerModifier m_PrevRound;
        private TrimPathModifier m_PrevTrim;
        private WiggleModifier m_PrevWiggle;
        private Color m_PrevGraphicColor;
        private bool m_PrevFitToRect;
        private VMGShapeAsset m_PrevSvgAsset;
        private Texture m_PrevTexture;
        private bool m_HasSnapshot;

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
            m_HasSnapshot = false; // force a rebuild on the first LateUpdate
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
            m_HasSnapshot = false;
            SetVerticesDirty();
        }
#endif

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (FitToRect)
            {
                m_HasSnapshot = false;
                SetVerticesDirty();
            }
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnsureCanvasUv1Channel();
        }

        /// Animator/Timeline-driven [SerializeField] writes don't go
        /// through any setter, so we poll every LateUpdate. The gate
        /// compares the current mesh inputs against a snapshot from the
        /// last rebuild and only calls SetVerticesDirty when something
        /// actually changed — saves the OnPopulateMesh / triangulation
        /// cost on idle frames. Snapshot is captured at the end of
        /// OnPopulateMesh so animator-driven channel writes are
        /// detected on the next frame.
        private void LateUpdate()
        {
            if (IsMeshInputDirty()) SetVerticesDirty();
        }

        // Edit-mode-safe clock for wiggle: Time.time only advances in Play,
        // so use the editor wall clock when not playing so wiggle previews
        // live in the scene/game view at author time.
        private static float WiggleTime()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif
            return Time.time;
        }

        bool IsMeshInputDirty()
        {
            if (!m_HasSnapshot) return true;
            // Wiggle is time-driven: rebuild every frame while it's active so
            // the shake animates. Defeats the value-equality gate by design.
            if (Wiggle.Enabled) return true;
            // FitToRect overwrites ShapeStack slots from rectTransform.rect
            // on every OnPopulateMesh. Parent Canvas resizes, anchor
            // changes, scaler updates and layout-group fixups can shift
            // the rect (size, position, or both) without an
            // OnRectTransformDimensionsChange callback in the same frame,
            // so a value-equality gate misses those cases. Skip the gate
            // entirely when FitToRect is on — the rebuild cost is no
            // worse than 0.36.0-pre and the visual stays correct.
            // FitToRect=false (user-driven sizing) keeps the gate.
            if (FitToRect) return true;
            if (!ReferenceEquals(m_PrevSvgAsset, SvgAsset)) return true;
            if (!ReferenceEquals(m_PrevTexture, Texture)) return true;
            if (m_PrevGraphicColor != color) return true;
            if (m_PrevFitToRect != FitToRect) return true;
            // When an SvgAsset is assigned the procedural pipeline is
            // bypassed entirely, so the shape / modifier / fill / stroke
            // fields don't influence the mesh. Skip their comparison.
            if (SvgAsset != null) return false;
            if (!VectorRendererEquality.Same(m_PrevStack, ShapeStack)) return true;
            if (!VectorRendererEquality.Same(m_PrevStroke, Stroke)) return true;
            if (!VectorRendererEquality.Same(m_PrevFill, Fill)) return true;
            if (!VectorRendererEquality.Same(m_PrevRound, RoundCorners)) return true;
            if (!VectorRendererEquality.Same(m_PrevTrim, Trim)) return true;
            if (!VectorRendererEquality.Same(m_PrevWiggle, Wiggle)) return true;
            return false;
        }

        void CaptureSnapshot()
        {
            m_PrevStack = ShapeStack;
            m_PrevStroke = Stroke;
            m_PrevFill = Fill;
            m_PrevRound = RoundCorners;
            m_PrevTrim = Trim;
            m_PrevWiggle = Wiggle;
            m_PrevGraphicColor = color;
            m_PrevFitToRect = FitToRect;
            m_PrevSvgAsset = SvgAsset;
            m_PrevTexture = Texture;
            m_HasSnapshot = true;
        }

        /// Manually force a mesh rebuild on the next LateUpdate. Use
        /// this after mutating an external resource the renderer
        /// references but cannot detect by value — typically a
        /// SvgAsset, a VMGShapeAsset's internal data, or a FreePath's
        /// legacy node list. Plain field changes (Fill.color,
        /// Stroke.width, ShapeStack slots, animator channels) are
        /// detected automatically and do not need this call.
        /// Also drops the SvgAsset's tessellation cache so other
        /// renderers sharing the same asset re-tessellate too.
        public void SetMeshDirty()
        {
            if (SvgAsset != null) SvgAsset.ClearTessellationCache();
            m_HasSnapshot = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (SvgAsset != null)
            {
                PopulateFromSvg(vh);
                CaptureSnapshot();
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

            // Pipeline: ShapeStack -> RoundCorner -> Wiggle -> Trim. Fill
            // skips Trim so the closed shape survives the slice.
            //
            // Direct calls (not an IPathModifier list) so the struct
            // modifiers don't get boxed every frame.

            float wiggleTime = WiggleTime();

            // Fill pipeline.
            m_Pipeline.workingPath.Clear();
            m_Pipeline.mesh.Clear();
            ShapeStack.Build(m_Pipeline.workingPath);
            if (RoundCorners.Enabled) RoundCorners.Apply(m_Pipeline.workingPath);
            if (Wiggle.Enabled) Wiggle.Apply(m_Pipeline.workingPath, wiggleTime);
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
            if (Wiggle.Enabled) Wiggle.Apply(m_Pipeline.workingPath, wiggleTime);
            if (Trim.Enabled) Trim.Apply(m_Pipeline.workingPath);
            if (Stroke.enabled)
            {
                var stroke = Stroke;
                stroke.color *= color;
                StrokeMeshBuilder.Build(m_Pipeline.workingPath, stroke, m_StrokeBuf);
            }

            // Gradient bake: recolor fill/stroke verts across the renderer's
            // union bounds (so both gradients share one coordinate frame),
            // multiplied by Graphic.color tint. Runs before UV normalization
            // since ApplyGradient reads each vertex's original position from
            // its UV placeholder. The same bounds double as the texture UV
            // rect when FitToRect is off, so only compute them once.
            bool fillGrad = Fill.enabled && Fill.useGradient;
            bool strokeGrad = Stroke.enabled && Stroke.useGradient;
            bool needBounds = fillGrad || strokeGrad || !FitToRect;
            Rect gradBounds = needBounds ? VertexUnionBounds(m_Pipeline.mesh, m_StrokeBuf) : default;
            if (fillGrad) m_Pipeline.mesh.ApplyGradient(Fill.gradient, gradBounds, color, 0);
            if (strokeGrad) m_StrokeBuf.ApplyGradient(Stroke.gradient, gradBounds, color, 0);

            // Normalize UVs across the union of fill + stroke so a texture
            // lays continuously over the whole renderer.
            Rect uvRect = FitToRect ? rectTransform.rect : gradBounds;
            m_Pipeline.mesh.NormalizeUVsToRect(uvRect);
            m_StrokeBuf.NormalizeUVsToRect(uvRect);

            // Push fill mesh, then stroke mesh, into VertexHelper.
            AppendBufferToVH(m_Pipeline.mesh, vh);
            AppendBufferToVH(m_StrokeBuf, vh);

            // Capture AFTER FitToRect has overwritten slot center/size,
            // so the next LateUpdate sees a stable ShapeStack across
            // frames where the RectTransform didn't change.
            CaptureSnapshot();
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

            int bezSamples = Mathf.Max(4, ShapeStack.Slot0.shape.bezierSamplesPerSegment > 0
                                       ? ShapeStack.Slot0.shape.bezierSamplesPerSegment : 16);
            for (int s = 0; s < asset.subShapes.Count; s++)
            {
                var sub = asset.subShapes[s];
                if (sub == null || sub.nodes.Count < 2) continue;

                // Reuse the asset's cached bezier-tessellated polyline; copy
                // it onto our path before applying the fit transform so the
                // shared cache stays untouched. The cache survives until the
                // SvgAsset is mutated (SetMeshDirty clears it).
                var tessellated = asset.GetTessellation(s, bezSamples);
                if (tessellated == null) continue;
                m_SvgPath.CopyFrom(tessellated);
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
