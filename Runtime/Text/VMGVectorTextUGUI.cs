using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VMG.Core;
using VMG.Svg;

namespace VMG.Text
{
    /// Canvas variant of Vector Image With TMP. Lives on the same
    /// GameObject as a TextMeshProUGUI; suppresses TMP's own rendering and
    /// draws the text as a vector graphic (fill + stroke + wiggle) through
    /// the shared VMG mesh builders.
    ///
    /// UGUI allows only one Graphic per CanvasRenderer, and TextMeshProUGUI
    /// already owns this object's. So the vector mesh is drawn by a companion
    /// VMGVectorTextGraphic on an auto-managed CHILD object, stretched to
    /// match this RectTransform so glyph local coords line up 1:1. TMP stays
    /// on this object purely as the layout engine (renderMode = DontRender).
    ///
    /// Mesh emission mirrors VectorImageGraphic.PopulateFromSvg but skips
    /// fit-to-rect: glyph contours are already placed in local space by the
    /// base class, so they draw at scale 1 exactly where TMP laid them out.
    [AddComponentMenu("VMG/Rendering/Vector Text (UI, TMP)", 2)]
    [RequireComponent(typeof(TextMeshProUGUI))]
    [ExecuteAlways]
    public sealed class VMGVectorTextUGUI : VMGVectorTextBase
    {
        public StrokeStyle Stroke = StrokeStyle.Default;
        public FillStyle Fill = new FillStyle { enabled = true, color = Color.white };
        [Tooltip("AE-style wiggle of every glyph contour. Rebuilds every frame while enabled.")]
        public WiggleModifier Wiggle = WiggleModifier.Default();
        [Tooltip("Multiplies fill and stroke colors.")]
        public Color Tint = Color.white;
        [Tooltip("Shader material for the vector mesh. Leave empty to use the default VMG/UI/VectorSDF material. " +
                 "Vector text is a plain mesh, so any UI material works (e.g. a gradient or texture shader).")]
        public Material Material;

        private const string ChildName = "__VMGVectorTextMesh";

        private TextMeshProUGUI m_Tmp;
        public override TMP_Text Tmp
        {
            get
            {
                if (m_Tmp == null) m_Tmp = GetComponent<TextMeshProUGUI>();
                return m_Tmp;
            }
        }

        private VMGVectorTextGraphic m_Graphic;

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureGraphic();
            PushToGraphic();
        }

        private void OnDisable()
        {
            // Leave the child in place (cheap, hidden) but stop it drawing.
            if (m_Graphic != null) m_Graphic.enabled = false;
        }

        // Creates / re-acquires the companion child graphic and keeps its
        // RectTransform stretched to ours so local coords coincide.
        private void EnsureGraphic()
        {
            if (m_Graphic == null)
            {
                var existing = transform.Find(ChildName);
                if (existing != null)
                    m_Graphic = existing.GetComponent<VMGVectorTextGraphic>();
            }
            if (m_Graphic == null)
            {
                var go = new GameObject(ChildName);
                go.hideFlags = HideFlags.DontSave;
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(transform, false);
                StretchFull(rt);
                m_Graphic = go.AddComponent<VMGVectorTextGraphic>();
            }
            m_Graphic.enabled = true;
            m_Graphic.Owner = this;
            StretchFull(m_Graphic.rectTransform);

            ForwardMaskComponents();

            // Push the user material onto the companion Graphic. UGUI lets a
            // Graphic.material override its defaultMaterial; assigning null
            // falls the Graphic back to its SDF defaultMaterial automatically.
            // Vector text is a plain mesh, so any UI material applies — this is
            // the "treat text like an image and skin it" hook. Guard so we only
            // dirty the material when it actually changes.
            if (!ReferenceEquals(m_Graphic.material, Material) &&
                !(Material == null && m_Graphic.material == m_Graphic.defaultMaterial))
            {
                m_Graphic.material = Material;
            }
        }

        // The vector glyph mesh lives on the __VMGVectorTextMesh CHILD, not on
        // this (TMP) object — TMP owns this object's CanvasRenderer and we run
        // it in DontRender mode. So a VMGMaskSource / VMGMaskClient that a user
        // drops on THIS object would attach to TMP's suppressed graphic and do
        // nothing. Mirror those mask markers onto the child mesh graphic so the
        // mask actually affects the visible vector text.
        //
        // Standard Mask / RectMask2D on an ANCESTOR needs no mirroring — the
        // child mesh graphic inherits clipping via MaskableGraphic. We still
        // nudge RecalculateMasking() below so a mask added AFTER the text was
        // built takes effect without a manual toggle.
        private void ForwardMaskComponents()
        {
            if (m_Graphic == null) return;
            var meshGO = m_Graphic.gameObject;

            // Source.
            var parentSrc = GetComponent<VMG.UI.VMGMaskSource>();
            var childSrc = meshGO.GetComponent<VMG.UI.VMGMaskSource>();
            if (parentSrc != null && parentSrc.enabled)
            {
                if (childSrc == null) childSrc = meshGO.AddComponent<VMG.UI.VMGMaskSource>();
                childSrc.enabled = true;
                // SetShowSource only dirties when the value actually changes, so
                // this is cheap to call every EnsureGraphic.
                childSrc.SetShowSource(parentSrc.ShowSource);
            }
            else if (childSrc != null)
            {
                childSrc.enabled = false;
            }

            // Client.
            var parentCli = GetComponent<VMG.UI.VMGMaskClient>();
            var childCli = meshGO.GetComponent<VMG.UI.VMGMaskClient>();
            if (parentCli != null && parentCli.enabled)
            {
                if (childCli == null) childCli = meshGO.AddComponent<VMG.UI.VMGMaskClient>();
                childCli.enabled = true;
            }
            else if (childCli != null)
            {
                childCli.enabled = false;
            }

            // Self-heal standard Mask / RectMask2D added after the text built.
            m_Graphic.RecalculateMasking();
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        private void Update()
        {
            SuppressTmpRendering();
            EnsureGraphic();

            // Two-level dirty gate (mirrors VectorImageGraphic):
            //  • ShapeDirty = the glyph CONTOURS changed (text/font/size/
            //    quality) → re-harvest TMP layout + reparse outlines.
            //  • StyleDirty = only fill/stroke/wiggle/tint changed → contours
            //    stand, just re-emit the mesh.
            // Wiggle keeps the mesh dirty every frame while enabled (it's
            // time-driven). The frame Wiggle turns OFF is StyleDirty (the
            // Wiggle struct changed), so the mesh rebuilds once WITHOUT the
            // shake and settles flat — fixing the "frozen wiggle" bug.
            bool shapeDirty = IsShapeDirty();
            if (shapeDirty) RebuildShape();
            if (shapeDirty || IsStyleDirty() || Wiggle.Enabled)
            {
                PushToGraphic();
                CaptureSnapshot();
            }
        }

        private void PushToGraphic()
        {
            if (m_Graphic != null) m_Graphic.SetVerticesDirty();
        }

        // ---- dirty gate ----
        // Shape inputs (contour-affecting).
        private string m_PrevText;
        private float m_PrevFontSize;
        private TMP_FontAsset m_PrevFont;
        private int m_PrevQuality;
        private VMGTextWarp m_PrevWarp;
        // Layout inputs: TMP wraps text against the RectTransform width and
        // margin, so a resize/margin change can alter line breaks (hence glyph
        // placement) WITHOUT the text string changing. Track both so wrap
        // changes re-harvest the layout.
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
            EnsureGraphic();
            RebuildShape();
            PushToGraphic();
            CaptureSnapshot();
        }

        // ============================================================
        //  Mesh emission (called by the companion Graphic)
        // ============================================================

        private readonly ShapePipeline m_Pipeline = new ShapePipeline();
        private readonly MeshBuffer m_StrokeBuf = new MeshBuffer();
        private readonly GlyphFillEmitter m_Emitter = new GlyphFillEmitter();

        internal void PopulateMesh(VertexHelper vh, Color graphicColor)
        {
            vh.Clear();
            if (m_Shape == null || m_Shape.subShapes.Count == 0) return;

            m_Pipeline.mesh.Clear();
            m_StrokeBuf.Clear();

            Color tint = Tint * graphicColor;
            m_Emitter.Emit(
                m_Shape, m_GlyphOfSub,
                Fill, Stroke, Tint, graphicColor,
                Wiggle, WiggleTime(),
                Mathf.Max(2, curveQuality), Warp.Enabled,
                m_Pipeline.mesh, m_StrokeBuf);

            AppendBufferToVH(m_Pipeline.mesh, vh);
            AppendBufferToVH(m_StrokeBuf, vh);
        }

        private static float WiggleTime()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif
            return Time.time;
        }

        private static void AppendBufferToVH(MeshBuffer mb, VertexHelper vh)
        {
            int baseV = vh.currentVertCount;
            int vc = mb.vertices.Count;
            bool hasUv1 = mb.uv1s.Count == vc;
            for (int i = 0; i < vc; i++)
            {
                var ui = new UIVertex
                {
                    position = mb.vertices[i],
                    color = mb.colors[i],
                    uv0 = mb.uvs[i],
                    uv1 = hasUv1 ? mb.uv1s[i] : new Vector2(1f, 0f),
                    normal = new Vector3(0f, 0f, -1f),
                    tangent = new Vector4(1f, 0f, 0f, -1f),
                };
                vh.AddVert(ui);
            }
            int tc = mb.triangles.Count;
            for (int i = 0; i + 2 < tc; i += 3)
                vh.AddTriangle(baseV + mb.triangles[i], baseV + mb.triangles[i + 1], baseV + mb.triangles[i + 2]);
        }
    }
}
