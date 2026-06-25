using System.Collections.Generic;
using UnityEngine;
using TMPro;
using VMG.Core;
using VMG.Fonts;
using VMG.Svg;

namespace VMG.Text
{
    /// Shared logic for the "Vector Image With TMP" feature: take a sibling
    /// TMP_Text component, use it purely as a LAYOUT engine (DontRender), and
    /// rebuild every visible glyph as a placed VMGShapeAsset whose contours
    /// come from parsing the source font's TrueType outlines.
    ///
    /// TMP gives WHERE (per-glyph origin / baseline / advance via
    /// characterInfo); TtfOutlineParser gives WHAT (glyph bezier contours).
    /// Subclasses (UGUI / World) own the actual mesh emission via the
    /// existing VMG render pipeline.
    ///
    /// Placement: a glyph outline point in font em units (ex, ey) maps to
    /// local space as (origin + ex*k, baseLine + ey*k). The em->local scale
    /// k is derived per LINE from the pen advance between adjacent glyphs
    /// (robust across TMP versions), falling back to the TMP face metrics
    /// when a line has a single glyph.
    [ExecuteAlways]
    public abstract class VMGVectorTextBase : MonoBehaviour
    {
        [Tooltip("Bezier samples per segment when tessellating glyph curves. Higher = smoother letters, more verts.")]
        [Range(2, 48)] public int curveQuality = 12;

        [Tooltip("PowerPoint-style text distortion (Arc, Circle, Trapezoid, Wave). Applied per glyph vertex after layout.")]
        public VMGTextWarp Warp = VMGTextWarp.Default;

        // The assembled, placed shape — one VMGSubShape per glyph contour,
        // node positions already in this object's local space (scale = 1).
        // Rebuilt by RebuildShape(); consumed by the subclass renderer.
        protected VMGShapeAsset m_Shape;

        // Parallel to m_Shape.subShapes: the source glyph (character) index
        // each subShape belongs to. Contours of the SAME glyph share an index
        // and must be filled TOGETHER so counters ('o','e','A') render as
        // holes via the even-odd rule. Subclass renderers group by this.
        protected readonly List<int> m_GlyphOfSub = new List<int>(64);

        // Pre-warp text bounds in local space (minX/minY/width/height), from
        // the last RebuildShape. The Grid warp editor places its control-point
        // handles relative to this box. Valid only after a successful rebuild.
        private VMGTextWarp.Bounds2D m_PreWarpBounds;
        private bool m_HasPreWarpBounds;
        public bool TryGetPreWarpBounds(out VMGTextWarp.Bounds2D b)
        {
            b = m_PreWarpBounds;
            return m_HasPreWarpBounds;
        }

        // Cached font outline provider, keyed by the resolved TMP_FontAsset.
        private TMP_FontAsset m_ProviderFontAsset;
        private GlyphOutlineProvider m_Provider;

        // Editor-resolved font bytes (set by the editor when the font asset
        // changes). At runtime this is whatever was last baked/assigned.
        [SerializeField, HideInInspector] private byte[] m_FontBytes;

        /// The TMP component this vector text mirrors. Subclasses resolve the
        /// concrete type (TextMeshPro vs TextMeshProUGUI) but operate on the
        /// shared TMP_Text base.
        public abstract TMP_Text Tmp { get; }

        /// True once a usable glyph provider + non-empty shape exist.
        public bool HasShape => m_Shape != null && m_Shape.subShapes.Count > 0;

        /// Assign raw TTF/OTF bytes for the font (used at runtime when no
        /// editor asset path is available, e.g. after baking). Clears the
        /// provider cache so the next rebuild re-parses.
        public void SetFontBytes(byte[] bytes)
        {
            m_FontBytes = bytes;
            m_ProviderFontAsset = null;
            m_Provider = null;
        }

        public byte[] FontBytes => m_FontBytes;

        /// Editor hook: rebuild the shape from current state and re-push the
        /// mesh RIGHT NOW, without waiting for the next Update tick (which the
        /// editor may not run while idle). Called by the inspector / scene
        /// handles after changing the warp grid so edits show immediately.
        public abstract void EditorRebuildAndPush();

        protected virtual void OnEnable()
        {
            SuppressTmpRendering();
            RebuildShape();
        }

        // TMP keeps computing layout/textInfo but stops pushing its own glyph
        // geometry to the renderer. Renderer-agnostic; works for both
        // TextMeshPro (MeshRenderer) and TextMeshProUGUI (CanvasRenderer).
        protected void SuppressTmpRendering()
        {
            var t = Tmp;
            if (t != null) t.renderMode = TextRenderFlags.DontRender;
        }

        // ============================================================
        //  Shape rebuild
        // ============================================================

        /// Re-harvests TMP layout and rebuilds m_Shape. Safe to call every
        /// frame; subclasses gate this behind a dirty check. Returns true if
        /// a shape was produced.
        public bool RebuildShape()
        {
            var t = Tmp;
            if (t == null) return false;

            t.renderMode = TextRenderFlags.DontRender;
            t.ForceMeshUpdate();

            var provider = ResolveProvider(t);
            if (provider == null || !provider.IsUsable)
            {
                // No usable outlines (bitmap-only font, missing font bytes, etc.).
                m_Shape = null;
                return false;
            }

            if (m_Shape == null) m_Shape = ScriptableObject.CreateInstance<VMGShapeAsset>();
            m_Shape.subShapes.Clear();
            m_GlyphOfSub.Clear();

            var info = t.textInfo;
            int charCount = info.characterCount;
            float invEm = 1f / Mathf.Max(1, provider.UnitsPerEm);

            // Track text bounds for viewBoxSize (informational; renderers
            // draw at scale 1 so this is just metadata).
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < charCount; i++)
            {
                var ci = info.characterInfo[i];
                if (!ci.isVisible) continue;

                float k = EmToLocalScale(info, i, provider, invEm);
                if (k <= 0f) continue;

                var glyph = provider.GetGlyph(ci.character);
                if (glyph == null || glyph.isEmpty) continue;

                // Pen origin (X) + baseline (Y) in local space.
                float ox = ci.origin;
                float oy = ci.baseLine;

                for (int c = 0; c < glyph.contours.Count; c++)
                {
                    var src = glyph.contours[c];
                    var sub = new VMGSubShape
                    {
                        id = $"glyph_{i}_{(int)ci.character}_{c}",
                        closed = true,
                    };
                    sub.nodes.Capacity = src.Count;
                    for (int n = 0; n < src.Count; n++)
                    {
                        var node = src[n];
                        // em -> local: scale by k, translate to pen origin/baseline.
                        node.position = new Vector2(ox + node.position.x * k, oy + node.position.y * k);
                        node.inTangent *= k;
                        node.outTangent *= k;
                        sub.nodes.Add(node);

                        if (node.position.x < minX) minX = node.position.x;
                        if (node.position.y < minY) minY = node.position.y;
                        if (node.position.x > maxX) maxX = node.position.x;
                        if (node.position.y > maxY) maxY = node.position.y;
                    }
                    m_Shape.subShapes.Add(sub);
                    m_GlyphOfSub.Add(i); // character index groups a glyph's contours
                }
            }

            // Pass 2: warp. Now that all glyphs are placed and the text bounds
            // are known, push every contour through the warp map. A warp is a
            // NON-LINEAR map of position, so a straight glyph edge (e.g. the
            // diagonal of M/N) becomes a chord cutting across the warped
            // surface — its endpoints land on the curve but the edge between
            // them stays straight, producing a visible spike at the corner.
            // Fix: flatten each contour to a DENSE polyline FIRST (beziers
            // tessellated + long straight edges evenly subdivided), THEN warp
            // every point. The result is all-corner nodes close enough that
            // the warped shape reads smooth, with no spikes. Tangents are
            // dropped (straight segments), so no Jacobian is needed.
            // Record the placed (pre-warp) bounds so the Grid editor can put
            // handles in the right place even when the warp moves geometry.
            if (maxX >= minX)
            {
                m_PreWarpBounds = new VMGTextWarp.Bounds2D(minX, minY, maxX, maxY);
                m_HasPreWarpBounds = true;
            }

            if (Warp.Enabled && maxX >= minX)
            {
                // Grid mode needs its control-point array materialized to a
                // uniform grid before mapping (identity until the user drags).
                // Warp is a struct field, so write the ensured copy back.
                if (Warp.mode == WarpMode.Grid)
                {
                    var wcopy = Warp;
                    if (wcopy.EnsureGrid()) Warp = wcopy;
                }

                var b = new VMGTextWarp.Bounds2D(minX, minY, maxX, maxY);
                int bez = Mathf.Max(2, curveQuality);
                // Max edge length before warp so curvature is well sampled:
                // a fraction of the smaller text dimension.
                float maxSeg = Mathf.Max(0.5f, Mathf.Min(maxX - minX, maxY - minY) * 0.04f);

                float wMinX = float.MaxValue, wMinY = float.MaxValue, wMaxX = float.MinValue, wMaxY = float.MinValue;
                for (int s = 0; s < m_Shape.subShapes.Count; s++)
                {
                    var sub = m_Shape.subShapes[s];
                    DenseFlatten(sub, bez, maxSeg, s_warpScratch);
                    sub.nodes.Clear();
                    for (int i = 0; i < s_warpScratch.Count; i++)
                    {
                        Vector2 wp = Warp.Map(s_warpScratch[i], b).pos;
                        sub.nodes.Add(VectorNode.Corner(wp)); // all-corner: no tangents
                        if (wp.x < wMinX) wMinX = wp.x;
                        if (wp.y < wMinY) wMinY = wp.y;
                        if (wp.x > wMaxX) wMaxX = wp.x;
                        if (wp.y > wMaxY) wMaxY = wp.y;
                    }
                }

                // Re-center the warped text on the original block center so
                // modes that move the centroid (esp. Circle, which orbits a
                // point below the baseline) stay where the text was laid out
                // instead of drifting off the RectTransform. Grid mode is
                // EXEMPT: dragging a control point is a deliberate position
                // change the user expects to stick, not drift to correct.
                if (Warp.mode == WarpMode.Grid)
                {
                    minX = wMinX; minY = wMinY; maxX = wMaxX; maxY = wMaxY;
                }
                else
                {
                    float ocx = (minX + maxX) * 0.5f, ocy = (minY + maxY) * 0.5f;
                    float ncx = (wMinX + wMaxX) * 0.5f, ncy = (wMinY + wMaxY) * 0.5f;
                    Vector2 shift = new Vector2(ocx - ncx, ocy - ncy);
                    if (shift != Vector2.zero)
                    {
                        for (int s = 0; s < m_Shape.subShapes.Count; s++)
                        {
                            var nodes = m_Shape.subShapes[s].nodes;
                            for (int i = 0; i < nodes.Count; i++)
                            {
                                var nd = nodes[i]; nd.position += shift; nodes[i] = nd;
                            }
                        }
                        wMinX += shift.x; wMaxX += shift.x; wMinY += shift.y; wMaxY += shift.y;
                    }
                    minX = wMinX; minY = wMinY; maxX = wMaxX; maxY = wMaxY;
                }
            }

            m_Shape.viewBoxSize = (maxX >= minX)
                ? new Vector2(maxX - minX, maxY - minY)
                : new Vector2(1f, 1f);
            m_Shape.ClearTessellationCache();
            return m_Shape.subShapes.Count > 0;
        }

        // Flatten a subShape into a dense corner polyline: beziers adaptively
        // tessellated, then every edge split so no edge exceeds maxSeg. This
        // gives the warp enough points to follow curvature without spikes.
        private static readonly VectorPath s_flattenPath = new VectorPath();
        private static readonly List<Vector2> s_warpScratch = new List<Vector2>(256);
        private static void DenseFlatten(VMGSubShape sub, int bezSamples, float maxSeg, List<Vector2> outPts)
        {
            s_flattenPath.nodes.Clear();
            BezierTessellator.Tessellate(sub.nodes, sub.closed, bezSamples, s_flattenPath);
            var src = s_flattenPath.nodes;
            outPts.Clear();
            int n = src.Count;
            if (n == 0) return;
            float maxSegSqr = maxSeg * maxSeg;
            int segCount = sub.closed ? n : n - 1;
            for (int i = 0; i < segCount; i++)
            {
                Vector2 a = src[i].position;
                Vector2 c = src[(i + 1) % n].position;
                outPts.Add(a);
                float d2 = (c - a).sqrMagnitude;
                if (d2 > maxSegSqr)
                {
                    int steps = Mathf.CeilToInt(Mathf.Sqrt(d2) / maxSeg);
                    for (int k = 1; k < steps; k++)
                        outPts.Add(Vector2.LerpUnclamped(a, c, (float)k / steps));
                }
            }
            if (!sub.closed) outPts.Add(src[n - 1].position);
        }

        /// Snapshot a warp for the dirty gate. Grid control points are now flat
        /// value fields (not an array), so a plain struct copy is a full deep
        /// copy — no special handling needed.
        protected static VMGTextWarp WarpSnapshot(in VMGTextWarp w) => w;

        /// Value-equality for the warp struct (used by subclass dirty gates).
        protected static bool WarpSame(in VMGTextWarp a, in VMGTextWarp b)
        {
            if (a.mode != b.mode) return false;
            if (!Mathf.Approximately(a.amount, b.amount)) return false;
            if (!Mathf.Approximately(a.secondary, b.secondary)) return false;
            if (a.mode == WarpMode.Grid)
            {
                if (a.gridCols != b.gridCols || a.gridRows != b.gridRows) return false;
                if (a.gridInitialized != b.gridInitialized) return false;
                for (int i = 0; i < VMGTextWarp.MaxPts; i++)
                    if ((a.GetPt(i) - b.GetPt(i)).sqrMagnitude > 1e-10f) return false;
            }
            return true;
        }

        // Per-glyph em->local scale. Prefer the pen-advance ratio between this
        // glyph and the next glyph on the SAME line (font size is constant per
        // line), since that maps directly from TMP's actual layout. Fall back
        // to the previous glyph, then to the TMP face metric.
        private float EmToLocalScale(TMP_TextInfo info, int i, GlyphOutlineProvider provider, float invEm)
        {
            var ci = info.characterInfo[i];

            // Forward neighbour on the same line.
            if (i + 1 < info.characterCount)
            {
                var next = info.characterInfo[i + 1];
                if (next.lineNumber == ci.lineNumber)
                {
                    float advLocal = next.origin - ci.origin;
                    float advEm = AdvanceEm(provider, ci.character);
                    if (advEm > 1e-3f && advLocal > 1e-4f) return advLocal / advEm;
                }
            }
            // Backward neighbour on the same line.
            if (i - 1 >= 0)
            {
                var prev = info.characterInfo[i - 1];
                if (prev.lineNumber == ci.lineNumber)
                {
                    float advLocal = ci.origin - prev.origin;
                    float advEm = AdvanceEm(provider, prev.character);
                    if (advEm > 1e-3f && advLocal > 1e-4f) return advLocal / advEm;
                }
            }
            // Fallback: TMP face scale maps face-units to local; convert to em.
            // characterInfo.scale already folds fontSize/pointSize*faceScale.
            // pointSize is the font asset's sampling size in em-normalized
            // face units, so scale * pointSize approximates local-per-em.
            var fa = ci.fontAsset != null ? ci.fontAsset : Tmp.font;
            float pointSize = fa != null ? fa.faceInfo.pointSize : Tmp.fontSize;
            return ci.scale * pointSize * invEm;
        }

        // Glyph advance in em units (native font units), from the parser.
        private float AdvanceEm(GlyphOutlineProvider provider, int unicode)
        {
            var g = provider.GetGlyph(unicode);
            return g != null ? g.advanceWidth : 0f;
        }

        private GlyphOutlineProvider ResolveProvider(TMP_Text t)
        {
            var fa = t.font;
            if (fa == null) return null;
            if (ReferenceEquals(fa, m_ProviderFontAsset) && m_Provider != null) return m_Provider;

            byte[] bytes = m_FontBytes;
#if UNITY_EDITOR
            // In the editor, pull bytes straight from the source font file so
            // authoring needs no manual bake. Cached into m_FontBytes so a
            // build keeps working off the serialized copy.
            if (bytes == null || bytes.Length == 0)
            {
                var path = ResolveSourceFontPathEditor(fa);
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    bytes = System.IO.File.ReadAllBytes(path);
                    m_FontBytes = bytes;
                }
            }
#endif
            if (bytes == null || bytes.Length == 0)
            {
                WarnNoFontBytesOnce(fa);
                return null;
            }

            m_Provider = GlyphOutlineProvider.FromBytes(bytes);
            m_ProviderFontAsset = fa;
            if (m_Provider == null || !m_Provider.IsUsable)
                WarnUnusableFontOnce(fa);
            return m_Provider;
        }

#if UNITY_EDITOR
        /// True if usable font bytes are already embedded on this component
        /// (so a build will render without any source-file dependency).
        public bool HasBakedFontBytes => m_FontBytes != null && m_FontBytes.Length > 0;

        /// Editor bake: resolve the current TMP font's source .ttf/.otf and
        /// embed its raw bytes on this component (serialized field), so the
        /// glyph outlines parse at runtime in a BUILD where the TMP font asset
        /// often has sourceFontFile == null. Returns true if usable bytes were
        /// embedded. Caller is responsible for marking the object dirty /
        /// recording undo (the inspector and build hook both do).
        ///
        /// This is the v1 "bake": font bytes travel with the component and the
        /// runtime re-parses them (the parse result is cached, so the cost is a
        /// single parse at first rebuild). It deliberately does NOT freeze the
        /// placed shape, so the text can still re-layout / wiggle / warp at
        /// runtime.
        public bool BakeFontBytes()
        {
            var t = Tmp;
            var fa = t != null ? t.font : null;
            if (fa == null) return false;

            var path = ResolveSourceFontPathEditor(fa);
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                return false;

            byte[] bytes;
            try { bytes = System.IO.File.ReadAllBytes(path); }
            catch { return false; }
            if (bytes == null || bytes.Length == 0) return false;

            // Validate the bytes actually yield parseable outlines (TrueType
            // `glyf` OR OpenType-CFF) before embedding, so baking a font we
            // can't render fails loudly instead of shipping dead weight.
            if (GlyphOutlineProvider.FromBytes(bytes) == null) return false;

            m_FontBytes = bytes;
            // Drop the cached provider so the next rebuild re-parses the
            // freshly embedded bytes (path-derived and embedded are identical
            // here, but stay consistent with SetFontBytes).
            m_ProviderFontAsset = null;
            m_Provider = null;
            return true;
        }

        // Resolve the .ttf/.otf asset path behind a TMP_FontAsset. The runtime
        // sourceFontFile is frequently null (e.g. TMP's bundled default
        // "LiberationSans SDF" serializes m_SourceFontFile = {fileID: 0}). The
        // editor-only reference and a serialized GUID survive, so fall back
        // through both before giving up.
        private static string ResolveSourceFontPathEditor(TMP_FontAsset fa)
        {
            // 1) Live runtime reference, when present.
            if (fa.sourceFontFile != null)
            {
                var p = UnityEditor.AssetDatabase.GetAssetPath(fa.sourceFontFile);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            // 2) Serialized editor-only ref + GUID (m_SourceFontFile_EditorRef,
            //    m_SourceFontFileGUID). Read via SerializedObject so this stays
            //    robust across TMP versions without a hard field dependency.
            var so = new UnityEditor.SerializedObject(fa);
            var refProp = so.FindProperty("m_SourceFontFile_EditorRef");
            if (refProp != null && refProp.objectReferenceValue != null)
            {
                var p = UnityEditor.AssetDatabase.GetAssetPath(refProp.objectReferenceValue);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            var guidProp = so.FindProperty("m_SourceFontFileGUID");
            if (guidProp != null && !string.IsNullOrEmpty(guidProp.stringValue))
            {
                var p = UnityEditor.AssetDatabase.GUIDToAssetPath(guidProp.stringValue);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            return null;
        }
#endif

        // One-shot warnings keyed by font asset so a steady non-resolving state
        // doesn't spam the console every Update.
        private TMP_FontAsset m_WarnedNoBytes;
        private TMP_FontAsset m_WarnedUnusable;

        private void WarnNoFontBytesOnce(TMP_FontAsset fa)
        {
            if (ReferenceEquals(m_WarnedNoBytes, fa)) return;
            m_WarnedNoBytes = fa;
            Debug.LogWarning(
                $"[VMGVectorText] Could not load source font bytes for '{(fa != null ? fa.name : "null")}'. " +
                "The TMP font asset has no resolvable source .ttf/.otf. In a build, call SetFontBytes(...) or bake. " +
                "In the editor, assign a font asset whose source file is present in the project.", this);
        }

        private void WarnUnusableFontOnce(TMP_FontAsset fa)
        {
            if (ReferenceEquals(m_WarnedUnusable, fa)) return;
            m_WarnedUnusable = fa;
            Debug.LogWarning(
                $"[VMGVectorText] Font '{(fa != null ? fa.name : "null")}' has no parseable vector outlines " +
                "(neither TrueType `glyf` nor OpenType-CFF `CFF `). Bitmap-only or unsupported font; " +
                "use a standard .ttf or .otf font.", this);
        }
    }
}
