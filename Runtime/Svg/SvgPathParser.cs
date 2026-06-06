using System.Collections.Generic;
using UnityEngine;
using VMG.Core;

namespace VMG.Svg
{
    /// Parses the contents of an SVG <path d="..."> attribute into a list
    /// of VectorNodes. Cubic Beziers are stored as nodes with in/out
    /// tangents; quadratic Beziers are auto-elevated to cubic; arcs are
    /// converted to cubic approximations.
    ///
    /// A single path "d" may contain multiple sub-paths (each starting with
    /// M/m). Each sub-path becomes its own entry in the output list.
    public static class SvgPathParser
    {
        public sealed class SubPath
        {
            public readonly List<VectorNode> nodes = new List<VectorNode>(16);
            public bool closed;
        }

        public static List<SubPath> Parse(string d)
        {
            var result = new List<SubPath>();
            if (string.IsNullOrEmpty(d)) return result;

            var tokens = Tokenize(d);
            int ti = 0;

            SubPath current = null;
            char prevCmd = '\0';
            Vector2 cursor = Vector2.zero;
            Vector2 subStart = Vector2.zero;
            // For S/s and T/t reflection.
            Vector2 lastCubicCtrl = Vector2.zero;
            Vector2 lastQuadCtrl = Vector2.zero;
            bool hadCubic = false;
            bool hadQuad = false;

            while (ti < tokens.Count)
            {
                var tok = tokens[ti];
                char cmd;
                if (tok.isCommand)
                {
                    cmd = tok.command;
                    ti++;
                }
                else
                {
                    // Implicit repeat of previous command. After M/m, repeat as L/l.
                    if (prevCmd == 'M') cmd = 'L';
                    else if (prevCmd == 'm') cmd = 'l';
                    else cmd = prevCmd;
                    if (cmd == '\0') break;
                }

                bool rel = char.IsLower(cmd);
                char upper = char.ToUpperInvariant(cmd);

                switch (upper)
                {
                    case 'M':
                    {
                        Vector2 p = ReadVec(tokens, ref ti);
                        if (rel && current != null) p += cursor;
                        // Start new sub-path.
                        current = new SubPath();
                        result.Add(current);
                        current.nodes.Add(VectorNode.Corner(p));
                        cursor = p;
                        subStart = p;
                        hadCubic = hadQuad = false;
                        break;
                    }
                    case 'L':
                    {
                        Vector2 p = ReadVec(tokens, ref ti);
                        if (rel) p += cursor;
                        EnsureSub(ref current, result, cursor);
                        current.nodes.Add(VectorNode.Corner(p));
                        cursor = p;
                        hadCubic = hadQuad = false;
                        break;
                    }
                    case 'H':
                    {
                        float x = ReadFloat(tokens, ref ti);
                        Vector2 p = rel ? new Vector2(cursor.x + x, cursor.y) : new Vector2(x, cursor.y);
                        EnsureSub(ref current, result, cursor);
                        current.nodes.Add(VectorNode.Corner(p));
                        cursor = p;
                        hadCubic = hadQuad = false;
                        break;
                    }
                    case 'V':
                    {
                        float y = ReadFloat(tokens, ref ti);
                        Vector2 p = rel ? new Vector2(cursor.x, cursor.y + y) : new Vector2(cursor.x, y);
                        EnsureSub(ref current, result, cursor);
                        current.nodes.Add(VectorNode.Corner(p));
                        cursor = p;
                        hadCubic = hadQuad = false;
                        break;
                    }
                    case 'C':
                    {
                        Vector2 c1 = ReadVec(tokens, ref ti);
                        Vector2 c2 = ReadVec(tokens, ref ti);
                        Vector2 p  = ReadVec(tokens, ref ti);
                        if (rel) { c1 += cursor; c2 += cursor; p += cursor; }
                        EmitCubic(ref current, result, ref cursor, c1, c2, p);
                        lastCubicCtrl = c2;
                        hadCubic = true; hadQuad = false;
                        break;
                    }
                    case 'S':
                    {
                        Vector2 c2 = ReadVec(tokens, ref ti);
                        Vector2 p  = ReadVec(tokens, ref ti);
                        if (rel) { c2 += cursor; p += cursor; }
                        Vector2 c1 = hadCubic ? (2f * cursor - lastCubicCtrl) : cursor;
                        EmitCubic(ref current, result, ref cursor, c1, c2, p);
                        lastCubicCtrl = c2;
                        hadCubic = true; hadQuad = false;
                        break;
                    }
                    case 'Q':
                    {
                        Vector2 qc = ReadVec(tokens, ref ti);
                        Vector2 p  = ReadVec(tokens, ref ti);
                        if (rel) { qc += cursor; p += cursor; }
                        EmitQuadAsCubic(ref current, result, ref cursor, qc, p);
                        lastQuadCtrl = qc;
                        hadQuad = true; hadCubic = false;
                        break;
                    }
                    case 'T':
                    {
                        Vector2 p = ReadVec(tokens, ref ti);
                        if (rel) p += cursor;
                        Vector2 qc = hadQuad ? (2f * cursor - lastQuadCtrl) : cursor;
                        EmitQuadAsCubic(ref current, result, ref cursor, qc, p);
                        lastQuadCtrl = qc;
                        hadQuad = true; hadCubic = false;
                        break;
                    }
                    case 'A':
                    {
                        float rx = ReadFloat(tokens, ref ti);
                        float ry = ReadFloat(tokens, ref ti);
                        float xrot = ReadFloat(tokens, ref ti);
                        float largeArc = ReadFloat(tokens, ref ti);
                        float sweep = ReadFloat(tokens, ref ti);
                        Vector2 p = ReadVec(tokens, ref ti);
                        if (rel) p += cursor;
                        EnsureSub(ref current, result, cursor);
                        EmitArc(current, cursor, p, rx, ry, xrot * Mathf.Deg2Rad, largeArc != 0f, sweep != 0f);
                        cursor = p;
                        hadCubic = hadQuad = false;
                        break;
                    }
                    case 'Z':
                    {
                        if (current != null)
                        {
                            current.closed = true;
                            cursor = subStart;
                        }
                        hadCubic = hadQuad = false;
                        break;
                    }
                }

                prevCmd = cmd;
            }

            return result;
        }

        // ---------------- emission helpers ----------------

        private static void EnsureSub(ref SubPath current, List<SubPath> all, Vector2 cursor)
        {
            if (current == null)
            {
                current = new SubPath();
                current.nodes.Add(VectorNode.Corner(cursor));
                all.Add(current);
            }
        }

        private static void EmitCubic(ref SubPath current, List<SubPath> all, ref Vector2 cursor,
                                      Vector2 c1, Vector2 c2, Vector2 p)
        {
            EnsureSub(ref current, all, cursor);
            int lastIdx = current.nodes.Count - 1;
            var a = current.nodes[lastIdx];
            a.outTangent = c1 - a.position;
            a.type = NodeType.Bezier;
            current.nodes[lastIdx] = a;

            var b = VectorNode.Corner(p);
            b.inTangent = c2 - p;
            b.type = NodeType.Bezier;
            current.nodes.Add(b);
            cursor = p;
        }

        private static void EmitQuadAsCubic(ref SubPath current, List<SubPath> all, ref Vector2 cursor,
                                            Vector2 qc, Vector2 p)
        {
            // Elevate quadratic -> cubic. C1 = P0 + 2/3 (Q - P0), C2 = P2 + 2/3 (Q - P2).
            Vector2 c1 = cursor + (2f / 3f) * (qc - cursor);
            Vector2 c2 = p + (2f / 3f) * (qc - p);
            EmitCubic(ref current, all, ref cursor, c1, c2, p);
        }

        private static void EmitArc(SubPath current, Vector2 start, Vector2 end,
                                    float rx, float ry, float xRot, bool largeArc, bool sweep)
        {
            if (start == end) return;
            rx = Mathf.Abs(rx); ry = Mathf.Abs(ry);
            if (rx < 1e-5f || ry < 1e-5f)
            {
                current.nodes.Add(VectorNode.Corner(end));
                return;
            }

            // Endpoint -> center parametrization (per SVG spec appendix).
            float cosR = Mathf.Cos(xRot), sinR = Mathf.Sin(xRot);
            Vector2 d = (start - end) * 0.5f;
            Vector2 p = new Vector2(cosR * d.x + sinR * d.y, -sinR * d.x + cosR * d.y);

            float rxSq = rx * rx, rySq = ry * ry;
            float pxSq = p.x * p.x, pySq = p.y * p.y;
            // Scale radii up if too small.
            float lambda = pxSq / rxSq + pySq / rySq;
            if (lambda > 1f)
            {
                float s = Mathf.Sqrt(lambda);
                rx *= s; ry *= s;
                rxSq = rx * rx; rySq = ry * ry;
            }

            float sign = (largeArc == sweep) ? -1f : 1f;
            float num = rxSq * rySq - rxSq * pySq - rySq * pxSq;
            float den = rxSq * pySq + rySq * pxSq;
            float coef = sign * Mathf.Sqrt(Mathf.Max(0f, num / den));
            Vector2 cPrime = new Vector2(coef * rx * p.y / ry, -coef * ry * p.x / rx);

            Vector2 mid = (start + end) * 0.5f;
            Vector2 center = new Vector2(cosR * cPrime.x - sinR * cPrime.y + mid.x,
                                         sinR * cPrime.x + cosR * cPrime.y + mid.y);

            float angleStart = Angle(new Vector2(1f, 0f), new Vector2((p.x - cPrime.x) / rx, (p.y - cPrime.y) / ry));
            float delta = Angle(
                new Vector2((p.x - cPrime.x) / rx, (p.y - cPrime.y) / ry),
                new Vector2((-p.x - cPrime.x) / rx, (-p.y - cPrime.y) / ry));
            delta %= 2f * Mathf.PI;
            if (!sweep && delta > 0f) delta -= 2f * Mathf.PI;
            else if (sweep && delta < 0f) delta += 2f * Mathf.PI;

            // Subdivide into <=90deg cubic segments per spec.
            int segs = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(delta) / (Mathf.PI * 0.5f)));
            float perSeg = delta / segs;
            float t = Mathf.Tan(perSeg * 0.5f);
            float alpha = Mathf.Sin(perSeg) * (Mathf.Sqrt(4f + 3f * t * t) - 1f) / 3f;

            float angle = angleStart;
            for (int i = 0; i < segs; i++)
            {
                float nextAng = angle + perSeg;
                Vector2 cur = ArcPoint(center, rx, ry, cosR, sinR, angle);
                Vector2 nxt = ArcPoint(center, rx, ry, cosR, sinR, nextAng);
                Vector2 dCur = ArcDeriv(rx, ry, cosR, sinR, angle);
                Vector2 dNxt = ArcDeriv(rx, ry, cosR, sinR, nextAng);

                Vector2 c1 = cur + alpha * dCur;
                Vector2 c2 = nxt - alpha * dNxt;

                int lastIdx = current.nodes.Count - 1;
                var a = current.nodes[lastIdx];
                a.outTangent = c1 - a.position;
                a.type = NodeType.Bezier;
                current.nodes[lastIdx] = a;

                var b = VectorNode.Corner(nxt);
                b.inTangent = c2 - nxt;
                b.type = NodeType.Bezier;
                current.nodes.Add(b);

                angle = nextAng;
            }
        }

        private static Vector2 ArcPoint(Vector2 c, float rx, float ry, float cosR, float sinR, float ang)
        {
            float x = rx * Mathf.Cos(ang);
            float y = ry * Mathf.Sin(ang);
            return new Vector2(cosR * x - sinR * y + c.x, sinR * x + cosR * y + c.y);
        }

        private static Vector2 ArcDeriv(float rx, float ry, float cosR, float sinR, float ang)
        {
            float x = -rx * Mathf.Sin(ang);
            float y =  ry * Mathf.Cos(ang);
            return new Vector2(cosR * x - sinR * y, sinR * x + cosR * y);
        }

        private static float Angle(Vector2 u, Vector2 v)
        {
            float dot = Mathf.Clamp(Vector2.Dot(u.normalized, v.normalized), -1f, 1f);
            float a = Mathf.Acos(dot);
            float cross = u.x * v.y - u.y * v.x;
            return cross < 0f ? -a : a;
        }

        // ---------------- tokenizer ----------------

        private struct Token
        {
            public bool isCommand;
            public char command;
            public float number;
        }

        private static List<Token> Tokenize(string d)
        {
            var list = new List<Token>(64);
            int i = 0;
            int len = d.Length;
            while (i < len)
            {
                char c = d[i];
                if (c == ' ' || c == ',' || c == '\t' || c == '\n' || c == '\r') { i++; continue; }
                if (IsCommand(c))
                {
                    list.Add(new Token { isCommand = true, command = c });
                    i++;
                    continue;
                }
                if (c == '-' || c == '+' || c == '.' || (c >= '0' && c <= '9'))
                {
                    int start = i;
                    bool dotSeen = false;
                    bool eSeen = false;
                    if (c == '-' || c == '+') i++;
                    while (i < len)
                    {
                        char x = d[i];
                        if (x >= '0' && x <= '9') i++;
                        else if (x == '.' && !dotSeen) { dotSeen = true; i++; }
                        else if ((x == 'e' || x == 'E') && !eSeen)
                        {
                            eSeen = true; i++;
                            if (i < len && (d[i] == '+' || d[i] == '-')) i++;
                        }
                        else break;
                    }
                    string s = d.Substring(start, i - start);
                    if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float v))
                    {
                        list.Add(new Token { isCommand = false, number = v });
                    }
                    continue;
                }
                // Unknown character — skip.
                i++;
            }
            return list;
        }

        private static bool IsCommand(char c)
        {
            switch (c)
            {
                case 'M': case 'm': case 'L': case 'l':
                case 'H': case 'h': case 'V': case 'v':
                case 'C': case 'c': case 'S': case 's':
                case 'Q': case 'q': case 'T': case 't':
                case 'A': case 'a': case 'Z': case 'z':
                    return true;
            }
            return false;
        }

        private static float ReadFloat(List<Token> tokens, ref int i)
        {
            // Caller must have consumed/handled any preceding command token —
            // numeric operands never cross a command boundary.
            if (i >= tokens.Count || tokens[i].isCommand) return 0f;
            return tokens[i++].number;
        }

        private static Vector2 ReadVec(List<Token> tokens, ref int i)
        {
            float x = ReadFloat(tokens, ref i);
            float y = ReadFloat(tokens, ref i);
            return new Vector2(x, y);
        }
    }
}
