using UnityEngine;
using TMPro;
using VMG.Core;
using VMG.Svg;

namespace VMG.Text
{
    /// World-space variant of Vector Image With TMP. Lives on the same
    /// GameObject as a world TextMeshPro; suppresses TMP's own mesh and draws
    /// the text as a vector mesh (fill + stroke + wiggle) via the shared VMG
    /// builders, on an auto-managed child MeshFilter/MeshRenderer so it never
    /// fights TMP for the object's renderer.
    ///
    /// Glyph contours are pre-placed in local space by the base class, so the
    /// child mesh draws them at scale 1 right where TMP laid them out.
    [AddComponentMenu("VMG/Rendering/Vector Text World (TMP)", 3)]
    [RequireComponent(typeof(TextMeshPro))]
    [ExecuteAlways]
    public sealed class VMGVectorTextWorld : VMGVectorTextBase
    {
        public StrokeStyle Stroke = StrokeStyle.WorldDefault;
        public FillStyle Fill = new FillStyle { enabled = true, color = Color.white };
        [Tooltip("AE-style wiggle of every glyph contour. Rebuilds every frame while enabled.")]
        public WiggleModifier Wiggle = WiggleModifier.Default();
        [Tooltip("Multiplies fill and stroke colors.")]
        public Color Tint = Color.white;
        [Tooltip("Shader material for the vector mesh. Defaults to VMG/World/VectorSDF.")]
        public Material Material;

        private const string ChildName = "__VMGVectorTextMesh";

        private TextMeshPro m_Tmp;
        public override TMP_Text Tmp
        {
            get
            {
                if (m_Tmp == null) m_Tmp = GetComponent<TextMeshPro>();
                return m_Tmp;
            }
        }

        private Transform m_Child;
        private MeshFilter m_Filter;
        private MeshRenderer m_Renderer;
        private Mesh m_Mesh;

        private readonly MeshBuffer m_Combined = new MeshBuffer();
        private readonly MeshBuffer m_StrokeBuf = new MeshBuffer();
        private readonly GlyphFillEmitter m_Emitter = new GlyphFillEmitter();

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureChild();
            RebuildShape();
            RebuildMesh();
        }

        private void OnDisable()
        {
            if (m_Renderer != null) m_Renderer.enabled = false;
            if (m_Mesh != null)
            {
                if (Application.isPlaying) Destroy(m_Mesh); else DestroyImmediate(m_Mesh);
                m_Mesh = null;
            }
        }

        private void EnsureChild()
        {
            if (m_Child == null)
            {
                var existing = transform.Find(ChildName);
                if (existing != null) m_Child = existing;
            }
            if (m_Child == null)
            {
                var go = new GameObject(ChildName);
                go.hideFlags = HideFlags.DontSave;
                m_Child = go.transform;
                m_Child.SetParent(transform, false);
            }
            m_Child.localPosition = Vector3.zero;
            m_Child.localRotation = Quaternion.identity;
            m_Child.localScale = Vector3.one;

            m_Filter = m_Child.GetComponent<MeshFilter>();
            if (m_Filter == null) m_Filter = m_Child.gameObject.AddComponent<MeshFilter>();
            m_Renderer = m_Child.GetComponent<MeshRenderer>();
            if (m_Renderer == null) m_Renderer = m_Child.gameObject.AddComponent<MeshRenderer>();
            m_Renderer.enabled = true;

            if (m_Mesh == null)
            {
                m_Mesh = new Mesh { name = "VMGVectorText", hideFlags = HideFlags.DontSave };
                m_Filter.sharedMesh = m_Mesh;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            SetDirty();
        }
#endif

        private void Update()
        {
            SuppressTmpRendering();
            EnsureChild();
            EnsureMaterial();

            // Two-level gate (see VMGVectorTextUGUI): shape inputs reparse
            // contours; style inputs (fill/stroke/wiggle/tint) only re-mesh.
            // The frame Wiggle turns off is StyleDirty, so the mesh settles
            // flat instead of freezing mid-shake.
            bool shapeDirty = IsShapeDirty();
            if (shapeDirty) RebuildShape();
            if (shapeDirty || IsStyleDirty() || Wiggle.Enabled)
            {
                RebuildMesh();
                CaptureSnapshot();
            }
        }

        private void EnsureMaterial()
        {
            if (m_Renderer == null) return;
            var mat = Material;
            if (mat == null)
            {
                var shader = Shader.Find("VMG/World/VectorSDF");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader != null) mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (mat != null && m_Renderer.sharedMaterial != mat) m_Renderer.sharedMaterial = mat;
        }

        // ============================================================
        //  Mesh build
        // ============================================================

        private void RebuildMesh()
        {
            if (m_Mesh == null || m_Shape == null) { if (m_Mesh != null) m_Mesh.Clear(); return; }

            m_Combined.Clear();
            m_StrokeBuf.Clear();

            // Fill and stroke go into SEPARATE buffers so per-style gradients
            // recolor only their own verts (a shared buffer would let the fill
            // gradient clobber the stroke). Then merge for a single upload.
            m_Emitter.Emit(
                m_Shape, m_GlyphOfSub,
                Fill, Stroke, Tint, Color.white,
                Wiggle, WiggleTime(),
                Mathf.Max(2, curveQuality), Warp.Enabled,
                m_Combined, m_StrokeBuf);

            m_Combined.Append(m_StrokeBuf);
            m_Combined.ApplyTo(m_Mesh);
        }

        private static float WiggleTime()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif
            return Time.time;
        }

        // ---- dirty gate ----
        // Shape inputs (contour-affecting).
        private string m_PrevText;
        private float m_PrevFontSize;
        private TMP_FontAsset m_PrevFont;
        private int m_PrevQuality;
        private VMGTextWarp m_PrevWarp;
        // Layout inputs: wrap depends on rect width + margin.
        private Vector2 m_PrevRectSize;
        private Vector4 m_PrevMargin;
        // Style inputs (mesh-emit-affecting only).
        private StrokeStyle m_PrevStroke;
        private FillStyle m_PrevFill;
        private WiggleModifier m_PrevWiggle;
        private Color m_PrevTint;
        private bool m_HasSnap;

        private bool IsShapeDirty()
        {
            var t = Tmp;
            if (!m_HasSnap || t == null) return true;
            if (m_PrevText != t.text) return true;
            if (!Mathf.Approximately(m_PrevFontSize, t.fontSize)) return true;
            if (!ReferenceEquals(m_PrevFont, t.font)) return true;
            if (m_PrevQuality != curveQuality) return true;
            if (!WarpSame(m_PrevWarp, Warp)) return true;
            if ((m_PrevRectSize - t.rectTransform.rect.size).sqrMagnitude > 1e-4f) return true;
            if ((m_PrevMargin - t.margin).sqrMagnitude > 1e-4f) return true;
            return false;
        }

        private bool IsStyleDirty()
        {
            if (!m_HasSnap) return true;
            if (!VectorRendererEquality.Same(m_PrevStroke, Stroke)) return true;
            if (!VectorRendererEquality.Same(m_PrevFill, Fill)) return true;
            if (!VectorRendererEquality.Same(m_PrevWiggle, Wiggle)) return true;
            if (m_PrevTint != Tint) return true;
            return false;
        }

        private void CaptureSnapshot()
        {
            var t = Tmp;
            m_PrevText = t != null ? t.text : null;
            m_PrevFontSize = t != null ? t.fontSize : 0f;
            m_PrevFont = t != null ? t.font : null;
            m_PrevQuality = curveQuality;
            m_PrevWarp = WarpSnapshot(Warp);
            if (t != null) { m_PrevRectSize = t.rectTransform.rect.size; m_PrevMargin = t.margin; }
            m_PrevStroke = Stroke;
            m_PrevFill = Fill;
            m_PrevWiggle = Wiggle;
            m_PrevTint = Tint;
            m_HasSnap = true;
        }

        /// Force a rebuild on the next Update (e.g. after SetFontBytes).
        public void SetDirty() { m_HasSnap = false; }

        public override void EditorRebuildAndPush()
        {
            EnsureChild();
            EnsureMaterial();
            RebuildShape();
            RebuildMesh();
            CaptureSnapshot();
        }
    }
}
