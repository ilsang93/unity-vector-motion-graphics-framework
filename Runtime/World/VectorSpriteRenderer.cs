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
        [SerializeField] private PrimitiveShapeSource m_Shape = PrimitiveShapeSource.Default();
        [SerializeField] private StrokeStyle m_Stroke = StrokeStyle.Default;
        [SerializeField] private FillStyle m_Fill = new FillStyle { enabled = true, color = Color.white };
        [SerializeField] private PathMorphModifier m_Morph = PathMorphModifier.Default();
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
        private Mesh m_Mesh;
        private MeshFilter m_Filter;
        private MeshRenderer m_Renderer;
        private MaterialPropertyBlock m_PropertyBlock;
        private static readonly int s_MainTexID = Shader.PropertyToID("_MainTex");

        public VMGShapeAsset SvgAsset { get => m_SvgAsset; set { m_SvgAsset = value; Rebuild(); } }
        public ref PrimitiveShapeSource Shape => ref m_Shape;
        public ref StrokeStyle Stroke => ref m_Stroke;
        public ref FillStyle Fill => ref m_Fill;
        public ref PathMorphModifier MorphModifier => ref m_Morph;
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

            // Fill stage: Morph -> RoundCorner. Trim is omitted so the
            // closed path survives for filling.
            m_Pipeline.workingPath.Clear();
            m_Shape.Build(m_Pipeline.workingPath);
            if (m_Morph.Enabled) m_Morph.Apply(m_Pipeline.workingPath);
            if (m_RoundCorners.Enabled) m_RoundCorners.Apply(m_Pipeline.workingPath);
            if (m_Fill.enabled)
            {
                var fill = m_Fill; fill.color *= m_Tint;
                FillMeshBuilder.Build(m_Pipeline.workingPath, fill, m_Combined);
            }

            // Stroke stage: Morph -> RoundCorner -> Trim.
            m_StrokeBuf.Clear();
            m_Pipeline.workingPath.Clear();
            m_Shape.Build(m_Pipeline.workingPath);
            if (m_Morph.Enabled) m_Morph.Apply(m_Pipeline.workingPath);
            if (m_RoundCorners.Enabled) m_RoundCorners.Apply(m_Pipeline.workingPath);
            if (m_Trim.Enabled) m_Trim.Apply(m_Pipeline.workingPath);
            if (m_Stroke.enabled)
            {
                var stroke = m_Stroke; stroke.color *= m_Tint;
                StrokeMeshBuilder.Build(m_Pipeline.workingPath, stroke, m_StrokeBuf);
            }
            AppendBuffer(m_StrokeBuf, m_Combined);

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
            int bezSamples = Mathf.Max(4, m_Shape.bezierSamplesPerSegment > 0 ? m_Shape.bezierSamplesPerSegment : 16);

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
            int baseV = dst.vertices.Count;
            for (int i = 0; i < src.vertices.Count; i++)
            {
                dst.vertices.Add(src.vertices[i]);
                dst.colors.Add(src.colors[i]);
                dst.uvs.Add(src.uvs[i]);
            }
            for (int i = 0; i < src.triangles.Count; i++) dst.triangles.Add(baseV + src.triangles[i]);
        }
    }
}
