using System.Collections.Generic;
using UnityEngine;
using VMG.Core;
using VMG.Svg;

namespace VMG.World
{
    /// World-space procedural vector renderer using MeshFilter + MeshRenderer.
    /// Shares the Core pipeline with VectorImageGraphic so identical shape data
    /// renders identically in UGUI and world space.
    [AddComponentMenu("VMG/Rendering/Vector Sprite Renderer", 1)]
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class VectorSpriteRenderer : MonoBehaviour
    {
        [Tooltip("Optional SVG asset. When set, the procedural ShapeStack and its Fill/Stroke styles are bypassed — but Round Corners / Wiggle / Trim still apply, and the Svg Fill/Stroke Override fields below can replace the asset's colors. Depth extrusion is NOT applied to SVG assets. Object reference: NOT keyframable from AnimationClip — swap via script.")]
        public VMGShapeAsset SvgAsset;
        [Tooltip("Replaces the fill colors baked into the SVG asset. Tint alone only multiplies, so this is how a black-filled icon gets recolored. Ignored when no SvgAsset is set. Keyframable.")]
        public SvgStyleOverride SvgFillOverride = SvgStyleOverride.Default();
        [Tooltip("Replaces the stroke colors (and optionally widths) baked into the SVG asset. Ignored when no SvgAsset is set. Keyframable.")]
        public SvgStyleOverride SvgStrokeOverride = SvgStyleOverride.Default();
        [Tooltip("SVG units per world unit when rendering an SVG asset. Keyframable.")]
        public float SvgUnitsPerWorldUnit = 100f;
        public ShapeStack ShapeStack = ShapeStack.WorldDefault();
        public StrokeStyle Stroke = StrokeStyle.WorldDefault;
        public FillStyle Fill = new FillStyle { enabled = true, color = Color.white };
        public DepthStyle Depth = DepthStyle.Default;
        public RoundCornerModifier RoundCorners = RoundCornerModifier.Default();
        public TrimPathModifier Trim = TrimPathModifier.Default();
        [Tooltip("AE-style wiggle: time-varying Perlin shake of every path node. Rebuilds every frame while enabled. Keyframable (intensity / frequency / seed).")]
        public WiggleModifier Wiggle = WiggleModifier.Default();
        [Tooltip("Multiplies all fill and stroke colors. Keyframable.")]
        public Color Tint = Color.white;
        [Tooltip("Shader material. Object reference: NOT keyframable from AnimationClip — swap via script. Material property keyframing is also not supported through this component.")]
        public Material Material;
        [Tooltip("Texture sampled across the renderer's bounds (UV 0..1). Applied via MaterialPropertyBlock so the shared material stays shared. Object reference: NOT keyframable from AnimationClip — swap via script.")]
        public Texture Texture;
        [Tooltip("Build strokes as one merged outline per color instead of overlapping segment / join / cap pieces, so a semi-transparent stroke never blends twice over itself or over another same-colored stroke (SVG group-opacity look). With an SvgAsset, merged strokes draw after all fills (as in UGUI). Costs a polygon union per rebuild — avoid on dense paths whose Trim / Wiggle changes every frame. Off = legacy per-segment mesh (cheaper, overlaps blend twice under alpha).")]
        public bool MergeStrokeOverlaps;
        [Tooltip("Sorting layer ID for the underlying MeshRenderer. Keyframable (integer).")]
        public int SortingLayerID;
        [Tooltip("Order in sorting layer. Keyframable.")]
        public int SortingOrder;

        private readonly ShapePipeline m_Pipeline = new ShapePipeline();
        private readonly MeshBuffer m_Combined = new MeshBuffer();
        private readonly MeshBuffer m_StrokeBuf = new MeshBuffer();
        private readonly MeshBuffer m_BackStrokeBuf = new MeshBuffer();
        private readonly StrokeMerger m_StrokeMerger = new StrokeMerger();
        private Mesh m_Mesh;
        private MeshFilter m_Filter;
        private MeshRenderer m_Renderer;
        private MaterialPropertyBlock m_PropertyBlock;
        private static readonly int s_MainTexID = Shader.PropertyToID("_MainTex");
        private static readonly int s_TrimID = Shader.PropertyToID("_VMGTrim");
        // True when the current mesh was built untrimmed with per-vertex arc
        // positions, so the shader applies Trim (see WantsGpuTrim).
        private bool m_GpuTrimBuilt;
        private bool m_PrevGpuTrim;
        private bool m_TrimBlockTouched;

        // Dirty-flag snapshot. Update compares the current mesh inputs
        // against this and only calls Rebuild when something changed —
        // saves the triangulation + Mesh upload cost on idle frames.
        // Snapshot is captured at the end of Rebuild so the steady-
        // state frame compares equal. m_HasSnapshot stays false until
        // the first Rebuild so the initial Update always rebuilds.
        private ShapeStack m_PrevStack;
        private StrokeStyle m_PrevStroke;
        private FillStyle m_PrevFill;
        private DepthStyle m_PrevDepth;
        private RoundCornerModifier m_PrevRound;
        private TrimPathModifier m_PrevTrim;
        private WiggleModifier m_PrevWiggle;
        private Color m_PrevTint;
        private float m_PrevSvgUnits;
        private VMGShapeAsset m_PrevSvgAsset;
        private SvgStyleOverride m_PrevSvgFill;
        private SvgStyleOverride m_PrevSvgStroke;
        private bool m_PrevMergeStrokes;
        private bool m_HasSnapshot;

        private void OnEnable()
        {
            EnsureRefs();
            m_HasSnapshot = false;
            Rebuild();
        }

        private void OnDisable()
        {
            if (m_Mesh != null)
            {
                if (Application.isPlaying) Destroy(m_Mesh);
                else DestroyImmediate(m_Mesh);
                m_Mesh = null;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            EnsureRefs();
            m_HasSnapshot = false;
            Rebuild();
        }
#endif

        private void Update()
        {
            // Material / texture / sorting refresh is independent of
            // mesh content (a tint change doesn't need triangulation),
            // so EnsureRefs keeps running every frame to honour
            // inspector edits. Mesh Rebuild is gated by the dirty
            // snapshot so animator-idle frames pay only the cheap
            // field compare.
            EnsureRefs();
            if (IsMeshInputDirty()) Rebuild();
        }

        bool IsMeshInputDirty()
        {
            if (!m_HasSnapshot) return true;
            // Wiggle is time-driven: rebuild every frame while active so the
            // shake animates. Defeats the value-equality gate by design.
            if (Wiggle.Enabled) return true;
            if (!ReferenceEquals(m_PrevSvgAsset, SvgAsset)) return true;
            if (m_PrevTint != Tint) return true;
            if (m_PrevSvgUnits != SvgUnitsPerWorldUnit) return true;
            if (m_PrevMergeStrokes != MergeStrokeOverlaps) return true;
            // With an SvgAsset the procedural ShapeStack and its own
            // Fill/Stroke are bypassed, but modifiers and the SVG color
            // overrides DO reach the mesh — compare those, then stop.
            // (Wiggle already forced a rebuild above when enabled.)
            if (TrimDirty()) return true;
            if (SvgAsset != null)
            {
                if (!VectorRendererEquality.Same(m_PrevRound, RoundCorners)) return true;
                if (!VectorRendererEquality.Same(m_PrevWiggle, Wiggle)) return true;
                if (!SvgStyleOverride.Same(m_PrevSvgFill, SvgFillOverride)) return true;
                if (!SvgStyleOverride.Same(m_PrevSvgStroke, SvgStrokeOverride)) return true;
                return false;
            }
            if (!VectorRendererEquality.Same(m_PrevStack, ShapeStack)) return true;
            if (!VectorRendererEquality.Same(m_PrevStroke, Stroke)) return true;
            if (!VectorRendererEquality.Same(m_PrevFill, Fill)) return true;
            if (!VectorRendererEquality.Same(m_PrevDepth, Depth)) return true;
            if (!VectorRendererEquality.Same(m_PrevRound, RoundCorners)) return true;
            if (!VectorRendererEquality.Same(m_PrevWiggle, Wiggle)) return true;
            return false;
        }

        // GPU trim: the stroke mesh is built once, untrimmed, with each
        // vertex's arc position in UV1.y, and the VMG SDF shader cuts it from
        // _VMGTrim — so animating start / end / offset costs no rebuild.
        // Falls back to the CPU TrimPathModifier when strokes are merged
        // (a union has no per-vertex arc position) or when the material's
        // shader doesn't read _VMGTrim (custom materials).
        private bool WantsGpuTrim()
        {
            if (!Trim.Enabled || MergeStrokeOverlaps) return false;
            // The shader cuts the stroke square; the CPU trim re-caps the
            // cut ends with the stroke's own cap. Only Butt looks the same.
            if (SvgAsset != null ? !SvgAsset.AllStrokeCapsButt() : Stroke.cap != LineCap.Butt) return false;
            var mat = m_Renderer != null ? m_Renderer.sharedMaterial : null;
            return mat != null && mat.HasProperty(s_TrimID);
        }

        private bool TrimDirty()
        {
            bool gpu = WantsGpuTrim();
            if (gpu != m_PrevGpuTrim) return true;
            if (gpu) return false; // window lives in the shader
            return !VectorRendererEquality.Same(m_PrevTrim, Trim);
        }

        // Pushes the trim window to the shader. Reads the renderer's block
        // first so properties other scripts set on it survive.
        private void ApplyTrim()
        {
            if (m_Renderer == null) return;
            if (!m_GpuTrimBuilt && !m_TrimBlockTouched) return;
            Vector4 v = m_GpuTrimBuilt ? new Vector4(Trim.start, Trim.end, Trim.offset, 1f) : new Vector4(0f, 1f, 0f, 0f);
            if (m_PropertyBlock == null) m_PropertyBlock = new MaterialPropertyBlock();
            m_Renderer.GetPropertyBlock(m_PropertyBlock);
            if (m_TrimBlockTouched && m_PropertyBlock.GetVector(s_TrimID) == v) return;
            m_PropertyBlock.SetVector(s_TrimID, v);
            m_Renderer.SetPropertyBlock(m_PropertyBlock);
            m_TrimBlockTouched = true;
        }

        void CaptureSnapshot()
        {
            m_PrevStack = ShapeStack;
            m_PrevStroke = Stroke;
            m_PrevFill = Fill;
            m_PrevDepth = Depth;
            m_PrevRound = RoundCorners;
            m_PrevTrim = Trim;
            m_PrevWiggle = Wiggle;
            m_PrevTint = Tint;
            m_PrevSvgUnits = SvgUnitsPerWorldUnit;
            m_PrevSvgAsset = SvgAsset;
            m_PrevSvgFill = SvgFillOverride;
            m_PrevSvgStroke = SvgStrokeOverride;
            m_PrevMergeStrokes = MergeStrokeOverlaps;
            m_PrevGpuTrim = m_GpuTrimBuilt;
            m_HasSnapshot = true;
            ApplyTrim();
        }

        /// Manually force a mesh rebuild on the next Update. Use this
        /// after mutating an external resource the renderer references
        /// but cannot detect by value — typically a SvgAsset or a
        /// VMGShapeAsset's internal data. Plain field changes (Fill,
        /// Stroke, ShapeStack, animator channels) are detected
        /// automatically and do not need this call. Also drops the
        /// SvgAsset's tessellation cache so other renderers sharing the
        /// same asset re-tessellate too.
        public void SetMeshDirty()
        {
            if (SvgAsset != null) SvgAsset.ClearTessellationCache();
            m_HasSnapshot = false;
        }

        private void EnsureRefs()
        {
            if (m_Filter == null) m_Filter = GetComponent<MeshFilter>();
            if (m_Renderer == null) m_Renderer = GetComponent<MeshRenderer>();
            if (m_Mesh == null)
            {
                m_Mesh = new Mesh { name = "VMG_VectorSprite" };
                m_Mesh.MarkDynamic();
            }
            if (m_Filter.sharedMesh != m_Mesh) m_Filter.sharedMesh = m_Mesh;
            ApplyMaterial();
            ApplyTexture();
            ApplyTrim();
            ApplySorting();
        }

        private void ApplyMaterial()
        {
            if (m_Renderer == null) return;
            if (Material != null)
            {
                if (m_Renderer.sharedMaterial != Material) m_Renderer.sharedMaterial = Material;
            }
            else if (m_Renderer.sharedMaterial == null)
            {
                // Default to the package's SDF-AA shader so vector edges
                // antialias regardless of camera distance. Falls back to
                // Sprites/Default if the shader was stripped out (e.g.
                // missing Always Included entry in build settings) — the
                // mesh still renders, just without AA.
                var shader = Shader.Find("VMG/World/VectorSDF");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader != null) m_Renderer.sharedMaterial = new Material(shader);
            }
        }

        private void ApplyTexture()
        {
            if (m_Renderer == null) return;
            if (Texture != null)
            {
                if (m_PropertyBlock == null) m_PropertyBlock = new MaterialPropertyBlock();
                m_Renderer.GetPropertyBlock(m_PropertyBlock);
                m_PropertyBlock.SetTexture(s_MainTexID, Texture);
                m_Renderer.SetPropertyBlock(m_PropertyBlock);
            }
            else if (m_PropertyBlock != null)
            {
                m_Renderer.GetPropertyBlock(m_PropertyBlock);
                if (m_PropertyBlock.HasTexture(s_MainTexID))
                {
                    m_PropertyBlock.Clear();
                    m_Renderer.SetPropertyBlock(m_PropertyBlock);
                }
            }
        }

        private void ApplySorting()
        {
            if (m_Renderer == null) return;
            m_Renderer.sortingLayerID = SortingLayerID;
            m_Renderer.sortingOrder = SortingOrder;
        }

        // Stroke sits ε above the front fill face to avoid z-fighting when
        // depth extrusion is enabled. 0.0001 world-unit is small enough to
        // be visually invisible at any reasonable camera distance but
        // large enough to win the depth test on every consumer GPU.
        private const float StrokeZBias = 1e-4f;

        public void Rebuild()
        {
            EnsureRefs();
            m_Combined.Clear();

            if (SvgAsset != null)
            {
                BuildFromSvg();
                NormalizeSvgUVs();
                m_Combined.ApplyTo(m_Mesh);
                CaptureSnapshot();
                return;
            }

            bool extrude = Depth.enabled && Depth.thickness > 0f;
            Depth.GetFaceZ(out float frontZ, out float backZ);

            float wiggleTime = WiggleTime();

            // Fill stage: ShapeStack -> RoundCorner -> Wiggle. Trim is
            // omitted so the closed path survives for filling.
            m_Pipeline.workingPath.Clear();
            ShapeStack.Build(m_Pipeline.workingPath);
            if (RoundCorners.Enabled) RoundCorners.Apply(m_Pipeline.workingPath);
            if (Wiggle.Enabled) Wiggle.Apply(m_Pipeline.workingPath, wiggleTime);
            int fillStart = m_Combined.VertexCount;
            if (Fill.enabled)
            {
                var fill = Fill; fill.color *= Tint;
                if (extrude)
                    FillMeshBuilder.BuildExtruded(m_Pipeline.workingPath, fill, Depth, m_Combined);
                else
                    FillMeshBuilder.Build(m_Pipeline.workingPath, fill, m_Combined);
            }

            // Stroke stage: ShapeStack -> RoundCorner -> Wiggle -> Trim.
            m_StrokeBuf.Clear();
            m_Pipeline.workingPath.Clear();
            ShapeStack.Build(m_Pipeline.workingPath);
            if (RoundCorners.Enabled) RoundCorners.Apply(m_Pipeline.workingPath);
            if (Wiggle.Enabled) Wiggle.Apply(m_Pipeline.workingPath, wiggleTime);
            bool gpuTrim = WantsGpuTrim();
            m_GpuTrimBuilt = gpuTrim;
            if (Trim.Enabled && !gpuTrim) Trim.Apply(m_Pipeline.workingPath);
            if (Stroke.enabled)
            {
                var stroke = Stroke; stroke.color *= Tint;
                // In depth mode, force Inner alignment so the ribbon stays
                // inside the fill silhouette. Center/Outer would leak past
                // the side walls and break occlusion when the renderer is
                // viewed from the side.
                if (extrude) stroke.alignment = StrokeAlignment.Inner;
                if (MergeStrokeOverlaps)
                {
                    m_StrokeMerger.Begin(StrokeMerger.ExtentOf(m_Pipeline.workingPath, stroke.width));
                    m_StrokeMerger.QualityTriangulation = stroke.useGradient && stroke.gradient.type == GradientType.Radial;
                    m_StrokeMerger.Add(m_Pipeline.workingPath, stroke);
                    m_StrokeMerger.Flush(m_StrokeBuf);
                }
                else if (gpuTrim) StrokeMeshBuilder.BuildWithArcLength(m_Pipeline.workingPath, stroke, m_StrokeBuf);
                else StrokeMeshBuilder.Build(m_Pipeline.workingPath, stroke, m_StrokeBuf);
            }

            // Gradient bake: recolor fill/stroke verts across the renderer's
            // union bounds (so both gradients share one frame), multiplied by
            // Tint. Runs before the stroke merge + UV normalization since
            // ApplyGradient reads each vertex's original position from its UV
            // placeholder. The fill lives at [fillStart, count) in m_Combined;
            // the stroke is still in m_StrokeBuf at this point.
            if ((Fill.enabled && Fill.useGradient) || (Stroke.enabled && Stroke.useGradient))
            {
                Rect gradBounds = GradientUnionBounds(m_Combined, fillStart, m_StrokeBuf);
                if (Fill.enabled && Fill.useGradient)
                    m_Combined.ApplyGradient(Fill.gradient, gradBounds, Tint, fillStart);
                if (Stroke.enabled && Stroke.useGradient)
                    m_StrokeBuf.ApplyGradient(Stroke.gradient, gradBounds, Tint, 0);
            }

            if (extrude)
            {
                // Duplicate the stroke ribbon onto both fill faces so the
                // outline is visible from every viewing angle in 3D.
                // Back copy is made before the front-side promote so the
                // source vertex Z is still 0.
                m_BackStrokeBuf.CopyFrom(m_StrokeBuf);

                m_StrokeBuf.PromoteToZWithFrontNormal(frontZ + StrokeZBias);
                AppendBuffer(m_StrokeBuf, m_Combined);

                m_BackStrokeBuf.PromoteToZWithFrontNormal(backZ - StrokeZBias);
                m_BackStrokeBuf.FlipForBackFace();
                AppendBuffer(m_BackStrokeBuf, m_Combined);
            }
            else
            {
                AppendBuffer(m_StrokeBuf, m_Combined);
            }

            m_Combined.NormalizeUVsToVertexBounds();
            m_Combined.ApplyTo(m_Mesh);
            CaptureSnapshot();
        }

        private void NormalizeSvgUVs()
        {
            var asset = SvgAsset;
            float scale = SvgUnitsPerWorldUnit > 1e-5f ? 1f / SvgUnitsPerWorldUnit : 1f;
            Vector2 size = asset.viewBoxSize * scale;
            Vector2 origin = -size * 0.5f;
            m_Combined.NormalizeUVsToRect(new Rect(origin.x, origin.y, size.x, size.y));
        }

        private readonly VectorPath m_SvgPath = new VectorPath();
        // Separate buffer so trimming the stroke does not destroy the
        // closed contour the fill group still needs.
        private readonly VectorPath m_SvgTrimPath = new VectorPath();
        // Accumulates consecutive same-fill contours so a path element
        // authored with holes (M..Z M..Z -> several subShapes sharing one
        // fill) fills with the even-odd rule and the inner loops carve
        // holes — matching VectorImageGraphic. Without this an SVG with
        // holes renders solid in world space but correct in UGUI.
        private readonly List<VectorPath> m_SvgFillGroup = new List<VectorPath>(8);
        private readonly List<VectorPath> m_SvgPathPool = new List<VectorPath>(8);

        private void BuildFromSvg()
        {
            var asset = SvgAsset;
            float scale = SvgUnitsPerWorldUnit > 1e-5f ? 1f / SvgUnitsPerWorldUnit : 1f;
            Vector2 origin = -asset.viewBoxSize * scale * 0.5f;
            bool gpuTrim = WantsGpuTrim();
            m_GpuTrimBuilt = gpuTrim;
            bool cpuTrim = Trim.Enabled && !gpuTrim;
            // Bezier sample count comes from slot 0 (SVG ignores the shape
            // stack, but it still needs a tessellation density).
            int bezSamples = Mathf.Max(4, ShapeStack.Slot0.shape.bezierSamplesPerSegment > 0
                                       ? ShapeStack.Slot0.shape.bezierSamplesPerSegment
                                       : 16);

            // Modifiers run in the same order as the procedural pipeline
            // (RoundCorner -> Wiggle -> Trim), per sub-shape, in WORLD-
            // scaled space so radius / intensity read in meters like the
            // procedural path.
            float wiggleTime = WiggleTime();

            for (int i = 0; i < m_SvgFillGroup.Count; i++) m_SvgPathPool.Add(m_SvgFillGroup[i]);
            m_SvgFillGroup.Clear();
            FillStyle groupFill = default;
            bool haveGroup = false;

            int svgFillStart = m_Combined.VertexCount;
            if (MergeStrokeOverlaps)
            {
                Vector2 extent = asset.viewBoxSize * scale;
                m_StrokeMerger.Begin(Mathf.Max(extent.x, extent.y));
                // One gradient bake covers fill and stroke here (see below).
                m_StrokeMerger.QualityTriangulation =
                    (SvgFillOverride.enabled && SvgFillOverride.useGradient && SvgFillOverride.gradient.type == GradientType.Radial) ||
                    (SvgStrokeOverride.enabled && SvgStrokeOverride.useGradient && SvgStrokeOverride.gradient.type == GradientType.Radial);
            }

            for (int s = 0; s < asset.subShapes.Count; s++)
            {
                var sub = asset.subShapes[s];
                if (sub == null || sub.nodes.Count < 2) continue;

                // Reuse the asset's cached bezier-tessellated polyline; copy
                // it onto our path before applying the world transform so
                // the shared cache stays untouched. The cache survives
                // until the SvgAsset is mutated (SetMeshDirty clears it).
                var tessellated = asset.GetTessellation(s, bezSamples);
                if (tessellated == null) continue;
                m_SvgPath.CopyFrom(tessellated);
                for (int i = 0; i < m_SvgPath.nodes.Count; i++)
                {
                    var n = m_SvgPath.nodes[i];
                    n.position = origin + n.position * scale;
                    m_SvgPath.nodes[i] = n;
                }

                if (RoundCorners.Enabled) RoundCorners.Apply(m_SvgPath);
                if (Wiggle.Enabled) Wiggle.Apply(m_SvgPath, wiggleTime);

                if (sub.fill.enabled)
                {
                    // Group key is the AUTHORED fill, never the override:
                    // with an override on, every sub-shape would collapse
                    // into one group and separate shapes would carve
                    // holes out of each other via even-odd.
                    if (haveGroup && !SameSvgFill(groupFill, sub.fill))
                        FlushSvgFillGroup(groupFill);
                    groupFill = sub.fill;
                    haveGroup = true;
                    var clone = RentSvgPath();
                    clone.CopyFrom(m_SvgPath);
                    m_SvgFillGroup.Add(clone);
                }
                if (sub.stroke.enabled)
                {
                    // Trim applies to the stroke only, as in the
                    // procedural path: the fill keeps its closed contour.
                    if (cpuTrim)
                    {
                        m_SvgTrimPath.CopyFrom(m_SvgPath);
                        Trim.Apply(m_SvgTrimPath);
                    }
                    var strokePath = cpuTrim ? m_SvgTrimPath : m_SvgPath;

                    var stroke = SvgStrokeOverride.ApplyTo(sub.stroke);
                    stroke.color *= Tint;
                    if (!SvgStrokeOverride.overrideWidth) stroke.width *= scale;
                    if (MergeStrokeOverlaps) m_StrokeMerger.Add(strokePath, stroke);
                    else if (gpuTrim) StrokeMeshBuilder.BuildWithArcLength(strokePath, stroke, m_Combined);
                    else StrokeMeshBuilder.Build(strokePath, stroke, m_Combined);
                }
            }
            if (haveGroup) FlushSvgFillGroup(groupFill);
            if (MergeStrokeOverlaps) m_StrokeMerger.Flush(m_Combined);

            // Gradient bake across the whole SVG's fitted bounds, so fill
            // and stroke share one coordinate frame (mirrors the
            // procedural branch, which previously had no SVG equivalent).
            if ((SvgFillOverride.enabled && SvgFillOverride.useGradient) ||
                (SvgStrokeOverride.enabled && SvgStrokeOverride.useGradient))
            {
                Vector2 size = asset.viewBoxSize * scale;
                Rect fitRect = new Rect(origin.x, origin.y, size.x, size.y);
                // One bake over everything emitted in this pass: fill and
                // stroke verts are interleaved in m_Combined, so they are
                // recolored together from svgFillStart onward.
                var grad = SvgFillOverride.enabled && SvgFillOverride.useGradient
                    ? SvgFillOverride.gradient : SvgStrokeOverride.gradient;
                m_Combined.ApplyGradient(grad, fitRect, Tint, svgFillStart);
            }

            // Flat SVG geometry carries no normals, and MeshBuffer.ApplyTo
            // skips SetNormals unless every vertex has one — which left
            // any lighting-aware material undefined on SVG while the
            // procedural path (extruded) worked. Emit front-facing
            // normals so the same material behaves the same way on both.
            m_Combined.EnsureFrontNormals();
        }

        private void FlushSvgFillGroup(FillStyle fillStyle)
        {
            if (m_SvgFillGroup.Count > 0 && fillStyle.enabled)
            {
                var fill = SvgFillOverride.ApplyTo(fillStyle);
                fill.color *= Tint;
                FillMeshBuilder.BuildMulti(m_SvgFillGroup, fill, m_Combined);
            }
            for (int i = 0; i < m_SvgFillGroup.Count; i++) m_SvgPathPool.Add(m_SvgFillGroup[i]);
            m_SvgFillGroup.Clear();
        }

        private VectorPath RentSvgPath()
        {
            if (m_SvgPathPool.Count > 0)
            {
                var p = m_SvgPathPool[m_SvgPathPool.Count - 1];
                m_SvgPathPool.RemoveAt(m_SvgPathPool.Count - 1);
                return p;
            }
            return new VectorPath();
        }

        // Two fills belong to the same path element when color + gradient match.
        private static bool SameSvgFill(in FillStyle a, in FillStyle b)
        {
            if (a.useGradient != b.useGradient) return false;
            if (a.color != b.color) return false;
            return true;
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

        // 2D bounds spanning the fill range of `combined` (verts [fillStart,
        // count)) plus all of `stroke`. Used as the shared coordinate frame
        // for fill + stroke gradient evaluation. Reads positions from each
        // vertex's UV placeholder (still the raw 2D position pre-normalize).
        private static Rect GradientUnionBounds(MeshBuffer combined, int fillStart, MeshBuffer stroke)
        {
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            for (int i = fillStart; i < combined.uvs.Count; i++)
                AccumulateUv(combined.uvs[i], ref minX, ref minY, ref maxX, ref maxY);
            for (int i = 0; i < stroke.uvs.Count; i++)
                AccumulateUv(stroke.uvs[i], ref minX, ref minY, ref maxX, ref maxY);
            if (float.IsPositiveInfinity(minX)) return new Rect(0f, 0f, 1f, 1f);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static void AccumulateUv(Vector2 v, ref float minX, ref float minY, ref float maxX, ref float maxY)
        {
            if (v.x < minX) minX = v.x;
            if (v.y < minY) minY = v.y;
            if (v.x > maxX) maxX = v.x;
            if (v.y > maxY) maxY = v.y;
        }

        private static void AppendBuffer(MeshBuffer src, MeshBuffer dst)
        {
            // Empty src is a no-op — preserve dst's existing normals (the
            // common case when stroke is disabled but fill is extruded).
            if (src.vertices.Count == 0) return;

            int baseV = dst.vertices.Count;
            for (int i = 0; i < src.vertices.Count; i++)
            {
                dst.vertices.Add(src.vertices[i]);
                dst.colors.Add(src.colors[i]);
                dst.uvs.Add(src.uvs[i]);
                dst.uv1s.Add(i < src.uv1s.Count ? src.uv1s[i] : new Vector2(1f, MeshBuffer.NoArc));
            }
            // Carry normals only if both sides actually have a normal for
            // every vertex; otherwise drop them so ApplyTo skips SetNormals.
            bool srcHasNormals = src.normals.Count == src.vertices.Count;
            bool dstHasNormals = dst.normals.Count == baseV;
            if (srcHasNormals && (dstHasNormals || baseV == 0))
            {
                for (int i = 0; i < src.normals.Count; i++) dst.normals.Add(src.normals[i]);
            }
            else
            {
                dst.normals.Clear();
            }
            for (int i = 0; i < src.triangles.Count; i++) dst.triangles.Add(baseV + src.triangles[i]);
        }
    }
}
