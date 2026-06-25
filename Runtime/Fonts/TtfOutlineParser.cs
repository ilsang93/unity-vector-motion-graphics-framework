using System;
using System.Collections.Generic;
using UnityEngine;
using VMG.Core;

namespace VMG.Fonts
{
    /// Parses glyph outlines from raw TTF/OTF font bytes into VMG VectorNode
    /// contours. This is the SHAPE source for vector text: TMP gives us
    /// glyph PLACEMENT, this gives us glyph SHAPE.
    ///
    /// Outline sources, in priority order:
    ///  • TrueType `glyf`/`loca` (quadratic beziers, simple + composite) —
    ///    parsed here directly.
    ///  • OpenType-CFF `CFF ` (Type 2 cubic CharStrings, incl. CID-keyed) —
    ///    delegated to CffOutlineParser.
    /// cmap formats 4 & 12 cover unicode->glyph for both.
    ///
    /// HasOutlines is true when EITHER source is usable; HasGlyfOutlines /
    /// HasCffOutlines say which one. Construct once per font (parses the
    /// table directory + cmap eagerly), then call GetGlyph(unicode)
    /// repeatedly. Not thread-safe; reuses internal point buffers.
    public sealed class TtfOutlineParser
    {
        // ---- table offsets (0 = absent) ----
        private readonly byte[] m_Data;
        private int m_HeadOff, m_MaxpOff, m_CmapOff, m_LocaOff, m_GlyfOff, m_HheaOff, m_HmtxOff, m_CffOff;

        private int m_UnitsPerEm = 1000;
        private bool m_LongLoca;          // head.indexToLocFormat == 1
        private int m_NumGlyphs;
        private int m_NumHMetrics;        // hhea.numberOfHMetrics

        // cmap: unicode -> glyph index, lazily resolved through the selected subtable.
        private CmapSubtable m_Cmap;

        // CFF outline interpreter, built when the font carries a `CFF ` table
        // (OpenType-PostScript) instead of TrueType `glyf`.
        private CffOutlineParser m_Cff;

        /// True when the font carries TrueType `glyf` outlines we can parse.
        public bool HasGlyfOutlines { get; private set; }

        /// True when the font carries OpenType-CFF (`CFF `) outlines we can parse.
        public bool HasCffOutlines => m_Cff != null;

        /// True when either outline source is usable.
        public bool HasOutlines => HasGlyfOutlines || HasCffOutlines;

        public int UnitsPerEm => m_UnitsPerEm;
        public int NumGlyphs => m_NumGlyphs;

        public TtfOutlineParser(byte[] fontBytes)
        {
            if (fontBytes == null || fontBytes.Length < 12)
                throw new ArgumentException("Font bytes are null or too short.");
            m_Data = fontBytes;
            ParseTableDirectory();

            // head/maxp/hhea/cmap are shared by both outline kinds, so parse
            // them whenever ANY outline table is present.
            bool anyOutline = HasGlyfOutlines || m_CffOff != 0;
            if (anyOutline)
            {
                if (m_HeadOff != 0) ParseHead();
                if (m_MaxpOff != 0) ParseMaxp();
                ParseHhea();
                m_Cmap = ParseCmap();
            }

            // Prefer glyf when both exist (rare). Otherwise try CFF; if it
            // fails to parse we simply stay outline-less.
            if (!HasGlyfOutlines && m_CffOff != 0)
            {
                try
                {
                    var cff = new CffOutlineParser(m_Data, m_CffOff, m_UnitsPerEm);
                    if (cff.IsUsable) m_Cff = cff;
                }
                catch { m_Cff = null; }
            }
        }

        // ============================================================
        //  Public API
        // ============================================================

        /// Returns the glyph index for a unicode code point, or 0 (.notdef)
        /// if unmapped.
        public int GetGlyphIndex(int unicode)
        {
            return m_Cmap != null ? m_Cmap.Map(unicode) : 0;
        }

        /// Parses the outline for a unicode code point. Returns a
        /// GlyphContour (possibly empty for whitespace) or null if the font
        /// has no usable outlines.
        public GlyphContour GetGlyph(int unicode)
        {
            if (!HasOutlines) return null;
            int gid = GetGlyphIndex(unicode);
            return GetGlyphByIndex(gid);
        }

        public GlyphContour GetGlyphByIndex(int glyphIndex)
        {
            if (!HasOutlines) return null;
            var result = new GlyphContour { unitsPerEm = m_UnitsPerEm };
            (result.advanceWidth, result.leftSideBearing) = GetHMetrics(glyphIndex);
            if (HasGlyfOutlines) BuildGlyf(glyphIndex, result.contours, 0);
            else m_Cff.BuildGlyph(glyphIndex, result.contours);
            return result;
        }

        // ============================================================
        //  Table directory
        // ============================================================

        private void ParseTableDirectory()
        {
            uint sfnt = ReadU32(0);
            int tableStart = 12;
            // 0x00010000 = TrueType, 'true'/'ttcf' variants, 'OTTO' = CFF.
            if (sfnt == 0x74746366) // 'ttcf' — TrueType Collection
            {
                // Use first font in the collection.
                int firstOff = (int)ReadU32(12);
                sfnt = ReadU32(firstOff);
                tableStart = firstOff + 12;
                // numTables sits at firstOff+4
                int nt = ReadU16(firstOff + 4);
                ReadTables(tableStart, nt);
            }
            else
            {
                int numTables = ReadU16(4);
                ReadTables(tableStart, numTables);
            }

            // glyf+loca present => TrueType outlines. A `CFF ` table (often
            // with the 'OTTO' sfnt tag) => OpenType-PostScript, handled by
            // CffOutlineParser below.
            HasGlyfOutlines = m_GlyfOff != 0 && m_LocaOff != 0 && m_HeadOff != 0 && m_MaxpOff != 0;
        }

        private void ReadTables(int dirOffset, int numTables)
        {
            for (int i = 0; i < numTables; i++)
            {
                int rec = dirOffset + i * 16;
                uint tag = ReadU32(rec);
                int off = (int)ReadU32(rec + 8);
                switch (tag)
                {
                    case 0x68656164: m_HeadOff = off; break; // 'head'
                    case 0x6D617870: m_MaxpOff = off; break; // 'maxp'
                    case 0x636D6170: m_CmapOff = off; break; // 'cmap'
                    case 0x6C6F6361: m_LocaOff = off; break; // 'loca'
                    case 0x676C7966: m_GlyfOff = off; break; // 'glyf'
                    case 0x68686561: m_HheaOff = off; break; // 'hhea'
                    case 0x686D7478: m_HmtxOff = off; break; // 'hmtx'
                    case 0x43464620: m_CffOff = off; break;  // 'CFF '
                }
            }
        }

        private void ParseHead()
        {
            // head: unitsPerEm @18 (u16), indexToLocFormat @50 (i16)
            m_UnitsPerEm = ReadU16(m_HeadOff + 18);
            if (m_UnitsPerEm <= 0) m_UnitsPerEm = 1000;
            m_LongLoca = ReadI16(m_HeadOff + 50) == 1;
        }

        private void ParseMaxp()
        {
            // maxp: numGlyphs @4 (u16)
            m_NumGlyphs = ReadU16(m_MaxpOff + 4);
        }

        private void ParseHhea()
        {
            if (m_HheaOff == 0) { m_NumHMetrics = 0; return; }
            // hhea: numberOfHMetrics @34 (u16)
            m_NumHMetrics = ReadU16(m_HheaOff + 34);
        }

        // (advanceWidth, leftSideBearing) in em units.
        private (float, float) GetHMetrics(int glyphIndex)
        {
            if (m_HmtxOff == 0 || m_NumHMetrics == 0) return (0f, 0f);
            if (glyphIndex < m_NumHMetrics)
            {
                int rec = m_HmtxOff + glyphIndex * 4;
                return (ReadU16(rec), ReadI16(rec + 2));
            }
            // Glyphs beyond numHMetrics share the last advance; lsb in the
            // trailing leftSideBearing[] array.
            int lastAdv = ReadU16(m_HmtxOff + (m_NumHMetrics - 1) * 4);
            int lsbOff = m_HmtxOff + m_NumHMetrics * 4 + (glyphIndex - m_NumHMetrics) * 2;
            return (lastAdv, ReadI16(lsbOff));
        }

        // ============================================================
        //  loca / glyf
        // ============================================================

        // Returns (start, end) byte offsets of a glyph in the glyf table.
        // start==end means an empty glyph (no outline).
        private (int, int) LocaRange(int glyphIndex)
        {
            if (glyphIndex < 0 || glyphIndex >= m_NumGlyphs) return (0, 0);
            if (m_LongLoca)
            {
                int s = (int)ReadU32(m_LocaOff + glyphIndex * 4);
                int e = (int)ReadU32(m_LocaOff + (glyphIndex + 1) * 4);
                return (m_GlyfOff + s, m_GlyfOff + e);
            }
            else
            {
                int s = ReadU16(m_LocaOff + glyphIndex * 2) * 2;
                int e = ReadU16(m_LocaOff + (glyphIndex + 1) * 2) * 2;
                return (m_GlyfOff + s, m_GlyfOff + e);
            }
        }

        // Recursively builds contours for a glyph into `outContours`,
        // applying a 2x3 affine (for composite components). depth guards
        // against malformed cyclic composites.
        private void BuildGlyf(int glyphIndex, List<List<VectorNode>> outContours, int depth)
        {
            if (depth > 8) return;
            var (start, end) = LocaRange(glyphIndex);
            if (end <= start) return; // empty glyph

            int p = start;
            short numberOfContours = (short)ReadI16(p); p += 2;
            // skip bounding box xMin,yMin,xMax,yMax (4 * i16)
            p += 8;

            if (numberOfContours >= 0)
                ParseSimpleGlyph(p, numberOfContours, outContours);
            else
                ParseCompositeGlyph(p, outContours, depth);
        }

        // ---- simple glyph ----

        private static readonly List<int> s_EndPts = new List<int>(64);
        private static readonly List<byte> s_Flags = new List<byte>(256);
        private static readonly List<int> s_Xs = new List<int>(256);
        private static readonly List<int> s_Ys = new List<int>(256);

        private const byte ON_CURVE = 0x01;
        private const byte X_SHORT = 0x02;
        private const byte Y_SHORT = 0x04;
        private const byte REPEAT_FLAG = 0x08;
        private const byte X_SAME_OR_POS = 0x10; // x is same (if not short) / positive (if short)
        private const byte Y_SAME_OR_POS = 0x20;

        private void ParseSimpleGlyph(int p, int numContours, List<List<VectorNode>> outContours)
        {
            s_EndPts.Clear();
            int numPoints = 0;
            for (int i = 0; i < numContours; i++)
            {
                int ep = ReadU16(p); p += 2;
                s_EndPts.Add(ep);
                numPoints = ep + 1;
            }

            // instructionLength + bytecode (skip)
            int instrLen = ReadU16(p); p += 2;
            p += instrLen;

            // flags (run-length encoded via REPEAT_FLAG)
            s_Flags.Clear();
            for (int i = 0; i < numPoints; )
            {
                byte flag = m_Data[p++];
                s_Flags.Add(flag);
                i++;
                if ((flag & REPEAT_FLAG) != 0)
                {
                    int repeat = m_Data[p++];
                    for (int r = 0; r < repeat && i < numPoints; r++) { s_Flags.Add(flag); i++; }
                }
            }

            // x coordinates (delta-encoded)
            s_Xs.Clear();
            int x = 0;
            for (int i = 0; i < numPoints; i++)
            {
                byte flag = s_Flags[i];
                if ((flag & X_SHORT) != 0)
                {
                    int dx = m_Data[p++];
                    x += ((flag & X_SAME_OR_POS) != 0) ? dx : -dx;
                }
                else if ((flag & X_SAME_OR_POS) == 0)
                {
                    x += ReadI16(p); p += 2;
                }
                // else: x same as previous (delta 0)
                s_Xs.Add(x);
            }

            // y coordinates (delta-encoded)
            s_Ys.Clear();
            int y = 0;
            for (int i = 0; i < numPoints; i++)
            {
                byte flag = s_Flags[i];
                if ((flag & Y_SHORT) != 0)
                {
                    int dy = m_Data[p++];
                    y += ((flag & Y_SAME_OR_POS) != 0) ? dy : -dy;
                }
                else if ((flag & Y_SAME_OR_POS) == 0)
                {
                    y += ReadI16(p); p += 2;
                }
                s_Ys.Add(y);
            }

            // Split into per-contour point runs and build bezier nodes.
            int startIdx = 0;
            for (int c = 0; c < numContours; c++)
            {
                int endIdx = s_EndPts[c];
                EmitContour(startIdx, endIdx, outContours);
                startIdx = endIdx + 1;
            }
        }

        // Converts one TrueType contour (range of points, on/off-curve) into
        // a closed list of VMG cubic-bezier VectorNodes.
        //
        // TrueType uses QUADRATIC beziers. Off-curve points are control
        // points; consecutive off-curve points imply an implied on-curve
        // midpoint between them. We first reconstruct an explicit sequence
        // of (onCurve, [offControl]) segments, then elevate each quadratic
        // to cubic using the same 2/3 rule the SVG parser uses.
        private void EmitContour(int start, int end, List<List<VectorNode>> outContours)
        {
            int n = end - start + 1;
            if (n < 2) return;

            // Build explicit point list with on/off flags, inserting implied
            // on-curve midpoints between consecutive off-curve points.
            var pts = new List<(Vector2 p, bool on)>(n + 4);
            for (int i = 0; i < n; i++)
            {
                int idx = start + i;
                bool on = (s_Flags[idx] & ON_CURVE) != 0;
                pts.Add((new Vector2(s_Xs[idx], s_Ys[idx]), on));
            }

            // Insert implied midpoints between two consecutive off-curve pts.
            var expanded = new List<(Vector2 p, bool on)>(pts.Count * 2);
            for (int i = 0; i < pts.Count; i++)
            {
                var cur = pts[i];
                expanded.Add(cur);
                var next = pts[(i + 1) % pts.Count];
                if (!cur.on && !next.on)
                {
                    expanded.Add(((cur.p + next.p) * 0.5f, true));
                }
            }

            // Rotate so the list starts on an on-curve point. If the contour
            // has NO on-curve point (rare, all-off quadratic loop), synthesize
            // a start at the midpoint of the first two control points.
            int firstOn = -1;
            for (int i = 0; i < expanded.Count; i++)
                if (expanded[i].on) { firstOn = i; break; }

            if (firstOn < 0)
            {
                var mid = (expanded[0].p + expanded[1].p) * 0.5f;
                expanded.Insert(0, (mid, true));
                firstOn = 0;
            }

            // Reorder so traversal begins at an on-curve anchor. After this
            // rotation + midpoint insertion, no two off-curve points are
            // adjacent and seq[0] is on-curve.
            int count = expanded.Count;
            var seq = new List<(Vector2 p, bool on)>(count);
            for (int i = 0; i < count; i++)
                seq.Add(expanded[(firstOn + i) % count]);

            // Walk the closed sequence anchoring on on-curve points. The
            // endpoint of the final segment wraps back to seq[0]. We append
            // line/quadratic segments; the trailing node coincident with the
            // start is then merged in CloseContour.
            var nodes = new List<VectorNode>(count + 1);
            Vector2 startPt = seq[0].p;
            nodes.Add(VectorNode.Corner(startPt));

            int k = 0; // index of the current on-curve anchor in seq
            while (k < count)
            {
                Vector2 anchor = seq[k].p;
                var nxt = seq[(k + 1) % count];
                if (nxt.on)
                {
                    // straight line anchor -> nxt
                    AppendLine(nodes, nxt.p);
                    k += 1;
                }
                else
                {
                    // quadratic: control = nxt (off-curve), endpoint = the
                    // following point (guaranteed on-curve).
                    Vector2 end2 = seq[(k + 2) % count].p;
                    AppendQuadratic(nodes, anchor, nxt.p, end2);
                    k += 2;
                }
            }

            // The walk's final endpoint wraps to startPt; merge the duplicate
            // trailing node onto node 0, carrying its inTangent.
            CloseContour(nodes, startPt);

            if (nodes.Count >= 2)
                outContours.Add(nodes);
        }

        // Appends a straight segment to the last node (corner) -> new corner.
        private static void AppendLine(List<VectorNode> nodes, Vector2 to)
        {
            nodes.Add(VectorNode.Corner(to));
        }

        // Elevates a quadratic (p0, control qc, p1) to cubic and appends.
        // p0 is the current last node's position.
        private static void AppendQuadratic(List<VectorNode> nodes, Vector2 p0, Vector2 qc, Vector2 p1)
        {
            Vector2 c1 = p0 + (2f / 3f) * (qc - p0);
            Vector2 c2 = p1 + (2f / 3f) * (qc - p1);

            int last = nodes.Count - 1;
            var a = nodes[last];
            a.outTangent = c1 - a.position;
            a.type = NodeType.Bezier;
            nodes[last] = a;

            var b = VectorNode.Corner(p1);
            b.inTangent = c2 - p1;
            b.type = NodeType.Bezier;
            nodes.Add(b);
        }

        // Merges the trailing node (which should coincide with startPt) back
        // onto the first node so the contour closes cleanly without a
        // zero-length duplicate edge.
        private static void CloseContour(List<VectorNode> nodes, Vector2 startPt)
        {
            if (nodes.Count < 2) return;
            int lastIdx = nodes.Count - 1;
            var last = nodes[lastIdx];
            if ((last.position - startPt).sqrMagnitude < 1e-6f)
            {
                // Transfer the closing curve's inTangent onto node 0.
                var first = nodes[0];
                first.inTangent = last.inTangent;
                if (last.type == NodeType.Bezier && first.type != NodeType.Bezier)
                    first.type = NodeType.Bezier;
                else if (last.type == NodeType.Bezier)
                    first.type = NodeType.Bezier;
                nodes[0] = first;
                nodes.RemoveAt(lastIdx);
            }
        }

        // ---- composite glyph ----

        private const ushort ARG_1_AND_2_ARE_WORDS = 0x0001;
        private const ushort ARGS_ARE_XY_VALUES = 0x0002;
        private const ushort WE_HAVE_A_SCALE = 0x0008;
        private const ushort MORE_COMPONENTS = 0x0020;
        private const ushort WE_HAVE_AN_X_AND_Y_SCALE = 0x0040;
        private const ushort WE_HAVE_A_TWO_BY_TWO = 0x0080;

        private void ParseCompositeGlyph(int p, List<List<VectorNode>> outContours, int depth)
        {
            while (true)
            {
                ushort flags = (ushort)ReadU16(p); p += 2;
                int componentGid = ReadU16(p); p += 2;

                int arg1, arg2;
                if ((flags & ARG_1_AND_2_ARE_WORDS) != 0)
                {
                    arg1 = ReadI16(p); p += 2;
                    arg2 = ReadI16(p); p += 2;
                }
                else
                {
                    arg1 = (sbyte)m_Data[p]; p += 1;
                    arg2 = (sbyte)m_Data[p]; p += 1;
                }

                float a = 1f, b = 0f, c = 0f, d = 1f;
                if ((flags & WE_HAVE_A_SCALE) != 0)
                {
                    a = d = ReadF2Dot14(p); p += 2;
                }
                else if ((flags & WE_HAVE_AN_X_AND_Y_SCALE) != 0)
                {
                    a = ReadF2Dot14(p); p += 2;
                    d = ReadF2Dot14(p); p += 2;
                }
                else if ((flags & WE_HAVE_A_TWO_BY_TWO) != 0)
                {
                    a = ReadF2Dot14(p); p += 2;
                    b = ReadF2Dot14(p); p += 2;
                    c = ReadF2Dot14(p); p += 2;
                    d = ReadF2Dot14(p); p += 2;
                }

                // dx,dy only meaningful when ARGS_ARE_XY_VALUES (point-matching
                // composites are rare and unsupported — treated as 0 offset).
                float dx = 0f, dy = 0f;
                if ((flags & ARGS_ARE_XY_VALUES) != 0)
                {
                    dx = arg1; dy = arg2;
                }

                // Parse the component into a temp list, then transform.
                var sub = new List<List<VectorNode>>();
                BuildGlyf(componentGid, sub, depth + 1);
                for (int ci = 0; ci < sub.Count; ci++)
                {
                    var contour = sub[ci];
                    for (int ni = 0; ni < contour.Count; ni++)
                    {
                        var node = contour[ni];
                        node.position = Affine(node.position, a, b, c, d, dx, dy);
                        // tangents are relative vectors — transform without translation
                        node.inTangent = AffineVec(node.inTangent, a, b, c, d);
                        node.outTangent = AffineVec(node.outTangent, a, b, c, d);
                        contour[ni] = node;
                    }
                    outContours.Add(contour);
                }

                if ((flags & MORE_COMPONENTS) == 0) break;
            }
        }

        private static Vector2 Affine(Vector2 v, float a, float b, float c, float d, float dx, float dy)
        {
            return new Vector2(a * v.x + c * v.y + dx, b * v.x + d * v.y + dy);
        }

        private static Vector2 AffineVec(Vector2 v, float a, float b, float c, float d)
        {
            return new Vector2(a * v.x + c * v.y, b * v.x + d * v.y);
        }

        // ============================================================
        //  cmap
        // ============================================================

        private abstract class CmapSubtable { public abstract int Map(int unicode); }

        // format 4: segment mapping (BMP)
        private sealed class CmapFormat4 : CmapSubtable
        {
            public int segCount;
            public int[] endCode, startCode, idDelta, idRangeOffset;
            public int idRangeOffsetBase;   // absolute byte offset of idRangeOffset[0]
            public byte[] data;
            public override int Map(int unicode)
            {
                if (unicode > 0xFFFF) return 0;
                for (int i = 0; i < segCount; i++)
                {
                    if (unicode <= endCode[i])
                    {
                        if (unicode < startCode[i]) return 0;
                        int ro = idRangeOffset[i];
                        if (ro == 0)
                            return (unicode + idDelta[i]) & 0xFFFF;
                        // glyphIdArray indexing per spec.
                        int glyphIndexOffset = idRangeOffsetBase + i * 2 + ro
                                               + (unicode - startCode[i]) * 2;
                        int g = (data[glyphIndexOffset] << 8) | data[glyphIndexOffset + 1];
                        if (g == 0) return 0;
                        return (g + idDelta[i]) & 0xFFFF;
                    }
                }
                return 0;
            }
        }

        // format 12: segmented coverage (full unicode)
        private sealed class CmapFormat12 : CmapSubtable
        {
            public int nGroups;
            public int[] startChar, endChar, startGid;
            public override int Map(int unicode)
            {
                // groups are sorted; linear is fine for v1, binary later.
                for (int i = 0; i < nGroups; i++)
                {
                    if (unicode >= startChar[i] && unicode <= endChar[i])
                        return startGid[i] + (unicode - startChar[i]);
                }
                return 0;
            }
        }

        private CmapSubtable ParseCmap()
        {
            if (m_CmapOff == 0) return null;
            int numTables = ReadU16(m_CmapOff + 2);
            int bestOff = 0; int bestScore = -1;
            for (int i = 0; i < numTables; i++)
            {
                int rec = m_CmapOff + 4 + i * 8;
                int platformId = ReadU16(rec);
                int encodingId = ReadU16(rec + 2);
                int subOff = (int)ReadU32(rec + 4);
                // Prefer: (3,10) full-unicode, then (3,1) BMP, then (0,*) unicode.
                int score = -1;
                if (platformId == 3 && encodingId == 10) score = 4;
                else if (platformId == 0 && encodingId >= 4) score = 3;
                else if (platformId == 3 && encodingId == 1) score = 2;
                else if (platformId == 0) score = 1;
                if (score > bestScore) { bestScore = score; bestOff = m_CmapOff + subOff; }
            }
            if (bestOff == 0) return null;

            int format = ReadU16(bestOff);
            if (format == 4) return ParseCmap4(bestOff);
            if (format == 12) return ParseCmap12(bestOff);
            return null;
        }

        private CmapSubtable ParseCmap4(int off)
        {
            int segX2 = ReadU16(off + 6);
            int segCount = segX2 / 2;
            var t = new CmapFormat4
            {
                segCount = segCount,
                endCode = new int[segCount],
                startCode = new int[segCount],
                idDelta = new int[segCount],
                idRangeOffset = new int[segCount],
                data = m_Data,
            };
            int p = off + 14;
            for (int i = 0; i < segCount; i++) { t.endCode[i] = ReadU16(p); p += 2; }
            p += 2; // reservedPad
            for (int i = 0; i < segCount; i++) { t.startCode[i] = ReadU16(p); p += 2; }
            for (int i = 0; i < segCount; i++) { t.idDelta[i] = ReadI16(p); p += 2; }
            t.idRangeOffsetBase = p;
            for (int i = 0; i < segCount; i++) { t.idRangeOffset[i] = ReadU16(p); p += 2; }
            return t;
        }

        private CmapSubtable ParseCmap12(int off)
        {
            int nGroups = (int)ReadU32(off + 12);
            var t = new CmapFormat12
            {
                nGroups = nGroups,
                startChar = new int[nGroups],
                endChar = new int[nGroups],
                startGid = new int[nGroups],
            };
            int p = off + 16;
            for (int i = 0; i < nGroups; i++)
            {
                t.startChar[i] = (int)ReadU32(p);
                t.endChar[i] = (int)ReadU32(p + 4);
                t.startGid[i] = (int)ReadU32(p + 8);
                p += 12;
            }
            return t;
        }

        // ============================================================
        //  big-endian readers
        // ============================================================

        private int ReadU16(int o) => (m_Data[o] << 8) | m_Data[o + 1];
        private int ReadI16(int o) { int v = ReadU16(o); return v >= 0x8000 ? v - 0x10000 : v; }
        private uint ReadU32(int o) =>
            ((uint)m_Data[o] << 24) | ((uint)m_Data[o + 1] << 16) | ((uint)m_Data[o + 2] << 8) | m_Data[o + 3];
        private float ReadF2Dot14(int o) => ReadI16(o) / 16384f;
    }
}
