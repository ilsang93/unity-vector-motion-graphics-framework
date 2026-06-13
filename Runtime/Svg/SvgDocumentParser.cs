using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEngine;
using VMG.Core;

namespace VMG.Svg
{
    /// Parses an SVG document into a VMGShapeAsset. Supports <path>, <rect>,
    /// <circle>, <ellipse>, <line>, <polyline>, <polygon>, <g>, <defs>,
    /// <symbol>, <use>; viewBox normalization; transform="matrix/translate/
    /// scale/rotate"; fill / stroke / stroke-width / stroke-linecap /
    /// stroke-linejoin (presentation attributes and inline style="...").
    ///
    /// `<use href="#id">` inlines the referenced element at the use site,
    /// inheriting use's own transform and style. Forward references work
    /// because the document is loaded into a DOM first. Cycles are detected
    /// and broken with a warning.
    ///
    /// `<style>` blocks are parsed for simple class selectors only
    /// (`.foo { ... }`, `.a, .b { ... }`). No tag/id/attr/pseudo
    /// selectors, no specificity, no `!important`. Applied per spec
    /// precedence: inherited → class → presentation attribute → inline
    /// style (later overrides earlier).
    ///
    /// Out of scope: gradients, filters, text, masks, clip paths,
    /// animation.
    public static class SvgDocumentParser
    {
        public static VMGShapeAsset Parse(string svgText)
        {
            if (string.IsNullOrEmpty(svgText)) return null;

            var doc = new XmlDocument();
            try
            {
                using (var reader = XmlReader.Create(new StringReader(svgText), MakeSettings()))
                {
                    doc.Load(reader);
                }
            }
            catch (XmlException e)
            {
                Debug.LogWarning($"[VMG SVG] XML parse failed: {e.Message}");
                return null;
            }
            if (doc.DocumentElement == null) return null;

            var asset = ScriptableObject.CreateInstance<VMGShapeAsset>();

            // 1st pass: index every element with an id attribute so <use href="#x">
            // resolves regardless of document order. Also collect <style> blocks
            // and parse them into class-name rule tables.
            var idTable = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
            var classRules = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            IndexIds(doc.DocumentElement, idTable);
            CollectStyleRules(doc.DocumentElement, classRules);

            // 2nd pass: convert.
            var root = doc.DocumentElement;
            if (root.LocalName == "svg") ParseSvgRoot(root, asset);

            var env = new ParseEnv { idTable = idTable, classRules = classRules };
            var rootCtx = Context.Identity.WithElement(root, env);
            var useStack = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode child in root.ChildNodes)
            {
                if (child is XmlElement el)
                    Walk(el, rootCtx, asset, env, useStack);
            }

            return asset;
        }

        // Bundle of immutable per-parse lookups passed through the recursive walk.
        // internal (not private) because it appears in Context.WithElement's
        // signature and Context itself is internal — must match accessibility.
        internal struct ParseEnv
        {
            public Dictionary<string, XmlElement> idTable;
            public Dictionary<string, Dictionary<string, string>> classRules;
        }

        private static void IndexIds(XmlElement el, Dictionary<string, XmlElement> table)
        {
            string id = el.GetAttribute("id");
            if (!string.IsNullOrEmpty(id) && !table.ContainsKey(id)) table[id] = el;
            foreach (XmlNode child in el.ChildNodes)
            {
                if (child is XmlElement c) IndexIds(c, table);
            }
        }

        private static XmlReaderSettings MakeSettings()
        {
            return new XmlReaderSettings
            {
                IgnoreComments = true,
                // <style> needs its text/CDATA content preserved; whitespace
                // outside of text content is harmless for our attribute-driven
                // walk, so we no longer ignore it.
                IgnoreWhitespace = false,
                IgnoreProcessingInstructions = true,
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            };
        }

        private static void Walk(XmlElement el, Context parentCtx, VMGShapeAsset asset,
                                 ParseEnv env, HashSet<string> useStack)
        {
            string local = el.LocalName;

            // <defs> and <symbol> are definition-only containers. Their children
            // are not drawn directly; they're only emitted via <use> references.
            // <style> blocks were already harvested in the pre-pass.
            if (local == "defs" || local == "symbol" || local == "style") return;

            if (local == "use")
            {
                ExpandUse(el, parentCtx, asset, env, useStack);
                return;
            }

            var ctx = parentCtx.WithElement(el, env);

            switch (local)
            {
                case "path": ConvertPath(el, ctx, asset); return;
                case "rect": ConvertRect(el, ctx, asset); return;
                case "circle": ConvertCircle(el, ctx, asset); return;
                case "ellipse": ConvertEllipse(el, ctx, asset); return;
                case "line": ConvertLine(el, ctx, asset); return;
                case "polyline": ConvertPoly(el, ctx, asset, false); return;
                case "polygon": ConvertPoly(el, ctx, asset, true); return;
            }

            // Containers: <g>, nested <svg>, anything else with children.
            foreach (XmlNode child in el.ChildNodes)
            {
                if (child is XmlElement c) Walk(c, ctx, asset, env, useStack);
            }
        }

        private static void ExpandUse(XmlElement useEl, Context parentCtx, VMGShapeAsset asset,
                                       ParseEnv env, HashSet<string> useStack)
        {
            string href = useEl.GetAttribute("href");
            if (string.IsNullOrEmpty(href)) href = useEl.GetAttribute("xlink:href");
            // XmlDocument may not resolve the xlink namespace prefix lookup, fall back:
            if (string.IsNullOrEmpty(href))
            {
                foreach (XmlAttribute a in useEl.Attributes)
                {
                    if (a.LocalName == "href") { href = a.Value; break; }
                }
            }
            if (string.IsNullOrEmpty(href) || href[0] != '#')
            {
                Debug.LogWarning($"[VMG SVG] <use> with unsupported href '{href}' skipped (only same-document #id refs).");
                return;
            }
            string id = href.Substring(1);
            if (!env.idTable.TryGetValue(id, out XmlElement target))
            {
                Debug.LogWarning($"[VMG SVG] <use href=\"#{id}\"> target not found, skipped.");
                return;
            }
            if (useStack.Contains(id))
            {
                Debug.LogWarning($"[VMG SVG] <use href=\"#{id}\"> cycle detected, skipped.");
                return;
            }

            // Build the use site's effective context: parent + use's own
            // transform/style + the x/y offset translated into a transform.
            var ctx = parentCtx.WithElement(useEl, env);
            float ux = ReadFloatAttr(useEl, "x", 0f);
            float uy = ReadFloatAttr(useEl, "y", 0f);
            if (ux != 0f || uy != 0f)
            {
                var t = Matrix2D.Translate(ux, uy);
                ctx.matrix = Matrix2D.Multiply(ctx.matrix, t);
            }
            // width/height on <use> only meaningful for <svg>/<symbol> targets;
            // intentionally ignored in this version.

            useStack.Add(id);
            try
            {
                // If the target is <symbol> or <svg>, its children are what's
                // instanced. Otherwise the target element itself is instanced.
                if (target.LocalName == "symbol" || target.LocalName == "svg")
                {
                    foreach (XmlNode child in target.ChildNodes)
                    {
                        if (child is XmlElement c) Walk(c, ctx, asset, env, useStack);
                    }
                }
                else
                {
                    Walk(target, ctx, asset, env, useStack);
                }
            }
            finally
            {
                useStack.Remove(id);
            }
        }

        // ---------------- <style> harvesting ----------------

        private static void CollectStyleRules(XmlElement el, Dictionary<string, Dictionary<string, string>> rules)
        {
            if (el.LocalName == "style")
            {
                ParseCssBlock(el.InnerText, rules);
                return; // <style> contents aren't walked further
            }
            foreach (XmlNode child in el.ChildNodes)
            {
                if (child is XmlElement c) CollectStyleRules(c, rules);
            }
        }

        // Tiny CSS parser. Only `.class` selectors. Supports comma-separated
        // selector lists (`.a, .b { ... }`) and `/* */` comments. Compound
        // selectors (`.a.b`), descendant combinators, attribute selectors,
        // pseudo-classes, and @-rules are all ignored — those selectors are
        // simply skipped, but their declaration blocks still parse and attach
        // to any plain `.class` selectors present in the same comma list.
        private static void ParseCssBlock(string css, Dictionary<string, Dictionary<string, string>> rules)
        {
            if (string.IsNullOrEmpty(css)) return;

            // Strip /* ... */ comments cheaply.
            css = StripCssComments(css);

            int i = 0;
            while (i < css.Length)
            {
                // Skip whitespace.
                while (i < css.Length && char.IsWhiteSpace(css[i])) i++;
                if (i >= css.Length) break;

                // Skip @-rules (e.g. @media, @keyframes) — find matching '}' and continue.
                if (css[i] == '@')
                {
                    int depth = 0;
                    while (i < css.Length)
                    {
                        char ch = css[i++];
                        if (ch == '{') depth++;
                        else if (ch == '}') { depth--; if (depth <= 0) break; }
                        else if (ch == ';' && depth == 0) break; // @import-style single line
                    }
                    continue;
                }

                // Read selector list up to '{'.
                int selStart = i;
                while (i < css.Length && css[i] != '{') i++;
                if (i >= css.Length) break;
                string selectorList = css.Substring(selStart, i - selStart);
                i++; // past '{'

                // Read declaration block up to '}'.
                int blockStart = i;
                while (i < css.Length && css[i] != '}') i++;
                string block = css.Substring(blockStart, i - blockStart);
                if (i < css.Length) i++; // past '}'

                // Parse declarations once, attach to every plain class in selector list.
                var decls = ParseDeclarations(block);
                if (decls.Count == 0) continue;

                foreach (var sel in selectorList.Split(','))
                {
                    string s = sel.Trim();
                    // Accept only the simplest case: a single ".name" token.
                    if (s.Length < 2 || s[0] != '.') continue;
                    bool simple = true;
                    for (int k = 1; k < s.Length; k++)
                    {
                        char ch = s[k];
                        if (!(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')) { simple = false; break; }
                    }
                    if (!simple) continue;
                    string cls = s.Substring(1);
                    if (!rules.TryGetValue(cls, out var existing))
                    {
                        rules[cls] = new Dictionary<string, string>(decls, StringComparer.Ordinal);
                    }
                    else
                    {
                        // Later rules override earlier ones for the same property.
                        foreach (var kv in decls) existing[kv.Key] = kv.Value;
                    }
                }
            }
        }

        private static string StripCssComments(string css)
        {
            int idx = css.IndexOf("/*", StringComparison.Ordinal);
            if (idx < 0) return css;
            var sb = new System.Text.StringBuilder(css.Length);
            int start = 0;
            while (idx >= 0)
            {
                sb.Append(css, start, idx - start);
                int end = css.IndexOf("*/", idx + 2, StringComparison.Ordinal);
                if (end < 0) return sb.ToString(); // unterminated, drop the rest
                start = end + 2;
                idx = css.IndexOf("/*", start, StringComparison.Ordinal);
            }
            sb.Append(css, start, css.Length - start);
            return sb.ToString();
        }

        private static Dictionary<string, string> ParseDeclarations(string block)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in block.Split(';'))
            {
                int colon = prop.IndexOf(':');
                if (colon <= 0) continue;
                string k = prop.Substring(0, colon).Trim();
                string v = prop.Substring(colon + 1).Trim();
                if (k.Length == 0 || v.Length == 0) continue;
                // Strip a trailing "!important" — we don't model specificity but
                // the value itself shouldn't include it.
                int bang = v.IndexOf('!');
                if (bang >= 0) v = v.Substring(0, bang).TrimEnd();
                dict[k] = v;
            }
            return dict;
        }

        private static void ParseSvgRoot(XmlElement el, VMGShapeAsset asset)
        {
            string vb = el.GetAttribute("viewBox");
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
            string ws = el.GetAttribute("width");
            string hs = el.GetAttribute("height");
            if (TryParseLength(ws, out float ww) && TryParseLength(hs, out float hh))
            {
                asset.viewBoxSize = new Vector2(ww, hh);
            }
        }

        private static bool TryParseLength(string s, out float v)
        {
            v = 0f;
            if (string.IsNullOrEmpty(s)) return false;
            int end = 0;
            while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.' || s[end] == '-' || s[end] == '+' || s[end] == 'e' || s[end] == 'E')) end++;
            return float.TryParse(s.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        // ---------------- per-element handlers ----------------

        private static void ConvertPath(XmlElement el, Context ctx, VMGShapeAsset asset)
        {
            string d = el.GetAttribute("d");
            if (string.IsNullOrEmpty(d)) return;
            var subs = SvgPathParser.Parse(d);
            foreach (var sub in subs)
            {
                var shape = NewShape(el, ctx);
                shape.closed = sub.closed;
                ApplyTransform(sub.nodes, ctx.matrix);
                shape.nodes.AddRange(sub.nodes);
                if (HasContent(shape)) asset.subShapes.Add(shape);
            }
        }

        private static void ConvertRect(XmlElement el, Context ctx, VMGShapeAsset asset)
        {
            float x = ReadFloatAttr(el, "x", 0f);
            float y = ReadFloatAttr(el, "y", 0f);
            float w = ReadFloatAttr(el, "width", 0f);
            float h = ReadFloatAttr(el, "height", 0f);
            if (w <= 0f || h <= 0f) return;
            float rx = ReadFloatAttr(el, "rx", 0f);
            float ry = ReadFloatAttr(el, "ry", rx);
            if (rx <= 0f && ry > 0f) rx = ry;
            if (ry <= 0f && rx > 0f) ry = rx;

            var nodes = new List<VectorNode>();
            if (rx > 0f && ry > 0f)
            {
                rx = Mathf.Min(rx, w * 0.5f);
                ry = Mathf.Min(ry, h * 0.5f);
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

            var shape = NewShape(el, ctx);
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

        private static void ConvertCircle(XmlElement el, Context ctx, VMGShapeAsset asset)
        {
            float cx = ReadFloatAttr(el, "cx", 0f);
            float cy = ReadFloatAttr(el, "cy", 0f);
            float r = ReadFloatAttr(el, "r", 0f);
            if (r <= 0f) return;
            EmitEllipse(el, ctx, asset, cx, cy, r, r);
        }

        private static void ConvertEllipse(XmlElement el, Context ctx, VMGShapeAsset asset)
        {
            float cx = ReadFloatAttr(el, "cx", 0f);
            float cy = ReadFloatAttr(el, "cy", 0f);
            float rx = ReadFloatAttr(el, "rx", 0f);
            float ry = ReadFloatAttr(el, "ry", 0f);
            if (rx <= 0f || ry <= 0f) return;
            EmitEllipse(el, ctx, asset, cx, cy, rx, ry);
        }

        private static void EmitEllipse(XmlElement el, Context ctx, VMGShapeAsset asset,
                                        float cx, float cy, float rx, float ry)
        {
            const float k = 0.5522847498f;
            float kx = rx * k, ky = ry * k;
            var nodes = new List<VectorNode>(5);
            Vector2 top = new Vector2(cx, cy - ry);
            Vector2 right = new Vector2(cx + rx, cy);
            Vector2 bot = new Vector2(cx, cy + ry);
            Vector2 left = new Vector2(cx - rx, cy);
            nodes.Add(VectorNode.Corner(top));
            AddCubicSegment(nodes, top, right, new Vector2(cx + kx, cy - ry), new Vector2(cx + rx, cy - ky));
            AddCubicSegment(nodes, right, bot, new Vector2(cx + rx, cy + ky), new Vector2(cx + kx, cy + ry));
            AddCubicSegment(nodes, bot, left, new Vector2(cx - kx, cy + ry), new Vector2(cx - rx, cy + ky));
            AddCubicSegment(nodes, left, top, new Vector2(cx - rx, cy - ky), new Vector2(cx - kx, cy - ry));
            if (nodes.Count > 0 && nodes[nodes.Count - 1].position == nodes[0].position)
            {
                var first = nodes[0];
                first.inTangent = nodes[nodes.Count - 1].inTangent;
                first.type = NodeType.Bezier;
                nodes[0] = first;
                nodes.RemoveAt(nodes.Count - 1);
            }
            ApplyTransform(nodes, ctx.matrix);
            var shape = NewShape(el, ctx);
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

        private static void ConvertLine(XmlElement el, Context ctx, VMGShapeAsset asset)
        {
            float x1 = ReadFloatAttr(el, "x1", 0f);
            float y1 = ReadFloatAttr(el, "y1", 0f);
            float x2 = ReadFloatAttr(el, "x2", 0f);
            float y2 = ReadFloatAttr(el, "y2", 0f);
            var nodes = new List<VectorNode>(2)
            {
                VectorNode.Corner(new Vector2(x1, y1)),
                VectorNode.Corner(new Vector2(x2, y2)),
            };
            ApplyTransform(nodes, ctx.matrix);
            var shape = NewShape(el, ctx);
            shape.closed = false;
            shape.nodes.AddRange(nodes);
            if (HasContent(shape)) asset.subShapes.Add(shape);
        }

        private static void ConvertPoly(XmlElement el, Context ctx, VMGShapeAsset asset, bool closed)
        {
            string pts = el.GetAttribute("points");
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
            var shape = NewShape(el, ctx);
            shape.closed = closed;
            shape.nodes.AddRange(nodes);
            if (HasContent(shape)) asset.subShapes.Add(shape);
        }

        // ---------------- styling ----------------

        private static VMGSubShape NewShape(XmlElement el, Context ctx)
        {
            var s = new VMGSubShape { id = el.GetAttribute("id") };
            s.fill = ctx.fill;
            s.stroke = ctx.stroke;
            return s;
        }

        private static bool HasContent(VMGSubShape s)
        {
            return s.nodes.Count >= 2 && (s.fill.enabled || s.stroke.enabled);
        }

        private static float ReadFloatAttr(XmlElement el, string attr, float fallback)
        {
            string v = el.GetAttribute(attr);
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
                fill = new FillStyle { enabled = true, color = Color.black },
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

            public Context WithElement(XmlElement el, ParseEnv env)
            {
                var c = this;
                string tr = el.GetAttribute("transform");
                if (!string.IsNullOrEmpty(tr))
                {
                    var local = SvgTransformParser.Parse(tr);
                    c.matrix = Matrix2D.Multiply(c.matrix, local);
                }
                ApplyStyle(el, env, ref c);
                return c;
            }
        }

        private static void ApplyStyle(XmlElement el, ParseEnv env, ref Context c)
        {
            // Precedence (low → high): inherited (already in c) → class rules
            // → presentation attribute → inline `style="..."`.
            string fillAttr = null, fillOpAttr = null, strokeAttr = null,
                   strokeOpAttr = null, strokeWAttr = null,
                   capAttr = null, joinAttr = null, opAttr = null;

            // Class rules (lowest of the locals).
            string classAttr = el.GetAttribute("class");
            if (!string.IsNullOrEmpty(classAttr) && env.classRules != null && env.classRules.Count > 0)
            {
                foreach (var cls in classAttr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!env.classRules.TryGetValue(cls, out var decls)) continue;
                    if (decls.TryGetValue("fill", out var v0)) fillAttr = v0;
                    if (decls.TryGetValue("fill-opacity", out var v1)) fillOpAttr = v1;
                    if (decls.TryGetValue("stroke", out var v2)) strokeAttr = v2;
                    if (decls.TryGetValue("stroke-opacity", out var v3)) strokeOpAttr = v3;
                    if (decls.TryGetValue("stroke-width", out var v4)) strokeWAttr = v4;
                    if (decls.TryGetValue("stroke-linecap", out var v5)) capAttr = v5;
                    if (decls.TryGetValue("stroke-linejoin", out var v6)) joinAttr = v6;
                    if (decls.TryGetValue("opacity", out var v7)) opAttr = v7;
                }
            }

            // Presentation attributes override class rules.
            string attr;
            if ((attr = GetAttrOrNull(el, "fill")) != null) fillAttr = attr;
            if ((attr = GetAttrOrNull(el, "fill-opacity")) != null) fillOpAttr = attr;
            if ((attr = GetAttrOrNull(el, "stroke")) != null) strokeAttr = attr;
            if ((attr = GetAttrOrNull(el, "stroke-opacity")) != null) strokeOpAttr = attr;
            if ((attr = GetAttrOrNull(el, "stroke-width")) != null) strokeWAttr = attr;
            if ((attr = GetAttrOrNull(el, "stroke-linecap")) != null) capAttr = attr;
            if ((attr = GetAttrOrNull(el, "stroke-linejoin")) != null) joinAttr = attr;
            if ((attr = GetAttrOrNull(el, "opacity")) != null) opAttr = attr;

            // Inline style overrides everything else.
            string style = el.GetAttribute("style");
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

        private static string GetAttrOrNull(XmlElement el, string name)
        {
            return el.HasAttribute(name) ? el.GetAttribute(name) : null;
        }

        private static bool TryParseFloat(string s, out float v)
        {
            v = 0f;
            return !string.IsNullOrEmpty(s)
                   && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
