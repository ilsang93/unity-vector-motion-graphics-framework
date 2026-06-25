using System.Collections.Generic;
using UnityEngine;
using VMG.Core;
using VMG.Svg;

namespace VMG.Text
{
    /// Turns a placed glyph-contour shape (one VMGSubShape per contour, tagged
    /// by source glyph via a parallel index list) into fill + stroke mesh
    /// buffers. Shared by the UGUI and World vector-text renderers.
    ///
    /// The single job that can't be done per-contour is FILL: a glyph's
    /// counters ('o','e','A') are separate inner contours that must be
    /// tessellated TOGETHER with the outer contour so the even-odd rule
    /// carves the hole. So contours are grouped by glyph index and filled via
    /// FillMeshBuilder.BuildMulti. Stroke is per-contour (each loop is its own
    /// outline) and unaffected.
    internal sealed class GlyphFillEmitter
    {
        private readonly VectorPath m_Path = new VectorPath();
        private readonly List<VectorPath> m_GlyphContours = new List<VectorPath>(8);
        private readonly List<VectorPath> m_Pool = new List<VectorPath>(8);

        public void Emit(
            VMGShapeAsset shape, List<int> glyphOfSub,
            in FillStyle fillStyle, in StrokeStyle strokeStyle,
            Color tintColor, Color graphicColor,
            in WiggleModifier wiggle, float wiggleTime,
            int bezSamples, bool warpActive,
            MeshBuffer fillBuf, MeshBuffer strokeBuf)
        {
            int count = shape.subShapes.Count;
            if (count == 0) return;
            Color tint = tintColor * graphicColor;

            // After a warp, every glyph contour is a DENSE polyline approxim-
            // ating a curve — its "corners" are just curve samples, not real
            // sharp corners. Miter joins there spike outward on the convex
            // side (1/sin blow-up under the miter limit), so force Bevel: it's
            // visually identical on a smooth curve and produces no spikes.
            LineJoin joinOverride = warpActive ? LineJoin.Bevel : strokeStyle.join;

            ReturnAll();
            int curGlyph = int.MinValue;

            for (int s = 0; s < count; s++)
            {
                var sub = shape.subShapes[s];
                int g = (s < glyphOfSub.Count) ? glyphOfSub[s] : s;

                // Flush the previous glyph's accumulated contours when the
                // glyph index changes (contours of one glyph are contiguous).
                if (g != curGlyph && m_GlyphContours.Count > 0)
                {
                    FlushFill(fillStyle, tint, fillBuf);
                    ReturnAll();
                }
                curGlyph = g;

                if (sub == null || sub.nodes.Count < 2) continue;

                var tess = shape.GetTessellation(s, bezSamples);
                if (tess == null) continue;

                var path = Rent();
                path.CopyFrom(tess);
                if (wiggle.Enabled) wiggle.Apply(path, wiggleTime);

                // Stroke each contour independently.
                if (strokeStyle.enabled)
                {
                    var stroke = strokeStyle; stroke.color *= tint; stroke.join = joinOverride;
                    StrokeMeshBuilder.Build(path, stroke, strokeBuf);
                }

                // Accumulate for the glyph's combined even-odd fill.
                m_GlyphContours.Add(path);
            }

            // Flush the final glyph.
            if (m_GlyphContours.Count > 0)
            {
                FlushFill(fillStyle, tint, fillBuf);
                ReturnAll();
            }

            // Gradients: recolor the fill / stroke verts across the text's
            // union bounds (so both gradients share one coordinate frame),
            // multiplied by tint. AddVertex stashed each vertex position in its
            // UV, which ApplyGradient reads — so this runs on the raw buffers
            // before any UV normalization. Mirrors VectorImageGraphic.
            bool fillGrad = fillStyle.enabled && fillStyle.useGradient;
            bool strokeGrad = strokeStyle.enabled && strokeStyle.useGradient;
            if (fillGrad || strokeGrad)
            {
                // Both renderers pass SEPARATE fill / stroke buffers, so each
                // gradient recolors only its own verts. Bounds are the union so
                // fill and stroke gradients share one coordinate frame.
                Rect bounds = UnionBounds(fillBuf, strokeBuf);
                if (fillGrad) fillBuf.ApplyGradient(fillStyle.gradient, bounds, tint, 0);
                if (strokeGrad) strokeBuf.ApplyGradient(strokeStyle.gradient, bounds, tint, 0);
            }
        }

        private static Rect UnionBounds(MeshBuffer a, MeshBuffer b)
        {
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            Accum(a, ref minX, ref minY, ref maxX, ref maxY);
            if (!ReferenceEquals(a, b)) Accum(b, ref minX, ref minY, ref maxX, ref maxY);
            if (float.IsPositiveInfinity(minX)) return new Rect(0f, 0f, 1f, 1f);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static void Accum(MeshBuffer mb, ref float minX, ref float minY, ref float maxX, ref float maxY)
        {
            var vs = mb.vertices;
            for (int i = 0; i < vs.Count; i++)
            {
                var v = vs[i];
                if (v.x < minX) minX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.x > maxX) maxX = v.x;
                if (v.y > maxY) maxY = v.y;
            }
        }

        private void FlushFill(in FillStyle fillStyle, Color tint, MeshBuffer fillBuf)
        {
            if (fillStyle.enabled)
            {
                var fill = fillStyle; fill.color *= tint;
                FillMeshBuilder.BuildMulti(m_GlyphContours, fill, fillBuf);
            }
        }

        private VectorPath Rent()
        {
            VectorPath p;
            if (m_Pool.Count > 0) { p = m_Pool[m_Pool.Count - 1]; m_Pool.RemoveAt(m_Pool.Count - 1); }
            else p = new VectorPath();
            return p;
        }

        private void ReturnAll()
        {
            for (int i = 0; i < m_GlyphContours.Count; i++) m_Pool.Add(m_GlyphContours[i]);
            m_GlyphContours.Clear();
        }
    }
}
