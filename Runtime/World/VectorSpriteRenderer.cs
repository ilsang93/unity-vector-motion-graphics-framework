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
        [SerializeField] private VMGShapeAsset m_SvgAsset;
        [Tooltip("SVG units per world unit when rendering an SVG asset. Keyframable.")]
        [SerializeField] private float m_SvgUnitsPerWorldUnit = 100f;
        [SerializeField] private ShapeStack m_ShapeStack = ShapeStack.Default();
        [SerializeField] private StrokeStyle m_Stroke = StrokeStyle.Default;
        [SerializeField] private FillStyle m_Fill = new FillStyle { enabled = true, color = Color.white };
        [SerializeField] private DepthStyle m_Depth = DepthStyle.Default;
        [SerializeField] private RoundCornerModifier m_RoundCorners = RoundCornerModifier.Default();
        [SerializeField] private TrimPathModifier m_Trim = TrimPathModifier.Default();
        [Tooltip("Multiplies all fill and stroke colors. Keyframable.")]
        [SerializeField] private Color m_Tint = Color.white;
        [Tooltip("Shader material. Object reference: NOT keyframable from AnimationClip — swap via script. Material property keyframing is also not supported through this component.")]
        [SerializeField] private Material m_Material;
        [Tooltip("Texture sampled across the renderer's bounds (UV 0..1). Applied via MaterialPropertyBlock so the shared material stays shared. Object reference: NOT keyframable from AnimationClip — swap via script.")]
        [SerializeField] private Texture m_Texture;
        [Tooltip("Sorting layer ID for the underlying MeshRenderer. Keyframable (integer).")]
        [SerializeField] private int m_SortingLayerID;
        [Tooltip("Order in sorting layer. Keyframable.")]
        [SerializeField] private int m_SortingOrder;

        private readonly ShapePipeline m_Pipeline = new ShapePipeline();
        private readonly MeshBuffer m_Combined = new MeshBuffer();
        private readonly MeshBuffer m_StrokeBuf = new MeshBuffer();
        private readonly MeshBuffer m_BackStrokeBuf = new MeshBuffer();
        private Mesh m_Mesh;
        private MeshFilter m_Filter;
        private MeshRenderer m_Renderer;
        private MaterialPropertyBlock m_PropertyBlock;
        private static readonly int s_MainTexID = Shader.PropertyToID("_MainTex");

        public VMGShapeAsset SvgAsset { get => m_SvgAsset; set { m_SvgAsset = value; Rebuild(); } }
        public ref ShapeStack ShapeStack => ref m_ShapeStack;
        public ref StrokeStyle Stroke => ref m_Stroke;
        public ref FillStyle Fill => ref m_Fill;
        public ref DepthStyle Depth => ref m_Depth;
        public ref RoundCornerModifier RoundCornerModifier => ref m_RoundCorners;
        public ref TrimPathModifier TrimModifier => ref m_Trim;
        public Material Material { get => m_Material; set { m_Material = value; ApplyMaterial(); } }
        public Texture Texture { get => m_Texture; set { m_Texture = value; ApplyTexture(); } }
        public int SortingLayerID { get => m_SortingLayerID; set { m_SortingLayerID = value; ApplySorting(); } }
        public int SortingOrder { get => m_SortingOrder; set { m_SortingOrder = value; ApplySorting(); } }

        private void OnEnable()
        {
            EnsureRefs();
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
            Rebuild();
        }
#endif

        private void Update()
        {
            // Cheap rebuild every frame so Animator-driven values reflect.
            // For static shapes a dirty flag would be more efficient; keep
            // simple in MVP.
            Rebuild();
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
            if (m_Material != null)
            {
                if (m_Renderer.sharedMaterial != m_Material) m_Renderer.sharedMaterial = m_Material;
            }
            else if (m_Renderer.sharedMaterial == null)
            {
                // Default to Sprites/Default which understands vertex color.
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) m_Renderer.sharedMaterial = new Material(shader);
            }
        }

        private void ApplyTexture()
        {
            if (m_Renderer == null) return;
            if (m_Texture != null)
            {
                if (m_PropertyBlock == null) m_PropertyBlock = new MaterialPropertyBlock();
                m_Renderer.GetPropertyBlock(m_PropertyBlock);
                m_PropertyBlock.SetTexture(s_MainTexID, m_Texture);
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
            m_Renderer.sortingLayerID = m_SortingLayerID;
            m_Renderer.sortingOrder = m_SortingOrder;
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

            if (m_SvgAsset != null)
            {
                BuildFromSvg();
                NormalizeSvgUVs();
                m_Combined.ApplyTo(m_Mesh);
                return;
            }

            bool extrude = m_Depth.enabled && m_Depth.thickness > 0f;
            m_Depth.GetFaceZ(out float frontZ, out float backZ);

            // Fill stage: ShapeStack -> RoundCorner. Trim is omitted so
            // the closed path survives for filling.
            m_Pipeline.workingPath.Clear();
            m_ShapeStack.Build(m_Pipeline.workingPath);
            if (m_RoundCorners.Enabled) m_RoundCorners.Apply(m_Pipeline.workingPath);
            if (m_Fill.enabled)
            {
                var fill = m_Fill; fill.color *= m_Tint;
                if (extrude)
                    FillMeshBuilder.BuildExtruded(m_Pipeline.workingPath, fill, m_Depth, m_Combined);
                else
                    FillMeshBuilder.Build(m_Pipeline.workingPath, fill, m_Combined);
            }

            // Stroke stage: ShapeStack -> RoundCorner -> Trim.
            m_StrokeBuf.Clear();
            m_Pipeline.workingPath.Clear();
            m_ShapeStack.Build(m_Pipeline.workingPath);
            if (m_RoundCorners.Enabled) m_RoundCorners.Apply(m_Pipeline.workingPath);
            if (m_Trim.Enabled) m_Trim.Apply(m_Pipeline.workingPath);
            if (m_Stroke.enabled)
            {
                var stroke = m_Stroke; stroke.color *= m_Tint;
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
        }

        private void NormalizeSvgUVs()
        {
            var asset = m_SvgAsset;
            float scale = m_SvgUnitsPerWorldUnit > 1e-5f ? 1f / m_SvgUnitsPerWorldUnit : 1f;
            Vector2 size = asset.viewBoxSize * scale;
            Vector2 origin = -size * 0.5f;
            m_Combined.NormalizeUVsToRect(new Rect(origin.x, origin.y, size.x, size.y));
        }

        private readonly VectorPath m_SvgPath = new VectorPath();
        private void BuildFromSvg()
        {
            var asset = m_SvgAsset;
            float scale = m_SvgUnitsPerWorldUnit > 1e-5f ? 1f / m_SvgUnitsPerWorldUnit : 1f;
            Vector2 origin = -asset.viewBoxSize * scale * 0.5f;
            // Bezier sample count comes from slot 0 (SVG ignores stacks
            // and modifiers, but it still needs a tessellation density).
            int bezSamples = Mathf.Max(4, m_ShapeStack.m_Slot0.shape.bezierSamplesPerSegment > 0
                                       ? m_ShapeStack.m_Slot0.shape.bezierSamplesPerSegment
                                       : 16);

            for (int s = 0; s < asset.subShapes.Count; s++)
            {
                var sub = asset.subShapes[s];
                if (sub == null || sub.nodes.Count < 2) continue;

                BezierTessellator.Tessellate(sub.nodes, sub.closed, bezSamples, m_SvgPath);
                for (int i = 0; i < m_SvgPath.nodes.Count; i++)
                {
                    var n = m_SvgPath.nodes[i];
                    n.position = origin + n.position * scale;
                    m_SvgPath.nodes[i] = n;
                }
                if (sub.fill.enabled)
                {
                    var fill = sub.fill; fill.color *= m_Tint;
                    FillMeshBuilder.Build(m_SvgPath, fill, m_Combined);
                }
                if (sub.stroke.enabled)
                {
                    var stroke = sub.stroke; stroke.color *= m_Tint;
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
