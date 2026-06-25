using System;
using System.Collections.Generic;
using UnityEngine;
using VMG.Core;

namespace VMG.Fonts
{
    /// Parses OpenType-CFF (`CFF ` table, Type 2 CharStrings) glyph outlines
    /// into VMG VectorNode contours. This is the PostScript-outline counterpart
    /// to the TrueType `glyf` path in TtfOutlineParser; many .otf fonts (and
    /// most Korean/CJK fonts, which are typically CID-keyed) store their shapes
    /// here.
    ///
    /// Output convention matches the glyf path exactly: each contour is a
    /// closed list of VectorNodes where inTangent/outTangent are RELATIVE
    /// offsets from the node position and curved nodes are NodeType.Bezier.
    /// CFF CharStrings are already CUBIC, so curves map 1:1 (no quadratic
    /// elevation needed).
    ///
    /// Scope: enough of the Type 2 spec to render text — the full path
    /// operators (moveto/lineto/curveto families, incl. vv/hh/vh/hv curves and
    /// the implied-line tail of vh/hv), hstem/vstem (consumed for the width
    /// side effect + hintmask byte skipping), local + global subrs with the
    /// standard bias, callsubr/callgsubr/return/endchar, and CID-keyed fonts
    /// via FDArray/FDSelect (per-glyph Private DICT + local subrs). Seac-style
    /// endchar accent composition and the deprecated arithmetic/storage
    /// operators are not implemented (rare in modern text fonts).
    ///
    /// Reads big-endian over the SHARED font byte array; not thread-safe
    /// (reuses an interpreter stack). Construct once per font.
    public sealed class CffOutlineParser
    {
        private readonly byte[] m_Data;
        private readonly int m_CffStart;     // absolute offset of the CFF table
        private readonly int m_UnitsPerEm;

        // Glyph data: one offset range per glyph index into the CharStrings.
        private int[] m_CharStrings;         // absolute offsets, length count+1
        private int m_NumGlyphs;

        // Global + (non-CID) local subroutines: absolute offset arrays len+1.
        private int[] m_GSubrs;
        private int m_GSubrBias;
        private int[] m_LSubrs;              // null for CID fonts (per-FD instead)
        private int m_LSubrBias;

        // CID-keyed fonts: each glyph picks a Font DICT (FD) which carries its
        // own Private DICT + local subrs. m_FdSelect[gid] -> FD index.
        private bool m_IsCid;
        private byte[] m_FdSelect;           // length numGlyphs, FD index per glyph
        private int[][] m_FdLSubrs;          // per-FD local subr offset arrays
        private int[] m_FdLSubrBias;         // per-FD bias

        public bool IsUsable => m_CharStrings != null && m_NumGlyphs > 0;

        public CffOutlineParser(byte[] data, int cffTableOffset, int unitsPerEm)
        {
            m_Data = data;
            m_CffStart = cffTableOffset;
            m_UnitsPerEm = unitsPerEm > 0 ? unitsPerEm : 1000;
            Parse();
        }

        // ============================================================
        //  Top-level parse
        // ============================================================

        private void Parse()
        {
            // CFF header: major(1) minor(1) hdrSize(1) offSize(1).
            int hdrSize = m_Data[m_CffStart + 2];
            int p = m_CffStart + hdrSize;

            // Name INDEX (skip) -> Top DICT INDEX -> String INDEX (skip) ->
            // Global Subr INDEX.
            p = SkipIndex(p);                       // Name INDEX
            int[] topDictIdx = ReadIndex(p, out p); // Top DICT INDEX
            p = SkipIndex(p);                       // String INDEX
            m_GSubrs = ReadIndex(p, out p);         // Global Subr INDEX
            m_GSubrBias = SubrBias(m_GSubrs != null ? m_GSubrs.Length - 1 : 0);

            if (topDictIdx == null || topDictIdx.Length < 2) return;

            // First (only, for non-collection) Top DICT.
            var top = ParseDict(topDictIdx[0], topDictIdx[1]);

            // CharStrings INDEX (operator 17) — required.
            if (!top.TryGetValue(17, out var csOps) || csOps.Count < 1) return;
            int csOff = m_CffStart + (int)csOps[0];
            m_CharStrings = ReadIndex(csOff, out _);
            if (m_CharStrings == null) return;
            m_NumGlyphs = m_CharStrings.Length - 1;

            // CID-keyed? ROS operator (12 30) present. Then outlines come via
            // FDArray (12 36) + FDSelect (12 37).
            m_IsCid = top.ContainsKey(C2(30));
            if (m_IsCid && top.TryGetValue(C2(36), out var fdaOps) && top.TryGetValue(C2(37), out var fdsOps))
            {
                ParseFdArray(m_CffStart + (int)fdaOps[0]);
                ParseFdSelect(m_CffStart + (int)fdsOps[0]);
            }
            else
            {
                // Non-CID: a single Private DICT (operator 18 = [size, offset])
                // holds the local subrs + width defaults.
                if (top.TryGetValue(18, out var privOps) && privOps.Count >= 2)
                {
                    int privSize = (int)privOps[0];
                    int privOff = m_CffStart + (int)privOps[1];
                    var priv = ParseDict(privOff, privOff + privSize);
                    if (priv.TryGetValue(19, out var subrOps)) // Subrs, offset rel. to Private DICT
                    {
                        int lsOff = privOff + (int)subrOps[0];
                        m_LSubrs = ReadIndex(lsOff, out _);
                        m_LSubrBias = SubrBias(m_LSubrs != null ? m_LSubrs.Length - 1 : 0);
                    }
                }
            }
        }

        // FDArray: an INDEX of Font DICTs; each may carry its own Private DICT
        // + local subrs. Used by CID-keyed fonts.
        private void ParseFdArray(int off)
        {
            int[] fdIdx = ReadIndex(off, out _);
            if (fdIdx == null) return;
            int n = fdIdx.Length - 1;
            m_FdLSubrs = new int[n][];
            m_FdLSubrBias = new int[n];
            for (int i = 0; i < n; i++)
            {
                var fd = ParseDict(fdIdx[i], fdIdx[i + 1]);
                if (fd.TryGetValue(18, out var privOps) && privOps.Count >= 2)
                {
                    int privSize = (int)privOps[0];
                    int privOff = m_CffStart + (int)privOps[1];
                    var priv = ParseDict(privOff, privOff + privSize);
                    if (priv.TryGetValue(19, out var subrOps))
                    {
                        int lsOff = privOff + (int)subrOps[0];
                        m_FdLSubrs[i] = ReadIndex(lsOff, out _);
                        m_FdLSubrBias[i] = SubrBias(m_FdLSubrs[i] != null ? m_FdLSubrs[i].Length - 1 : 0);
                    }
                }
            }
        }

        // FDSelect: maps each glyph index to an FD index. Formats 0 and 3.
        private void ParseFdSelect(int off)
        {
            m_FdSelect = new byte[m_NumGlyphs];
            int format = m_Data[off];
            if (format == 0)
            {
                for (int g = 0; g < m_NumGlyphs; g++)
                    m_FdSelect[g] = m_Data[off + 1 + g];
            }
            else if (format == 3)
            {
                int nRanges = U16(off + 1);
                int rp = off + 3;
                for (int r = 0; r < nRanges; r++)
                {
                    int first = U16(rp);
                    int fd = m_Data[rp + 2];
                    int next = U16(rp + 3); // sentinel for last range = end glyph
                    for (int g = first; g < next && g < m_NumGlyphs; g++)
                        m_FdSelect[g] = (byte)fd;
                    rp += 3;
                }
            }
        }

        // ============================================================
        //  Glyph build
        // ============================================================

        /// Appends the closed contours of a glyph (by GID) to outContours,
        /// in font em units, using the VMG relative-tangent cubic convention.
        public void BuildGlyph(int glyphIndex, List<List<VectorNode>> outContours)
        {
            if (!IsUsable || glyphIndex < 0 || glyphIndex >= m_NumGlyphs) return;

            int[] lsubrs = m_LSubrs;
            int lbias = m_LSubrBias;
            if (m_IsCid && m_FdSelect != null && m_FdLSubrs != null)
            {
                int fd = m_FdSelect[glyphIndex];
                if (fd >= 0 && fd < m_FdLSubrs.Length)
                {
                    lsubrs = m_FdLSubrs[fd];
                    lbias = m_FdLSubrBias[fd];
                }
            }

            var interp = new Type2Interp(this, lsubrs, lbias, outContours);
            interp.Run(m_CharStrings[glyphIndex], m_CharStrings[glyphIndex + 1]);
        }

        // ============================================================
        //  Type 2 CharString interpreter
        // ============================================================

        private sealed class Type2Interp
        {
            private readonly CffOutlineParser m_O;
            private readonly byte[] m_Data;
            private readonly int[] m_LSubrs;
            private readonly int m_LBias;
            private readonly List<List<VectorNode>> m_Out;

            private readonly double[] m_Stack = new double[64];
            private int m_Sp;

            private int m_NStems;       // running stem count (for hintmask skip)
            private bool m_HaveWidth;   // first stack-clearing op may carry width
            private bool m_Open;        // a contour is currently being built

            private float m_X, m_Y;     // current pen position
            private List<VectorNode> m_Cur; // current contour

            public Type2Interp(CffOutlineParser o, int[] lsubrs, int lbias, List<List<VectorNode>> outContours)
            {
                m_O = o;
                m_Data = o.m_Data;
                m_LSubrs = lsubrs;
                m_LBias = lbias;
                m_Out = outContours;
            }

            public void Run(int start, int end)
            {
                Exec(start, end, 0);
                CloseCurrent();
            }

            // Returns true if endchar was hit (stop all execution).
            private bool Exec(int p, int end, int depth)
            {
                if (depth > 10) return true;
                while (p < end)
                {
                    int b0 = m_Data[p++];
                    if (b0 >= 32 || b0 == 28)
                    {
                        // operand
                        double v;
                        if (b0 == 28) { v = (short)((m_Data[p] << 8) | m_Data[p + 1]); p += 2; }
                        else if (b0 < 247) v = b0 - 139;
                        else if (b0 < 251) { v = (b0 - 247) * 256 + m_Data[p++] + 108; }
                        else if (b0 < 255) { v = -(b0 - 251) * 256 - m_Data[p++] - 108; }
                        else // 255: 16.16 fixed
                        {
                            int iv = (m_Data[p] << 24) | (m_Data[p + 1] << 16) | (m_Data[p + 2] << 8) | m_Data[p + 3];
                            p += 4;
                            v = iv / 65536.0;
                        }
                        Push(v);
                        continue;
                    }

                    // operator
                    switch (b0)
                    {
                        case 1:  // hstem
                        case 3:  // vstem
                        case 18: // hstemhm
                        case 23: // vstemhm
                            CountStems();
                            m_Sp = 0;
                            break;

                        case 19: // hintmask
                        case 20: // cntrmask
                            CountStems();
                            m_Sp = 0;
                            p += (m_NStems + 7) / 8; // skip the mask bytes
                            break;

                        case 21: // rmoveto
                            TakeWidth(2);
                            MoveTo(m_X + (float)m_Stack[0], m_Y + (float)m_Stack[1]);
                            m_Sp = 0;
                            break;
                        case 22: // hmoveto
                            TakeWidth(1);
                            MoveTo(m_X + (float)m_Stack[0], m_Y);
                            m_Sp = 0;
                            break;
                        case 4:  // vmoveto
                            TakeWidth(1);
                            MoveTo(m_X, m_Y + (float)m_Stack[0]);
                            m_Sp = 0;
                            break;

                        case 5:  // rlineto
                            for (int i = 0; i + 1 < m_Sp; i += 2)
                                LineTo(m_X + (float)m_Stack[i], m_Y + (float)m_Stack[i + 1]);
                            m_Sp = 0;
                            break;
                        case 6:  // hlineto
                            AlternatingLines(true);
                            m_Sp = 0;
                            break;
                        case 7:  // vlineto
                            AlternatingLines(false);
                            m_Sp = 0;
                            break;

                        case 8:  // rrcurveto
                            for (int i = 0; i + 5 < m_Sp; i += 6)
                                Curve(m_Stack[i], m_Stack[i + 1], m_Stack[i + 2], m_Stack[i + 3], m_Stack[i + 4], m_Stack[i + 5]);
                            m_Sp = 0;
                            break;
                        case 24: // rcurveline
                        {
                            int i = 0;
                            for (; i + 5 < m_Sp - 2; i += 6)
                                Curve(m_Stack[i], m_Stack[i + 1], m_Stack[i + 2], m_Stack[i + 3], m_Stack[i + 4], m_Stack[i + 5]);
                            if (i + 1 < m_Sp) LineTo(m_X + (float)m_Stack[i], m_Y + (float)m_Stack[i + 1]);
                            m_Sp = 0;
                            break;
                        }
                        case 25: // rlinecurve
                        {
                            int i = 0;
                            for (; i + 1 < m_Sp - 6; i += 2)
                                LineTo(m_X + (float)m_Stack[i], m_Y + (float)m_Stack[i + 1]);
                            if (i + 5 < m_Sp)
                                Curve(m_Stack[i], m_Stack[i + 1], m_Stack[i + 2], m_Stack[i + 3], m_Stack[i + 4], m_Stack[i + 5]);
                            m_Sp = 0;
                            break;
                        }
                        case 26: // vvcurveto
                            VvCurve();
                            m_Sp = 0;
                            break;
                        case 27: // hhcurveto
                            HhCurve();
                            m_Sp = 0;
                            break;
                        case 30: // vhcurveto
                            VhHvCurve(false);
                            m_Sp = 0;
                            break;
                        case 31: // hvcurveto
                            VhHvCurve(true);
                            m_Sp = 0;
                            break;

                        case 10: // callsubr
                        {
                            int idx = (int)Pop() + m_LBias;
                            if (m_LSubrs != null && idx >= 0 && idx + 1 < m_LSubrs.Length)
                                if (Exec(m_LSubrs[idx], m_LSubrs[idx + 1], depth + 1)) return true;
                            break;
                        }
                        case 29: // callgsubr
                        {
                            int idx = (int)Pop() + m_O.m_GSubrBias;
                            var g = m_O.m_GSubrs;
                            if (g != null && idx >= 0 && idx + 1 < g.Length)
                                if (Exec(g[idx], g[idx + 1], depth + 1)) return true;
                            break;
                        }
                        case 11: // return
                            return false;
                        case 14: // endchar
                            TakeWidth(0);
                            return true;

                        case 12: // escape (two-byte operator) — flex etc.
                        {
                            int b1 = m_Data[p++];
                            HandleEscape(b1);
                            m_Sp = 0;
                            break;
                        }

                        default:
                            // Unknown/unsupported operator: clear and continue.
                            m_Sp = 0;
                            break;
                    }
                }
                return false;
            }

            // ---- escape (12 xx) operators: only the flex family matters for
            // outlines; each produces two cubic curves. Args are deltas. ----
            private void HandleEscape(int b1)
            {
                switch (b1)
                {
                    case 34: // hflex: dx1 dx2 dy2 dx3 dx4 dx5 dx6
                        if (m_Sp >= 7)
                        {
                            Curve(m_Stack[0], 0, m_Stack[1], m_Stack[2], m_Stack[3], 0);
                            Curve(m_Stack[4], 0, m_Stack[5], -m_Stack[2], m_Stack[6], 0);
                        }
                        break;
                    case 35: // flex
                        if (m_Sp >= 13)
                        {
                            Curve(m_Stack[0], m_Stack[1], m_Stack[2], m_Stack[3], m_Stack[4], m_Stack[5]);
                            Curve(m_Stack[6], m_Stack[7], m_Stack[8], m_Stack[9], m_Stack[10], m_Stack[11]);
                        }
                        break;
                    case 36: // hflex1
                        if (m_Sp >= 9)
                        {
                            Curve(m_Stack[0], m_Stack[1], m_Stack[2], m_Stack[3], m_Stack[4], 0);
                            Curve(m_Stack[5], 0, m_Stack[6], m_Stack[7], m_Stack[8], -(m_Stack[1] + m_Stack[3] + m_Stack[7]));
                        }
                        break;
                    case 37: // flex1
                        if (m_Sp >= 11)
                        {
                            double dx = m_Stack[0] + m_Stack[2] + m_Stack[4] + m_Stack[6] + m_Stack[8];
                            double dy = m_Stack[1] + m_Stack[3] + m_Stack[5] + m_Stack[7] + m_Stack[9];
                            Curve(m_Stack[0], m_Stack[1], m_Stack[2], m_Stack[3], m_Stack[4], m_Stack[5]);
                            if (Math.Abs(dx) > Math.Abs(dy))
                                Curve(m_Stack[6], m_Stack[7], m_Stack[8], m_Stack[9], m_Stack[10], -dy);
                            else
                                Curve(m_Stack[6], m_Stack[7], m_Stack[8], m_Stack[9], -dx, m_Stack[10]);
                        }
                        break;
                    // 12 3 (and / etc.) arithmetic ops: unsupported, ignored.
                }
            }

            // ---- width handling: the FIRST stack-clearing operator may have
            // an extra leading operand = the glyph width delta. We don't use
            // the width, but we MUST drop it so the path args line up. nArgs is
            // the operator's expected fixed arg count (0 = "even residue"). ----
            private void TakeWidth(int nArgs)
            {
                if (m_HaveWidth) return;
                m_HaveWidth = true;
                bool hasExtra;
                if (nArgs == 0) hasExtra = (m_Sp % 2) == 1;   // endchar/rmoveto-even cases
                else hasExtra = m_Sp > nArgs;
                if (hasExtra) ShiftStackLeft();
            }

            // Stems may also carry a leading width on the first one.
            private void CountStems()
            {
                if (!m_HaveWidth)
                {
                    m_HaveWidth = true;
                    if ((m_Sp & 1) == 1) ShiftStackLeft(); // odd => leading width
                }
                m_NStems += m_Sp / 2;
            }

            private void ShiftStackLeft()
            {
                for (int i = 1; i < m_Sp; i++) m_Stack[i - 1] = m_Stack[i];
                m_Sp--;
            }

            // ---- curve helpers (all args are deltas from current point) ----
            private void Curve(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
            {
                EnsureOpen();
                float x0 = m_X, y0 = m_Y;
                float cx1 = x0 + (float)dx1, cy1 = y0 + (float)dy1;
                float cx2 = cx1 + (float)dx2, cy2 = cy1 + (float)dy2;
                float x3 = cx2 + (float)dx3, y3 = cy2 + (float)dy3;

                // Attach outTangent to the last (current) node.
                int last = m_Cur.Count - 1;
                var a = m_Cur[last];
                a.outTangent = new Vector2(cx1 - x0, cy1 - y0);
                a.type = NodeType.Bezier;
                m_Cur[last] = a;

                var b = VectorNode.Corner(new Vector2(x3, y3));
                b.inTangent = new Vector2(cx2 - x3, cy2 - y3);
                b.type = NodeType.Bezier;
                m_Cur.Add(b);

                m_X = x3; m_Y = y3;
            }

            // vvcurveto: {dxa? dya dxb dyb dyc}+ — first curve may have an
            // initial dx1; all curves are vertical-ish (dy major).
            private void VvCurve()
            {
                int i = 0;
                double dx1 = 0;
                if ((m_Sp & 1) == 1) { dx1 = m_Stack[0]; i = 1; }
                for (; i + 3 < m_Sp; i += 4)
                {
                    Curve(dx1, m_Stack[i], m_Stack[i + 1], m_Stack[i + 2], 0, m_Stack[i + 3]);
                    dx1 = 0;
                }
            }

            // hhcurveto: {dya? dxa dxb dyb dxc}+ — first may have initial dy1.
            private void HhCurve()
            {
                int i = 0;
                double dy1 = 0;
                if ((m_Sp & 1) == 1) { dy1 = m_Stack[0]; i = 1; }
                for (; i + 3 < m_Sp; i += 4)
                {
                    Curve(m_Stack[i], dy1, m_Stack[i + 1], m_Stack[i + 2], m_Stack[i + 3], 0);
                    dy1 = 0;
                }
            }

            // vhcurveto (startHorizontal=false) / hvcurveto (true): alternating
            // curves whose start/end tangents are axis-aligned, alternating
            // axis each curve. A trailing 5th operand (df) on the last curve
            // sets the otherwise-zero final delta.
            private void VhHvCurve(bool startHorizontal)
            {
                int i = 0;
                bool horiz = startHorizontal;
                int remain = m_Sp;
                while (remain >= 4)
                {
                    bool last = remain < 8;
                    double df = (last && (remain == 5)) ? m_Stack[i + 4] : 0;
                    if (horiz)
                        // start horizontal: dx1, (dx2,dy2), dy3 [+ df on x]
                        Curve(m_Stack[i], 0, m_Stack[i + 1], m_Stack[i + 2], df, m_Stack[i + 3]);
                    else
                        // start vertical: dy1, (dx2,dy2), dx3 [+ df on y]
                        Curve(0, m_Stack[i], m_Stack[i + 1], m_Stack[i + 2], m_Stack[i + 3], df);
                    horiz = !horiz;
                    i += 4;
                    remain -= 4;
                }
            }

            // hlineto/vlineto: alternating horizontal/vertical line segments.
            private void AlternatingLines(bool startHorizontal)
            {
                bool horiz = startHorizontal;
                for (int i = 0; i < m_Sp; i++)
                {
                    if (horiz) LineTo(m_X + (float)m_Stack[i], m_Y);
                    else LineTo(m_X, m_Y + (float)m_Stack[i]);
                    horiz = !horiz;
                }
            }

            // ---- path construction ----
            private void MoveTo(float x, float y)
            {
                CloseCurrent();
                m_Cur = new List<VectorNode>(32);
                m_Cur.Add(VectorNode.Corner(new Vector2(x, y)));
                m_Open = true;
                m_X = x; m_Y = y;
            }

            private void EnsureOpen()
            {
                if (!m_Open)
                {
                    // A curve/line before any moveto: anchor at the current pen.
                    m_Cur = new List<VectorNode>(32);
                    m_Cur.Add(VectorNode.Corner(new Vector2(m_X, m_Y)));
                    m_Open = true;
                }
            }

            private void LineTo(float x, float y)
            {
                EnsureOpen();
                m_Cur.Add(VectorNode.Corner(new Vector2(x, y)));
                m_X = x; m_Y = y;
            }

            // Close the current contour: CFF contours are implicitly closed
            // back to the start point. If the last node coincides with the
            // first, merge it (carry its inTangent onto node 0) so there's no
            // zero-length closing edge — mirrors the glyf path's CloseContour.
            private void CloseCurrent()
            {
                if (!m_Open || m_Cur == null) { m_Open = false; return; }
                if (m_Cur.Count >= 2)
                {
                    int li = m_Cur.Count - 1;
                    var last = m_Cur[li];
                    var first = m_Cur[0];
                    if ((last.position - first.position).sqrMagnitude < 1e-4f)
                    {
                        first.inTangent = last.inTangent;
                        if (last.type == NodeType.Bezier) first.type = NodeType.Bezier;
                        m_Cur[0] = first;
                        m_Cur.RemoveAt(li);
                    }
                    if (m_Cur.Count >= 2) m_Out.Add(m_Cur);
                }
                m_Open = false;
                m_Cur = null;
            }

            private void Push(double v) { if (m_Sp < m_Stack.Length) m_Stack[m_Sp++] = v; }
            private double Pop() => m_Sp > 0 ? m_Stack[--m_Sp] : 0;
        }

        // ============================================================
        //  CFF INDEX + DICT primitives
        // ============================================================

        // Reads an INDEX at `off`, returning an array of length count+1 of
        // ABSOLUTE byte offsets (entry i spans [arr[i], arr[i+1])). Sets
        // `after` to the byte just past the INDEX. Returns null/empty-safe.
        private int[] ReadIndex(int off, out int after)
        {
            int count = U16(off);
            if (count == 0) { after = off + 2; return new int[1] { off + 2 }; }
            int offSize = m_Data[off + 2];
            int offArrStart = off + 3;
            int dataBase = offArrStart + (count + 1) * offSize - 1; // offsets are 1-based
            var result = new int[count + 1];
            for (int i = 0; i <= count; i++)
            {
                int rel = ReadOffset(offArrStart + i * offSize, offSize);
                result[i] = dataBase + rel;
            }
            after = result[count];
            return result;
        }

        // Skips an INDEX, returning the offset just past it.
        private int SkipIndex(int off)
        {
            int count = U16(off);
            if (count == 0) return off + 2;
            int offSize = m_Data[off + 2];
            int offArrStart = off + 3;
            int dataBase = offArrStart + (count + 1) * offSize - 1;
            int last = ReadOffset(offArrStart + count * offSize, offSize);
            return dataBase + last;
        }

        private int ReadOffset(int p, int offSize)
        {
            int v = 0;
            for (int i = 0; i < offSize; i++) v = (v << 8) | m_Data[p + i];
            return v;
        }

        // Parses a CFF DICT in [start,end) into operator -> operand list.
        // Two-byte operators (escape 12 xx) are keyed as 1200 + xx (see C2).
        private Dictionary<int, List<double>> ParseDict(int start, int end)
        {
            var dict = new Dictionary<int, List<double>>();
            var operands = new List<double>(8);
            int p = start;
            while (p < end)
            {
                int b0 = m_Data[p];
                if (b0 <= 21)
                {
                    // operator
                    int op = b0;
                    p++;
                    if (b0 == 12) { op = C2(m_Data[p]); p++; }
                    dict[op] = new List<double>(operands);
                    operands.Clear();
                }
                else if (b0 == 28) { operands.Add((short)((m_Data[p + 1] << 8) | m_Data[p + 2])); p += 3; }
                else if (b0 == 29)
                {
                    int v = (m_Data[p + 1] << 24) | (m_Data[p + 2] << 16) | (m_Data[p + 3] << 8) | m_Data[p + 4];
                    operands.Add(v); p += 5;
                }
                else if (b0 == 30) { operands.Add(ParseReal(ref p)); }
                else if (b0 >= 32 && b0 <= 246) { operands.Add(b0 - 139); p++; }
                else if (b0 >= 247 && b0 <= 250) { operands.Add((b0 - 247) * 256 + m_Data[p + 1] + 108); p += 2; }
                else if (b0 >= 251 && b0 <= 254) { operands.Add(-(b0 - 251) * 256 - m_Data[p + 1] - 108); p += 2; }
                else p++; // 255 reserved in DICT — skip defensively
            }
            return dict;
        }

        // DICT real number (operator 30): BCD nibbles. We only need integers
        // for the operators we read (offsets/sizes), but parse the value fully.
        private double ParseReal(ref int p)
        {
            p++; // consume the 30
            var sb = new System.Text.StringBuilder(16);
            bool done = false;
            while (!done && p < m_Data.Length)
            {
                int b = m_Data[p++];
                for (int half = 0; half < 2; half++)
                {
                    int nib = half == 0 ? (b >> 4) : (b & 0xF);
                    switch (nib)
                    {
                        case 0xA: sb.Append('.'); break;
                        case 0xB: sb.Append('E'); break;
                        case 0xC: sb.Append("E-"); break;
                        case 0xE: sb.Append('-'); break;
                        case 0xF: done = true; break;
                        case 0xD: break; // reserved
                        default: sb.Append((char)('0' + nib)); break;
                    }
                    if (done) break;
                }
            }
            double.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double val);
            return val;
        }

        // Standard Type 2 subroutine bias by subr count.
        private static int SubrBias(int n)
        {
            if (n < 1240) return 107;
            if (n < 33900) return 1131;
            return 32768;
        }

        // Two-byte (escape) operator key.
        private static int C2(int b1) => 1200 + b1;

        private int U16(int o) => (m_Data[o] << 8) | m_Data[o + 1];
    }
}
