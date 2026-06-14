using UnityEngine;
using VMG.Core;
using VMG.Svg;

namespace VMG.World
{
    /// World-space procedural vector renderer using MeshFilter + MeshRenderer.
    /// Shares the Core pipeline with VectorImageGraphic so identical shape data
    /// renders identically in UGUI and world space.
    [AddComponentMenu("VMG/Vector Sprite Renderer", 11)]
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class VectorSpriteRenderer : MonoBehaviour
    {
        [Tooltip("Optional SVG asset. When set, procedural shape/modifiers/style are bypassed. Object reference: NOT keyframable from AnimationClip — swap via script.")]
        public VMGShapeAsset SvgAsset;
        [Tooltip("SVG units per world unit when rendering an SVG asset. Keyframable.")]
        public float SvgUnitsPerWorldUnit = 100f;
        public ShapeStack ShapeStack = ShapeStack.WorldDefault();
        public StrokeStyle Stroke = StrokeStyle.WorldDefault;
        public FillStyle Fill = new FillStyle { enabled = true, color = Color.white };
        public DepthStyle Depth = DepthStyle.Default;
        public RoundCornerModifier RoundCorners = RoundCornerModifier.Default();
        public TrimPathModifier Trim = TrimPathModifier.Default();
        [Tooltip("Multiplies all fill and stroke colors. Keyframable.")]
        public Color Tint = Color.white;
        [Tooltip("Shader material. Object reference: NOT keyframable from AnimationClip — swap via script. Material property keyframing is also not supported through this component.")]
        public Material Material;
        [Tooltip("Texture sampled across the renderer's bounds (UV 0..1). Applied via MaterialPropertyBlock so the shared material stays shared. Object reference: NOT keyframable from AnimationClip — swap via script.")]
        public Texture Texture;
        [Tooltip("Sorting layer ID for the underlying MeshRenderer. Keyframable (integer).")]
        public int SortingLayerID;
        [Tooltip("Order in sorting layer. Keyframable.")]
        public int SortingOrder;

        private readonly ShapePipeline m_Pipeline = new ShapePipeline();
        private readonly MeshBuffer m_Combined = new MeshBuffer();
        private readonly MeshBuffer m_StrokeBuf = new MeshBuffer();
        private readonly MeshBuffer m_BackStrokeBuf = new MeshBuffer();
        private Mesh m_Mesh;
        private MeshFilter m_Filter;
        private MeshRenderer m_Renderer;
        private MaterialPropertyBlock m_PropertyBlock;
        private static readonly int s_MainTexID = Shader.PropertyToID("_MainTex");

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
        private Color m_PrevTint;
        private float m_PrevSvgUnits;
        private VMGShapeAsset m_PrevSvgAsset;
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
            if (!ReferenceEquals(m_PrevSvgAsset, SvgAsset)) return true;
            if (m_PrevTint != Tint) return true;
            if (m_PrevSvgUnits != SvgUnitsPerWorldUnit) return true;
            // SvgAsset bypasses the procedural pipeline entirely.
            if (SvgAsset != null) return false;
            if (!VectorRendererEquality.Same(m_PrevStack, ShapeStack)) return true;
            if (!VectorRendererEquality.Same(m_PrevStroke, Stroke)) return true;
            if (!VectorRendererEquality.Same(m_PrevFill, Fill)) return true;
            if (!VectorRendererEquality.Same(m_PrevDepth, Depth)) return true;
            if (!VectorRendererEquality.Same(m_PrevRound, RoundCorners)) return true;
            if (!VectorRendererEquality.Same(m_PrevTrim, Trim)) return true;
            return false;
        }

        void CaptureSnapshot()
        {
            m_PrevStack = ShapeStack;
            m_PrevStroke = Stroke;
            m_PrevFill = Fill;
            m_PrevDepth = Depth;
            m_PrevRound = RoundCorners;
            m_PrevTrim = Trim;
            m_PrevTint = Tint;
            m_PrevSvgUnits = SvgUnitsPerWorldUnit;
            m_PrevSvgAsset = SvgAsset;
            m_HasSnapshot = true;
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

            // Fill stage: ShapeStack -> RoundCorner. Trim is omitted so
            // the closed path survives for filling.
            m_Pipeline.workingPath.Clear();
            ShapeStack.Build(m_Pipeline.workingPath);
            if (RoundCorners.Enabled) RoundCorners.Apply(m_Pipeline.workingPath);
            if (Fill.enabled)
            {
                var fill = Fill; fill.color *= Tint;
                if (extrude)
                    FillMeshBuilder.BuildExtruded(m_Pipeline.workingPath, fill, Depth, m_Combined);
                else
                    FillMeshBuilder.Build(m_Pipeline.workingPath, fill, m_Combined);
            }

            // Stroke stage: ShapeStack -> RoundCorner -> Trim.
            m_StrokeBuf.Clear();
            m_Pipeline.workingPath.Clear();
            ShapeStack.Build(m_Pipeline.workingPath);
            if (RoundCorners.Enabled) RoundCorners.Apply(m_Pipeline.workingPath);
            if (Trim.Enabled) Trim.Apply(m_Pipeline.workingPath);
            if (Stroke.enabled)
            {
                var stroke = Stroke; stroke.color *= Tint;
                // In depth mode, force Inner alignment so the ribbon stays
                // inside the fill silhouette. Center/Outer would leak past
                // the side walls and break occlusion when the renderer is
                // viewed from the side.
                if (extrude) stroke.alignment = StrokeAlignment.Inner;
                StrokeMeshBuilder.Build(m_Pipeline.workingPath, stroke, m_StrokeBuf);
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
        private void BuildFromSvg()
        {
            var asset = SvgAsset;
            float scale = SvgUnitsPerWorldUnit > 1e-5f ? 1f / SvgUnitsPerWorldUnit : 1f;
            Vector2 origin = -asset.viewBoxSize * scale * 0.5f;
            // Bezier sample count comes from slot 0 (SVG ignores stacks
            // and modifiers, but it still needs a tessellation density).
            int bezSamples = Mathf.Max(4, ShapeStack.Slot0.shape.bezierSamplesPerSegment > 0
                                       ? ShapeStack.Slot0.shape.bezierSamplesPerSegment
                                       : 16);

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
                if (sub.fill.enabled)
                {
                    var fill = sub.fill; fill.color *= Tint;
                    FillMeshBuilder.Build(m_SvgPath, fill, m_Combined);
                }
                if (sub.stroke.enabled)
                {
                    var stroke = sub.stroke; stroke.color *= Tint;
                    stroke.width *= scale;
                    StrokeMeshBuilder.Build(m_SvgPath, stroke, m_Combined);
                }
            }
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
                dst.uv1s.Add(i < src.uv1s.Count ? src.uv1s[i] : new Vector2(1f, 0f));
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
