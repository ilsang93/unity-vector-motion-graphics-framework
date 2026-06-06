using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEngine;
using VMG.Core;

namespace VMG.Svg
{
    /// Parses an SVG document into a VMGShapeAsset. Path-first MVP:
    /// supports <path>, <rect>, <circle>, <ellipse>, <line>, <polyline>,
    /// <polygon>; viewBox normalization; transform="matrix/translate/scale/rotate";
    /// fill / stroke / stroke-width / stroke-linecap / stroke-linejoin (both
    /// presentation attributes and inline style="...").
    ///
    /// Out of scope for MVP: gradients, filters, text, masks, clip paths,
    /// CSS classes, animation, use/symbol references.
    public static class SvgDocumentParser
    {
        public static VMGShapeAsset Parse(string svgText)
        {
            if (string.IsNullOrEmpty(svgText)) return null;

            var asset = ScriptableObject.CreateInstance<VMGShapeAsset>();

            using (var reader = XmlReader.Create(new StringReader(svgText), MakeSettings()))
            {
                var ctxStack = new Stack<Context>();
                ctxStack.Push(Context.Identity);

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        var local = reader.LocalName;
                        var current = ctxStack.Peek();
                        // Inherit + merge with this element's attributes.
                        var ctx = current.WithElement(reader);

                        if (local == "svg")
                        {
                            ParseSvgRoot(reader, asset);
                        }
                        else
                        {
                            ConvertElement(local, reader, ctx, asset);
                        }

                        if (reader.IsEmptyElement)
                        {
                            // nothing to pop
                        }
                        else
                        {
                            ctxStack.Push(ctx);
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (ctxStack.Count > 1) ctxStack.Pop();
                    }
                }
            }

            return asset;
        }

        private static XmlReaderSettings MakeSettings()
        {
            return new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true,
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            };
        }

        private static void ParseSvgRoot(XmlReader reader, VMGShapeAsset asset)
        {
            string vb = reader.GetAttribute("viewBox");
            if (!string.IsNullOrEmpty(vb))
            {
                var parts = vb.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float w)
                    && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float h))
                {
                    asset.viewBoxSize = new Vector2(w, h);
                    return;
                }
            }
            string ws = reader.GetAttribute("width");
            string hs = reader.GetAttribute("height");
            if (TryParseLength(ws, out float ww) && TryParseLength(hs, out float hh))
            {
                asset.viewBoxSize = new Vector2(ww, hh);
            }
        }

        private static bool TryParseLength(string s, out float v)
        {
            v = 0f;
            if (string.IsNullOrEmpty(s)) return false;
            // Strip unit suffix (px/pt/em/...) — we treat them as user units.
            int end = 0;
            while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.' || s[end] == '-' || s[end] == '+' || s[end] == 'e' || s[end] == 'E')) end++;
            return float.TryParse(s.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        // ---------------- per-element handlers ----------------

        private static void ConvertElement(string local, XmlReader reader, Context ctx, VMGShapeAsset asset)
        {
            switch (local)
            {
                case "path": ConvertPath(reader, ctx, asset); break;
                case "rect": ConvertRect(reader, ctx, asset); break;
                case "circle": ConvertCircle(reader, ctx, asset); break;
                case "ellipse": ConvertEllipse(reader, ctx, asset); break;
                case "line": ConvertLine(reader, ctx, asset); break;
                case "polyline": ConvertPoly(reader, ctx, asset, false); break;
                case "polygon": ConvertPoly(reader, ctx, asset, true); break;
                // <g>, <svg> are containers handled by the context stack only.
            }
        }

        private static void ConvertPath(XmlReader reader, Context ctx, VMGShapeAsset asset)
        {
            string d = reader.GetAttribute("d");
            if (string.IsNullOrEmpty(d)) return;
            var subs = SvgPathParser.Parse(d);
            foreach (var sub in subs)
            {
                var shape = NewShape(reader, ctx);
                shape.closed = sub.closed;
                ApplyTransform(sub.nodes, ctx.matrix);
                shape.nodes.AddRange(sub.nodes);
                if (HasContent(shape)) asset.subShapes.Add(shape);
            }
        }

        private static void ConvertRect(XmlReader reader, Context ctx, VMGShapeAsset asset)
        {
            float x = ReadFloat(reader, "x", 0f);
            float y = ReadFloat(reader, "y", 0f);
            float w = ReadFloat(reader, "width", 0f);
            float h = ReadFloat(reader, "height", 0f);
            if (w <= 0f || h <= 0f) return;
            float rx = ReadFloat(reader, "rx", 0f);
            float ry = ReadFloat(reader, "ry", rx);
            if (rx <= 0f && ry > 0f) rx = ry;
            if (ry <= 0f && rx > 0f) ry = rx;

            var nodes = new List<VectorNode>();
            if (rx > 0f && ry > 0f)
            {
                rx = Mathf.Min(rx, w * 0.5f);
                ry = Mathf.Min(ry, h * 0.5f);
                // Cubic-Bezier rounded corners (SVG uses ~0.5523 kappa).
                const float k = 0.5522847498f;
                float kx = rx * k, ky = ry * k;
                AppendBezierCorner(nodes,
                    new Vector2(x + rx, y), new Vector2(x, y + ry),
                    new Vector2(x + rx - kx, y), new Vector2(x, y + ry - ky));
                nodes.Add(VectorNode.Corner(new Vector2(x, y + h - ry)));
                AppendBezierCorner(nodes,
                    new Vector2(x, y + h - ry), new Vector2(x + rx, y + h),
                    new Vector2(x, y + h - ry + ky), new Vector2(x + rx - kx, y + h));
                nodes.Add(VectorNode.Corner(new Vector2(x + w - rx, y + h)));
                AppendBezierCorner(nodes,
                    new Vector2(x + w - rx, y + h), new Vector2(x + w, y + h - ry),
                    new Vector2(x + w - rx + kx, y + h), new Vector2(x + w, y + h - ry + ky));
                nodes.Add(VectorNode.Corner(new Vector2(x + w, y + ry)));
                AppendBezierCorner(nodes,
                    new Vector2(x + w, y + ry), new Vector2(x + w - rx, y),
                    new Vector2(x + w, y + ry - ky), new Vector2(x + w - rx + kx, y));
            }
            else
            {
                nodes.Add(VectorNode.Corner(new Vector2(x, y)));
                nodes.Add(VectorNode.Corner(new Vector2(x + w, y)));
                nodes.Add(VectorNode.Corner(new Vector2(x + w, y + h)));
                nodes.Add(VectorNode.Corner(new Vector2(x, y + h)));
            }
            ApplyTransform(nodes, ctx.matrix);

            var shape = NewShape(reader, ctx);
            shape.closed = true;
            shape.nodes.AddRange(nodes);
            if (HasContent(shape)) asset.subShapes.Add(shape);
        }

        private static void AppendBezierCorner(List<VectorNode> nodes, Vector2 from, Vector2 to, Vector2 ctrl1, Vector2 ctrl2)
        {
            if (nodes.Count == 0 || nodes[nodes.Count - 1].position != from)
            {
                nodes.Add(VectorNode.Corner(from));
            }
            int idx = nodes.Count - 1;
            var a = nodes[idx];
            a.outTangent = ctrl1 - a.position;
            a.type = NodeType.Bezier;
            nodes[idx] = a;
            var b = VectorNode.Corner(to);
            b.inTangent = ctrl2 - to;
            b.type = NodeType.Bezier;
            nodes.Add(b);
        }

        private static void ConvertCircle(XmlReader reader, Context ctx, VMGShapeAsset asset)
        {
            float cx = ReadFloat(reader, "cx", 0f);
            float cy = ReadFloat(reader, "cy", 0f);
            float r = ReadFloat(reader, "r", 0f);
            if (r <= 0f) return;
            EmitEllipse(reader, ctx, asset, cx, cy, r, r);
        }

        private static void ConvertEllipse(XmlReader reader, Context ctx, VMGShapeAsset asset)
        {
            float cx = ReadFloat(reader, "cx", 0f);
            float cy = ReadFloat(reader, "cy", 0f);
            float rx = ReadFloat(reader, "rx", 0f);
            float ry = ReadFloat(reader, "ry", 0f);
            if (rx <= 0f || ry <= 0f) return;
            EmitEllipse(reader, ctx, asset, cx, cy, rx, ry);
        }

        private static void EmitEllipse(XmlReader reader, Context ctx, VMGShapeAsset asset,
                                        float cx, float cy, float rx, float ry)
        {
            const float k = 0.5522847498f;
            float kx = rx * k, ky = ry * k;
            var nodes = new List<VectorNode>(5);
            // 4 cubic-bezier quadrants, kappa-approximated circle.
            Vector2 top = new Vector2(cx, cy - ry);
            Vector2 right = new Vector2(cx + rx, cy);
            Vector2 bot = new Vector2(cx, cy + ry);
            Vector2 left = new Vector2(cx - rx, cy);
            nodes.Add(VectorNode.Corner(top));
            AddCubicSegment(nodes, top, right, new Vector2(cx + kx, cy - ry), new Vector2(cx + rx, cy - ky));
            AddCubicSegment(nodes, right, bot, new Vector2(cx + rx, cy + ky), new Vector2(cx + kx, cy + ry));
            AddCubicSegment(nodes, bot, left, new Vector2(cx - kx, cy + ry), new Vector2(cx - rx, cy + ky));
            AddCubicSegment(nodes, left, top, new Vector2(cx - rx, cy - ky), new Vector2(cx - kx, cy - ry));
            // Last node duplicates the start; drop it so the closed loop has 4 nodes.
            if (nodes.Count > 0 && nodes[nodes.Count - 1].position == nodes[0].position)
            {
                // Move the trailing inTangent onto the first node.
                var first = nodes[0];
                first.inTangent = nodes[nodes.Count - 1].inTangent;
                first.type = NodeType.Bezier;
                nodes[0] = first;
                nodes.RemoveAt(nodes.Count - 1);
            }
            ApplyTransform(nodes, ctx.matrix);
            var shape = NewShape(reader, ctx);
            shape.closed = true;
            shape.nodes.AddRange(nodes);
            if (HasContent(shape)) asset.subShapes.Add(shape);
        }

        private static void AddCubicSegment(List<VectorNode> nodes, Vector2 from, Vector2 to, Vector2 ctrl1, Vector2 ctrl2)
        {
            int idx = nodes.Count - 1;
            var a = nodes[idx];
            a.outTangent = ctrl1 - a.position;
            a.type = NodeType.Bezier;
            nodes[idx] = a;
            var b = VectorNode.Corner(to);
            b.inTangent = ctrl2 - to;
            b.type = NodeType.Bezier;
            nodes.Add(b);
        }

        private static void ConvertLine(XmlReader reader, Context ctx, VMGShapeAsset asset)
        {
            float x1 = ReadFloat(reader, "x1", 0f);
            float y1 = ReadFloat(reader, "y1", 0f);
            float x2 = ReadFloat(reader, "x2", 0f);
            float y2 = ReadFloat(reader, "y2", 0f);
            var nodes = new List<VectorNode>(2)
            {
                VectorNode.Corner(new Vector2(x1, y1)),
                VectorNode.Corner(new Vector2(x2, y2)),
            };
            ApplyTransform(nodes, ctx.matrix);
            var shape = NewShape(reader, ctx);
            shape.closed = false;
            shape.nodes.AddRange(nodes);
            if (HasContent(shape)) asset.subShapes.Add(shape);
        }

        private static void ConvertPoly(XmlReader reader, Context ctx, VMGShapeAsset asset, bool closed)
        {
            string pts = reader.GetAttribute("points");
            if (string.IsNullOrEmpty(pts)) return;
            var parts = pts.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var nodes = new List<VectorNode>(parts.Length / 2);
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                {
                    nodes.Add(VectorNode.Corner(new Vector2(x, y)));
                }
            }
            if (nodes.Count < 2) return;
            ApplyTransform(nodes, ctx.matrix);
            var shape = NewShape(reader, ctx);
            shape.closed = closed;
            shape.nodes.AddRange(nodes);
            if (HasContent(shape)) asset.subShapes.Add(shape);
        }

        // ---------------- styling ----------------

        private static VMGSubShape NewShape(XmlReader reader, Context ctx)
        {
            var s = new VMGSubShape { id = reader.GetAttribute("id") };
            s.fill = ctx.fill;
            s.stroke = ctx.stroke;
            return s;
        }

        private static bool HasContent(VMGSubShape s)
        {
            return s.nodes.Count >= 2 && (s.fill.enabled || s.stroke.enabled);
        }

        private static float ReadFloat(XmlReader reader, string attr, float fallback)
        {
            string v = reader.GetAttribute(attr);
            if (string.IsNullOrEmpty(v)) return fallback;
            return TryParseLength(v, out float f) ? f : fallback;
        }

        private static void ApplyTransform(List<VectorNode> nodes, Matrix2D m)
        {
            if (m.IsIdentity) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                n.position = m.MultiplyPoint(n.position);
                if (n.inTangent != Vector2.zero) n.inTangent = m.MultiplyVector(n.inTangent);
                if (n.outTangent != Vector2.zero) n.outTangent = m.MultiplyVector(n.outTangent);
                nodes[i] = n;
            }
        }

        // ---------------- context (inheritable style + transform) ----------------

        internal struct Context
        {
            public Matrix2D matrix;
            public FillStyle fill;
            public StrokeStyle stroke;

            public static Context Identity => new Context
            {
                matrix = Matrix2D.Identity,
                fill = new FillStyle { enabled = true, color = Color.black }, // SVG default fill = black
                stroke = new StrokeStyle
                {
                    enabled = false,
                    color = Color.black,
                    width = 1f,
                    alignment = StrokeAlignment.Center,
                    cap = LineCap.Butt,
                    join = LineJoin.Miter,
                    miterLimit = 4f,
                },
            };

            public Context WithElement(XmlReader reader)
            {
                var c = this;
                // Transform.
                string tr = reader.GetAttribute("transform");
                if (!string.IsNullOrEmpty(tr))
                {
                    var local = SvgTransformParser.Parse(tr);
                    c.matrix = Matrix2D.Multiply(c.matrix, local);
                }
                // Inline style overrides everything else.
                string style = reader.GetAttribute("style");
                ApplyStyle(reader, style, ref c);
                return c;
            }
        }

        private static void ApplyStyle(XmlReader reader, string style, ref Context c)
        {
            // Presentation attributes.
            string fillAttr = reader.GetAttribute("fill");
            string fillOpAttr = reader.GetAttribute("fill-opacity");
            string strokeAttr = reader.GetAttribute("stroke");
            string strokeOpAttr = reader.GetAttribute("stroke-opacity");
            string strokeWAttr = reader.GetAttribute("stroke-width");
            string capAttr = reader.GetAttribute("stroke-linecap");
            string joinAttr = reader.GetAttribute("stroke-linejoin");
            string opAttr = reader.GetAttribute("opacity");

            // Style attribute overrides each of the above.
            if (!string.IsNullOrEmpty(style))
            {
                foreach (var prop in style.Split(';'))
                {
                    int colon = prop.IndexOf(':');
                    if (colon <= 0) continue;
                    string k = prop.Substring(0, colon).Trim();
                    string v = prop.Substring(colon + 1).Trim();
                    switch (k)
                    {
                        case "fill": fillAttr = v; break;
                        case "fill-opacity": fillOpAttr = v; break;
                        case "stroke": strokeAttr = v; break;
                        case "stroke-opacity": strokeOpAttr = v; break;
                        case "stroke-width": strokeWAttr = v; break;
                        case "stroke-linecap": capAttr = v; break;
                        case "stroke-linejoin": joinAttr = v; break;
                        case "opacity": opAttr = v; break;
                    }
                }
            }

            if (fillAttr != null)
            {
                if (fillAttr == "none") c.fill.enabled = false;
                else if (SvgColorParser.TryParse(fillAttr, out Color col)) { c.fill.enabled = true; c.fill.color = col; }
            }
            if (TryParseFloat(fillOpAttr, out float fOp)) { var col = c.fill.color; col.a *= Mathf.Clamp01(fOp); c.fill.color = col; }

            if (strokeAttr != null)
            {
                if (strokeAttr == "none") c.stroke.enabled = false;
                else if (SvgColorParser.TryParse(strokeAttr, out Color col)) { c.stroke.enabled = true; c.stroke.color = col; }
            }
            if (TryParseFloat(strokeOpAttr, out float sOp)) { var col = c.stroke.color; col.a *= Mathf.Clamp01(sOp); c.stroke.color = col; }
            if (TryParseFloat(strokeWAttr, out float sw)) c.stroke.width = sw;

            if (capAttr != null)
            {
                switch (capAttr)
                {
                    case "butt": c.stroke.cap = LineCap.Butt; break;
                    case "square": c.stroke.cap = LineCap.Square; break;
                    case "round": c.stroke.cap = LineCap.Round; break;
                }
            }
            if (joinAttr != null)
            {
                switch (joinAttr)
                {
                    case "miter": c.stroke.join = LineJoin.Miter; break;
                    case "bevel": c.stroke.join = LineJoin.Bevel; break;
                    case "round": c.stroke.join = LineJoin.Round; break;
                }
            }
            if (TryParseFloat(opAttr, out float op))
            {
                float a = Mathf.Clamp01(op);
                var fc = c.fill.color; fc.a *= a; c.fill.color = fc;
                var sc = c.stroke.color; sc.a *= a; c.stroke.color = sc;
            }
        }

        private static bool TryParseFloat(string s, out float v)
        {
            v = 0f;
            return !string.IsNullOrEmpty(s)
                   && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
