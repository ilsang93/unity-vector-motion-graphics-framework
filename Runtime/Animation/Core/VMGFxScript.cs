using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using VMG.Animation;
using VMG.Core;

namespace VMG.Animation.Core
{
    // VMGFx DSL — flat statement list that mirrors VMGFx.Scene / VMGFx.Animate
    // / VMGFx.Timeline calls one-to-one. Authored as plain text (TextAsset)
    // and compiled by VMGAnimator at OnEnable.
    //
    // Statement keywords:
    //   add <name> <shape> [key=value ...]
    //   group <name> { ... }
    //   animate <target> <path> -> <value> [key=value ...]
    //   timeline { ... }
    //     - inside timeline: animate / set / call / label, all sequenced
    //   set <target> <path> = <value> [at=pos]      (timeline-only)
    //   call <eventName> [at=pos]                   (timeline-only)
    //   label <name> [at=pos]                       (timeline-only)
    //
    // Comments: // to end of line.
    // Statement terminator: newline (or ; optional).
    // Values: numbers (1, 1.5), tuples (1,2 or 1,2,3), hex colours (#fff,
    // #ffffff), named colours (red, cyan, ...), bareword identifiers,
    // quoted strings ("..."), booleans (true/false).
    //
    // Attribute keys recognised:
    //   add:       size, pos, position, rotation, fill, stroke, trim,
    //              roundCorner, fitToRect, sides, corner, cornerRadius,
    //              points, closed
    //   animate:   duration, ease, at, delay, endDelay, loop, alternate, from
    //   set/call/label: at
    public static class VMGFxScript
    {
        public static VMGFxCompiled Compile(string source, Transform root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var compiled = new VMGFxCompiled();
            if (string.IsNullOrEmpty(source)) return compiled;

            var tokens = Tokenizer.Tokenize(source);
            var statements = Parser.Parse(tokens);
            Compiler.Build(statements, root, compiled);
            return compiled;
        }

        // ----- Tokenizer -----

        enum TokKind
        {
            Ident,        // bareword (keyword / name / hex colour / quoted-removed)
            Number,       // numeric literal
            String,       // quoted string
            Symbol,       // single-char punctuation: { } , = ; ->  (arrow is multi-char but tokenised whole)
            Arrow,        // ->
            Newline,
            EOF,
        }

        struct Tok
        {
            public TokKind kind;
            public string text;
            public int line;
        }

        static class Tokenizer
        {
            public static List<Tok> Tokenize(string src)
            {
                var list = new List<Tok>();
                int i = 0;
                int line = 1;
                // After we emit '=' or '->', the very next token is the value
                // of that attribute / animate target — it can be a tuple
                // (120,0), a hex colour (#fff), a position string (+=0.2), or
                // any bareword that mixes punctuation we'd otherwise treat as
                // separate tokens. We capture it as a single Ident token by
                // reading until the next whitespace / newline / ';' / '}'.
                bool valueMode = false;
                while (i < src.Length)
                {
                    char c = src[i];

                    // line comment
                    if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
                    {
                        while (i < src.Length && src[i] != '\n') i++;
                        continue;
                    }

                    if (c == '\n') { list.Add(new Tok { kind = TokKind.Newline, line = line }); line++; i++; valueMode = false; continue; }
                    if (c == '\r') { i++; continue; }
                    if (c == ' ' || c == '\t') { i++; continue; }

                    if (valueMode)
                    {
                        // Read until whitespace or a statement-terminating
                        // punct. '{' / '}' / ';' end the value; ',' is part of
                        // tuple values so it's allowed inside.
                        int start = i;
                        while (i < src.Length)
                        {
                            char vc = src[i];
                            if (vc == ' ' || vc == '\t' || vc == '\n' || vc == '\r') break;
                            if (vc == ';' || vc == '{' || vc == '}') break;
                            // '//' inside a value ends the value (line comment).
                            if (vc == '/' && i + 1 < src.Length && src[i + 1] == '/') break;
                            i++;
                        }
                        if (i > start)
                            list.Add(new Tok { kind = TokKind.Ident, text = src.Substring(start, i - start), line = line });
                        valueMode = false;
                        continue;
                    }

                    // arrow ->
                    if (c == '-' && i + 1 < src.Length && src[i + 1] == '>')
                    {
                        list.Add(new Tok { kind = TokKind.Arrow, text = "->", line = line });
                        i += 2;
                        valueMode = true;
                        continue;
                    }

                    // single-char punctuation
                    if (c == '{' || c == '}' || c == ',' || c == '=' || c == ';')
                    {
                        list.Add(new Tok { kind = TokKind.Symbol, text = c.ToString(), line = line });
                        i++;
                        if (c == '=') valueMode = true;
                        continue;
                    }

                    // quoted string
                    if (c == '"')
                    {
                        var sb = new StringBuilder();
                        i++;
                        while (i < src.Length && src[i] != '"')
                        {
                            if (src[i] == '\\' && i + 1 < src.Length)
                            {
                                char esc = src[i + 1];
                                switch (esc)
                                {
                                    case 'n': sb.Append('\n'); break;
                                    case 't': sb.Append('\t'); break;
                                    case '\\': sb.Append('\\'); break;
                                    case '"': sb.Append('"'); break;
                                    default: sb.Append(esc); break;
                                }
                                i += 2;
                                continue;
                            }
                            if (src[i] == '\n') line++;
                            sb.Append(src[i]);
                            i++;
                        }
                        if (i < src.Length) i++;
                        list.Add(new Tok { kind = TokKind.String, text = sb.ToString(), line = line });
                        continue;
                    }

                    // number (including leading - or +, and decimal). To avoid
                    // gobbling '-' that's part of a different construct (we
                    // already handled '->'), only treat -/+ as number-start if
                    // the next char is a digit or '.'.
                    if (IsDigit(c) || ((c == '-' || c == '+') && i + 1 < src.Length && (IsDigit(src[i + 1]) || src[i + 1] == '.')))
                    {
                        int start = i;
                        if (c == '-' || c == '+') i++;
                        while (i < src.Length && (IsDigit(src[i]) || src[i] == '.')) i++;
                        list.Add(new Tok { kind = TokKind.Number, text = src.Substring(start, i - start), line = line });
                        continue;
                    }

                    // bareword / identifier — letters, digits, _ . # / + < (for at-position strings)
                    // We let any non-whitespace non-punctuation sequence form an identifier; the
                    // parser interprets context. This makes hex colours (#fff), paths (m_Trim.end),
                    // and position strings (+=0.2, <<) all single tokens.
                    if (IsIdentStart(c))
                    {
                        int start = i;
                        while (i < src.Length && IsIdentBody(src[i])) i++;
                        list.Add(new Tok { kind = TokKind.Ident, text = src.Substring(start, i - start), line = line });
                        continue;
                    }

                    // Unknown char — skip with warning.
                    Debug.LogWarning($"[VMGFx] unexpected char '{c}' at line {line}");
                    i++;
                }
                list.Add(new Tok { kind = TokKind.EOF, line = line });
                return list;
            }

            static bool IsDigit(char c) => c >= '0' && c <= '9';

            static bool IsIdentStart(char c)
            {
                if (c >= 'a' && c <= 'z') return true;
                if (c >= 'A' && c <= 'Z') return true;
                if (c == '_' || c == '#' || c == '.' || c == '/' || c == '<' || c == '+') return true;
                return false;
            }

            static bool IsIdentBody(char c)
            {
                if (c >= 'a' && c <= 'z') return true;
                if (c >= 'A' && c <= 'Z') return true;
                if (c >= '0' && c <= '9') return true;
                // '=' is punctuation only — including it here would glue
                // 'size=200' into a single ident token. Same reason '-' and
                // '+' must NOT be in ident bodies: they're only legal at the
                // start ('+=N', '<', '<<').
                if (c == '_' || c == '.' || c == '/' || c == '#' || c == '<') return true;
                return false;
            }
        }

        // ----- AST -----

        internal abstract class Stmt
        {
            public int line;
        }

        internal sealed class AddStmt : Stmt
        {
            public string name;
            public string shape;
            public Dictionary<string, string> attrs = new Dictionary<string, string>();
        }

        internal sealed class GroupStmt : Stmt
        {
            public string name;
            public List<Stmt> children = new List<Stmt>();
        }

        internal sealed class AnimateStmt : Stmt
        {
            public string target;
            public string path;
            public string toValue;
            public Dictionary<string, string> attrs = new Dictionary<string, string>();
        }

        internal sealed class TimelineStmt : Stmt
        {
            public List<Stmt> children = new List<Stmt>();
        }

        internal sealed class SetStmt : Stmt
        {
            public string target;
            public string path;
            public string value;
            public Dictionary<string, string> attrs = new Dictionary<string, string>();
        }

        internal sealed class CallStmt : Stmt
        {
            public string eventName;
            public Dictionary<string, string> attrs = new Dictionary<string, string>();
        }

        internal sealed class LabelStmt : Stmt
        {
            public string name;
            public Dictionary<string, string> attrs = new Dictionary<string, string>();
        }

        // ----- Parser -----

        static class Parser
        {
            public static List<Stmt> Parse(List<Tok> tokens)
            {
                var p = new ParseState(tokens);
                var stmts = new List<Stmt>();
                while (!p.AtEnd)
                {
                    p.SkipNewlines();
                    if (p.AtEnd) break;
                    var s = ParseStatement(p, insideTimeline: false);
                    if (s != null) stmts.Add(s);
                }
                return stmts;
            }

            static Stmt ParseStatement(ParseState p, bool insideTimeline)
            {
                var t = p.Peek();
                if (t.kind == TokKind.Symbol && t.text == "}") return null;

                if (t.kind != TokKind.Ident)
                {
                    p.Error($"expected statement keyword, got '{t.text}'");
                    p.SkipToNewline();
                    return null;
                }

                switch (t.text)
                {
                    case "add": return ParseAdd(p);
                    case "group": return ParseGroup(p);
                    case "animate": return ParseAnimate(p);
                    case "timeline": return ParseTimeline(p);
                    case "set": if (insideTimeline) return ParseSet(p); break;
                    case "call": if (insideTimeline) return ParseCall(p); break;
                    case "label": if (insideTimeline) return ParseLabel(p); break;
                }
                p.Error($"unknown statement '{t.text}'");
                p.SkipToNewline();
                return null;
            }

            static AddStmt ParseAdd(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'add'
                string name = p.ExpectIdent("add name");
                string shape = p.ExpectIdent("add shape");
                var attrs = ParseAttributes(p);
                p.EndStatement();
                return new AddStmt { line = line, name = name, shape = shape, attrs = attrs };
            }

            static GroupStmt ParseGroup(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'group'
                string name = p.ExpectIdent("group name");
                p.ExpectSymbol("{");
                var children = new List<Stmt>();
                p.SkipNewlines();
                while (!p.AtEnd)
                {
                    var t = p.Peek();
                    if (t.kind == TokKind.Symbol && t.text == "}") break;
                    var c = ParseStatement(p, insideTimeline: false);
                    if (c != null) children.Add(c);
                    p.SkipNewlines();
                }
                p.ExpectSymbol("}");
                p.EndStatement();
                return new GroupStmt { line = line, name = name, children = children };
            }

            static AnimateStmt ParseAnimate(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'animate'
                string target = p.ExpectIdent("animate target");
                string path = p.ExpectIdent("animate path");
                p.ExpectArrow();
                string toValue = ParseValue(p);
                var attrs = ParseAttributes(p);
                p.EndStatement();
                return new AnimateStmt { line = line, target = target, path = path, toValue = toValue, attrs = attrs };
            }

            static TimelineStmt ParseTimeline(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'timeline'
                p.ExpectSymbol("{");
                var children = new List<Stmt>();
                p.SkipNewlines();
                while (!p.AtEnd)
                {
                    var t = p.Peek();
                    if (t.kind == TokKind.Symbol && t.text == "}") break;
                    var c = ParseStatement(p, insideTimeline: true);
                    if (c != null) children.Add(c);
                    p.SkipNewlines();
                }
                p.ExpectSymbol("}");
                p.EndStatement();
                return new TimelineStmt { line = line, children = children };
            }

            static SetStmt ParseSet(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'set'
                string target = p.ExpectIdent("set target");
                string path = p.ExpectIdent("set path");
                p.ExpectSymbol("=");
                string value = ParseValue(p);
                var attrs = ParseAttributes(p);
                p.EndStatement();
                return new SetStmt { line = line, target = target, path = path, value = value, attrs = attrs };
            }

            static CallStmt ParseCall(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'call'
                string evName = p.ExpectIdent("call event name");
                var attrs = ParseAttributes(p);
                p.EndStatement();
                return new CallStmt { line = line, eventName = evName, attrs = attrs };
            }

            static LabelStmt ParseLabel(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'label'
                string name = p.ExpectIdent("label name");
                var attrs = ParseAttributes(p);
                p.EndStatement();
                return new LabelStmt { line = line, name = name, attrs = attrs };
            }

            // The tokenizer's value-mode emits the entire RHS of '=' or '->'
            // as a single Ident token (whitespace-terminated), so ParseValue
            // is just one atom.
            static string ParseValue(ParseState p)
            {
                var t = p.Peek();
                if (t.kind == TokKind.Ident || t.kind == TokKind.Number || t.kind == TokKind.String)
                {
                    p.Consume();
                    return t.text;
                }
                p.Error($"expected value, got '{t.text}'");
                return "";
            }

            // key=value attribute list. value may be a tuple (comma-separated
            // atoms). Stops at newline / EOF / closing brace.
            static Dictionary<string, string> ParseAttributes(ParseState p)
            {
                var attrs = new Dictionary<string, string>();
                while (true)
                {
                    var t = p.Peek();
                    if (t.kind == TokKind.Newline || t.kind == TokKind.EOF) break;
                    if (t.kind == TokKind.Symbol && (t.text == "}" || t.text == ";")) break;

                    if (t.kind != TokKind.Ident)
                    {
                        p.Error($"expected attribute key, got '{t.text}'");
                        p.SkipToNewline();
                        break;
                    }

                    string key = t.text;
                    p.Consume();

                    var eq = p.Peek();
                    if (eq.kind != TokKind.Symbol || eq.text != "=")
                    {
                        p.Error($"expected '=' after attribute key '{key}', got '{eq.text}'");
                        p.SkipToNewline();
                        break;
                    }
                    p.Consume(); // '='

                    string value = ParseValue(p);
                    attrs[key] = value;
                }
                return attrs;
            }
        }

        sealed class ParseState
        {
            readonly List<Tok> m_Tokens;
            int m_Pos;
            public ParseState(List<Tok> toks) { m_Tokens = toks; }

            public bool AtEnd => m_Tokens[m_Pos].kind == TokKind.EOF;
            public Tok Peek() => m_Tokens[m_Pos];
            public Tok Consume() => m_Tokens[m_Pos++];

            public void SkipNewlines()
            {
                while (m_Pos < m_Tokens.Count)
                {
                    var t = m_Tokens[m_Pos];
                    if (t.kind == TokKind.Newline) { m_Pos++; continue; }
                    if (t.kind == TokKind.Symbol && t.text == ";") { m_Pos++; continue; }
                    break;
                }
            }

            public void SkipToNewline()
            {
                while (m_Pos < m_Tokens.Count)
                {
                    var t = m_Tokens[m_Pos];
                    if (t.kind == TokKind.Newline || t.kind == TokKind.EOF) return;
                    m_Pos++;
                }
            }

            public void EndStatement()
            {
                // Consume an optional ';' then expect a newline (or EOF / '}').
                var t = m_Tokens[m_Pos];
                if (t.kind == TokKind.Symbol && t.text == ";") { m_Pos++; }
            }

            public string ExpectIdent(string desc)
            {
                var t = m_Tokens[m_Pos];
                if (t.kind == TokKind.Ident || t.kind == TokKind.Number || t.kind == TokKind.String)
                {
                    m_Pos++;
                    return t.text;
                }
                Error($"expected {desc}, got '{t.text}'");
                return "";
            }

            public void ExpectSymbol(string text)
            {
                var t = m_Tokens[m_Pos];
                if (t.kind == TokKind.Symbol && t.text == text) { m_Pos++; return; }
                Error($"expected '{text}', got '{t.text}'");
            }

            public void ExpectArrow()
            {
                var t = m_Tokens[m_Pos];
                if (t.kind == TokKind.Arrow) { m_Pos++; return; }
                Error($"expected '->', got '{t.text}'");
            }

            public void Error(string msg)
            {
                int line = m_Pos < m_Tokens.Count ? m_Tokens[m_Pos].line : -1;
                Debug.LogError($"[VMGFx] parse error at line {line}: {msg}");
            }
        }

        // ----- Compiler -----

        static class Compiler
        {
            public static void Build(List<Stmt> statements, Transform root, VMGFxCompiled compiled)
            {
                var scene = VMGFx.Scene(root);
                compiled.scene = scene;

                // Pass 1: hierarchy (add / group).
                foreach (var s in statements)
                {
                    if (s is AddStmt addS) ApplyAdd(addS, scene);
                    else if (s is GroupStmt grpS) ApplyGroup(grpS, scene);
                }

                // Pass 2: animations / timelines. We resolve names via the
                // scene's child lookup; if a target isn't in the scene we fall
                // back to looking up a child Transform under root.
                foreach (var s in statements)
                {
                    if (s is AnimateStmt anim) BuildStandaloneAnimate(anim, scene, root, compiled);
                    else if (s is TimelineStmt tl) BuildTimeline(tl, scene, root, compiled);
                }
            }

            static void ApplyAdd(AddStmt s, VMGScene scene)
            {
                var d = MakeDescriptor(s.shape, s.attrs);
                if (d == null) return;
                ApplyDescriptorAttrs(d, s.attrs);
                scene.Add(s.name, d);
            }

            static void ApplyGroup(GroupStmt s, VMGScene scene)
            {
                scene.Group(s.name, sub =>
                {
                    foreach (var c in s.children)
                    {
                        if (c is AddStmt addS) ApplyAdd(addS, sub);
                        else if (c is GroupStmt grpS) ApplyGroup(grpS, sub);
                    }
                });
            }

            static VMGShapeDescriptor MakeDescriptor(string shape, Dictionary<string, string> attrs)
            {
                switch (shape)
                {
                    case "circle": return VMGFx.Circle();
                    case "ellipse": return VMGFx.Ellipse();
                    case "rectangle":
                    case "rect": return VMGFx.Rectangle();
                    case "roundedRect":
                    case "roundedRectangle":
                    {
                        var d = VMGFx.RoundedRectangle();
                        if (attrs != null && (attrs.TryGetValue("cornerRadius", out var cr) || attrs.TryGetValue("corner", out cr)))
                        {
                            if (TryParseFloat(cr, out var v)) d.CornerRadius(v);
                        }
                        return d;
                    }
                    case "polygon":
                    {
                        var d = VMGFx.Polygon();
                        if (attrs != null && attrs.TryGetValue("sides", out var sides) && TryParseInt(sides, out var n))
                            d.Sides(n);
                        return d;
                    }
                    case "path":
                    {
                        var d = VMGFx.Path();
                        if (attrs != null && attrs.TryGetValue("points", out var pts))
                        {
                            var parts = pts.Split(',');
                            var list = new List<Vector2>();
                            for (int i = 0; i + 1 < parts.Length; i += 2)
                            {
                                if (TryParseFloat(parts[i], out var x) && TryParseFloat(parts[i + 1], out var y))
                                    list.Add(new Vector2(x, y));
                            }
                            d.Points(list.ToArray());
                        }
                        if (attrs != null && attrs.TryGetValue("closed", out var closed))
                            d.Closed(ParseBool(closed));
                        return d;
                    }
                }
                Debug.LogError($"[VMGFx] unknown shape '{shape}'");
                return null;
            }

            static void ApplyDescriptorAttrs(VMGShapeDescriptor d, Dictionary<string, string> attrs)
            {
                if (attrs == null) return;
                foreach (var kv in attrs)
                {
                    switch (kv.Key)
                    {
                        case "size":
                        {
                            if (TryParseVector2(kv.Value, out var v)) ApplySize(d, v);
                            else if (TryParseFloat(kv.Value, out var f)) ApplySize(d, new Vector2(f, f));
                            break;
                        }
                        case "pos":
                        case "position":
                        {
                            if (TryParseVector2(kv.Value, out var v)) ApplyPosition(d, v);
                            break;
                        }
                        case "rotation":
                        {
                            if (TryParseFloat(kv.Value, out var f)) ApplyRotation(d, f);
                            break;
                        }
                        case "fill":
                        {
                            if (TryParseColor(kv.Value, out var c)) ApplyFill(d, c);
                            break;
                        }
                        case "stroke":
                        {
                            // expected: color,width  (e.g. #fff,4)
                            var parts = SplitTuple(kv.Value);
                            if (parts.Count >= 2 && TryParseColor(parts[0], out var c) && TryParseFloat(parts[1], out var w))
                                ApplyStroke(d, c, w);
                            break;
                        }
                        case "trim":
                        {
                            var parts = SplitTuple(kv.Value);
                            if (parts.Count >= 2 && TryParseFloat(parts[0], out var a) && TryParseFloat(parts[1], out var b))
                                ApplyTrim(d, a, b);
                            break;
                        }
                        case "roundCorner":
                        {
                            if (TryParseFloat(kv.Value, out var r)) ApplyRoundCorner(d, r);
                            break;
                        }
                        case "fitToRect":
                        {
                            ApplyFitToRect(d, ParseBool(kv.Value));
                            break;
                        }
                        // shape-specific keys handled in MakeDescriptor:
                        case "sides":
                        case "corner":
                        case "cornerRadius":
                        case "points":
                        case "closed":
                            break;
                        default:
                            Debug.LogWarning($"[VMGFx] unknown add attribute '{kv.Key}'");
                            break;
                    }
                }
            }

            // The descriptor chain methods are generic; reach them through the
            // base by setting internal fields directly. ApplyXxx wrappers keep
            // the switch above readable.
            static void ApplySize(VMGShapeDescriptor d, Vector2 v)
            {
                d.m_Size = v;
                d.m_Slot0Shape.size = v;
                d.m_HasSize = true;
            }
            static void ApplyPosition(VMGShapeDescriptor d, Vector2 v) { d.m_Position = v; d.m_HasPosition = true; }
            static void ApplyRotation(VMGShapeDescriptor d, float deg) { d.m_RotationDeg = deg; d.m_HasRotation = true; }
            static void ApplyFill(VMGShapeDescriptor d, Color c)
            {
                d.m_Fill.enabled = true;
                d.m_Fill.color = c;
                d.m_HasFill = true;
            }
            static void ApplyStroke(VMGShapeDescriptor d, Color c, float w)
            {
                d.m_Stroke = new VMG.Core.StrokeStyle
                {
                    enabled = true,
                    color = c,
                    width = w,
                    alignment = VMG.Core.StrokeAlignment.Center,
                    cap = VMG.Core.LineCap.Butt,
                    join = VMG.Core.LineJoin.Miter,
                    miterLimit = 8f,
                };
                d.m_HasStroke = true;
            }
            static void ApplyTrim(VMGShapeDescriptor d, float a, float b)
            {
                d.m_Trim.start = a;
                d.m_Trim.end = b;
                d.m_Trim.enabled = true;
                d.m_HasTrim = true;
            }
            static void ApplyRoundCorner(VMGShapeDescriptor d, float r)
            {
                d.m_RoundCorners.radius = r;
                d.m_RoundCorners.enabled = true;
                d.m_HasRoundCorner = true;
            }
            static void ApplyFitToRect(VMGShapeDescriptor d, bool b) { d.m_FitToRect = b; d.m_HasFitToRect = true; }

            // ----- Animate / Timeline -----

            static void BuildStandaloneAnimate(AnimateStmt a, VMGScene scene, Transform root, VMGFxCompiled compiled)
            {
                var target = ResolveTarget(a.target, scene, root, a.path);
                if (target == null) { Debug.LogError($"[VMGFx] animate: target '{a.target}' not found"); return; }
                var builder = VMGFx.Animate(target);
                ConfigureAnimate(builder, a.path, a.toValue, a.attrs, target);
                compiled.standaloneAnimates.Add(builder);
            }

            static void BuildTimeline(TimelineStmt t, VMGScene scene, Transform root, VMGFxCompiled compiled)
            {
                var tl = VMGFx.Timeline();
                compiled.timelines.Add(tl);
                foreach (var c in t.children)
                {
                    switch (c)
                    {
                        case AnimateStmt anim:
                        {
                            var target = ResolveTarget(anim.target, scene, root, anim.path);
                            if (target == null) { Debug.LogError($"[VMGFx] animate: target '{anim.target}' not found"); break; }
                            var builder = VMGFx.Animate(target);
                            ConfigureAnimate(builder, anim.path, anim.toValue, anim.attrs, target);
                            var at = ExtractAt(anim.attrs);
                            tl.Add(builder, at);
                            break;
                        }
                        case SetStmt setS:
                        {
                            var target = ResolveTarget(setS.target, scene, root, setS.path);
                            if (target == null) { Debug.LogError($"[VMGFx] set: target '{setS.target}' not found"); break; }
                            var at = ExtractAt(setS.attrs);
                            ApplyTimelineSet(tl, target, setS.path, setS.value, at);
                            break;
                        }
                        case CallStmt callS:
                        {
                            var at = ExtractAt(callS.attrs);
                            string name = callS.eventName;
                            tl.Call(() => compiled.RaiseEvent(name), at);
                            break;
                        }
                        case LabelStmt labelS:
                        {
                            var at = ExtractAt(labelS.attrs);
                            tl.Label(labelS.name, at);
                            break;
                        }
                    }
                }
            }

            // target syntax:
            //   <name>           → scene child renderer (component on go), with
            //                      automatic Transform fallback if the path
            //                      isn't on the renderer
            //   <name>.transform → child Transform (explicit)
            //   /                → root itself
            //   /.transform      → root Transform
            //
            // The 'path' argument is consulted so 'animate dot localScale' (a
            // Transform path) finds the Transform automatically even though
            // 'dot' resolves to the renderer Component by default. This
            // mirrors anime.js's permissive target lookup.
            static Component ResolveTarget(string targetSpec, VMGScene scene, Transform root, string path)
            {
                if (string.IsNullOrEmpty(targetSpec)) return null;
                bool wantsTransform = targetSpec.EndsWith(".transform");
                string name = wantsTransform ? targetSpec.Substring(0, targetSpec.Length - ".transform".Length) : targetSpec;

                if (name == "/" || name == "" || name == "root")
                {
                    if (wantsTransform) return root;
                    return PickByPath(root, root, path);
                }

                // Resolve the GameObject: scene child first, then a Transform
                // search under root (supports nested 'group/child' paths).
                Component primary = scene[name];
                Transform tr = primary != null ? primary.transform : root.Find(name);
                if (tr == null) return null;
                if (primary == null) primary = tr;

                if (wantsTransform) return tr;
                return PickByPath(primary, tr, path);
            }

            // Choose between the primary renderer component and the Transform
            // by checking which one owns the requested field path. Transform
            // wins only when the primary doesn't resolve the path.
            static Component PickByPath(Component primary, Transform tr, string path)
            {
                if (primary == null) return tr;
                if (string.IsNullOrEmpty(path)) return primary;
                if (VMG.Animation.VMGFieldPathCompiler.TryCompile(primary.GetType(), path, out _, out _))
                    return primary;
                if (tr != null && VMG.Animation.VMGFieldPathCompiler.TryCompile(tr.GetType(), path, out _, out _))
                    return tr;
                return primary;
            }

            static void ConfigureAnimate(VMGAnimate builder, string path, string toValue, Dictionary<string, string> attrs, Component target)
            {
                // Resolve the channel type from the target's field path so we
                // pick the right typed To overload.
                var channelType = ResolveChannelType(target, path);
                bool hasFrom = attrs != null && attrs.ContainsKey("from");

                ApplyTypedTo(builder, path, channelType, toValue, hasFrom ? attrs["from"] : null);

                if (attrs == null) return;
                foreach (var kv in attrs)
                {
                    switch (kv.Key)
                    {
                        case "duration": if (TryParseFloat(kv.Value, out var dur)) builder.Duration(dur); break;
                        case "delay": if (TryParseFloat(kv.Value, out var dl)) builder.Delay(dl); break;
                        case "endDelay": if (TryParseFloat(kv.Value, out var ed)) builder.EndDelay(ed); break;
                        case "ease": builder.Ease(kv.Value); break;
                        case "loop":
                        {
                            if (string.IsNullOrEmpty(kv.Value) || ParseBool(kv.Value)) builder.Loop();
                            else if (TryParseInt(kv.Value, out var ln)) builder.Loop(ln);
                            break;
                        }
                        case "alternate": builder.Alternate(ParseBool(kv.Value)); break;
                        case "at":
                        case "from":
                            break; // handled outside
                        default:
                            Debug.LogWarning($"[VMGFx] unknown animate attribute '{kv.Key}'");
                            break;
                    }
                }
            }

            static VMGChannelType ResolveChannelType(Component target, string path)
            {
                if (target == null) return VMGChannelType.Float;
                if (!VMG.Animation.VMGFieldPathCompiler.TryCompile(target.GetType(), path, out var compiled, out _))
                    return VMGChannelType.Float;
                var leaf = compiled.leafType;
                if (leaf == typeof(float)) return VMGChannelType.Float;
                if (leaf == typeof(int)) return VMGChannelType.Int;
                if (leaf == typeof(bool)) return VMGChannelType.Bool;
                if (leaf == typeof(Color)) return VMGChannelType.Color;
                if (leaf == typeof(Vector2)) return VMGChannelType.Vector2;
                if (leaf == typeof(Vector3)) return VMGChannelType.Vector3;
                if (leaf == typeof(Vector4)) return VMGChannelType.Vector4;
                return VMGChannelType.Float;
            }

            static void ApplyTypedTo(VMGAnimate builder, string path, VMGChannelType type, string toRaw, string fromRaw)
            {
                switch (type)
                {
                    case VMGChannelType.Float:
                    {
                        if (!TryParseFloat(toRaw, out var to)) return;
                        if (fromRaw != null && TryParseFloat(fromRaw, out var from)) builder.FromTo(path, from, to);
                        else builder.To(path, to);
                        break;
                    }
                    case VMGChannelType.Int:
                    {
                        if (!TryParseInt(toRaw, out var to)) return;
                        if (fromRaw != null && TryParseInt(fromRaw, out var from)) builder.FromTo(path, from, to);
                        else builder.To(path, to);
                        break;
                    }
                    case VMGChannelType.Bool:
                    {
                        bool to = ParseBool(toRaw);
                        if (fromRaw != null) builder.FromTo(path, ParseBool(fromRaw), to);
                        else builder.To(path, to);
                        break;
                    }
                    case VMGChannelType.Color:
                    {
                        if (!TryParseColor(toRaw, out var to)) return;
                        if (fromRaw != null && TryParseColor(fromRaw, out var from)) builder.FromTo(path, from, to);
                        else builder.To(path, to);
                        break;
                    }
                    case VMGChannelType.Vector2:
                    {
                        if (!TryParseVector2(toRaw, out var to)) return;
                        if (fromRaw != null && TryParseVector2(fromRaw, out var from)) builder.FromTo(path, from, to);
                        else builder.To(path, to);
                        break;
                    }
                    case VMGChannelType.Vector3:
                    {
                        if (!TryParseVector3(toRaw, out var to)) return;
                        if (fromRaw != null && TryParseVector3(fromRaw, out var from)) builder.FromTo(path, from, to);
                        else builder.To(path, to);
                        break;
                    }
                    case VMGChannelType.Vector4:
                    {
                        if (!TryParseVector4(toRaw, out var to)) return;
                        if (fromRaw != null && TryParseVector4(fromRaw, out var from)) builder.FromTo(path, from, to);
                        else builder.To(path, to);
                        break;
                    }
                }
            }

            static void ApplyTimelineSet(VMGTimeline tl, Component target, string path, string value, VMGAt at)
            {
                var type = ResolveChannelType(target, path);
                switch (type)
                {
                    case VMGChannelType.Float: if (TryParseFloat(value, out var f)) tl.Set(target, path, f, at); break;
                    case VMGChannelType.Int: if (TryParseInt(value, out var i)) tl.Set(target, path, i, at); break;
                    case VMGChannelType.Bool: tl.Set(target, path, ParseBool(value), at); break;
                    case VMGChannelType.Color: if (TryParseColor(value, out var c)) tl.Set(target, path, c, at); break;
                    case VMGChannelType.Vector2: if (TryParseVector2(value, out var v2)) tl.Set(target, path, v2, at); break;
                    case VMGChannelType.Vector3: if (TryParseVector3(value, out var v3)) tl.Set(target, path, v3, at); break;
                    case VMGChannelType.Vector4: if (TryParseVector4(value, out var v4)) tl.Set(target, path, v4, at); break;
                }
            }

            static VMGAt ExtractAt(Dictionary<string, string> attrs)
            {
                if (attrs != null && attrs.TryGetValue("at", out var s) && !string.IsNullOrEmpty(s))
                    return VMGAt.Parse(s);
                return VMGAt.End();
            }
        }

        // ----- Value parsing helpers -----

        static List<string> SplitTuple(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(s)) return list;
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == ',') { list.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            list.Add(s.Substring(start));
            return list;
        }

        static bool TryParseFloat(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        static bool TryParseInt(string s, out int v)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        static bool ParseBool(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s == "true" || s == "yes" || s == "on" || s == "1") return true;
            return false;
        }

        static bool TryParseVector2(string s, out Vector2 v)
        {
            v = default;
            var parts = SplitTuple(s);
            if (parts.Count < 2) return false;
            if (!TryParseFloat(parts[0], out var x)) return false;
            if (!TryParseFloat(parts[1], out var y)) return false;
            v = new Vector2(x, y);
            return true;
        }

        static bool TryParseVector3(string s, out Vector3 v)
        {
            v = default;
            var parts = SplitTuple(s);
            if (parts.Count < 3) return false;
            if (!TryParseFloat(parts[0], out var x)) return false;
            if (!TryParseFloat(parts[1], out var y)) return false;
            if (!TryParseFloat(parts[2], out var z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        static bool TryParseVector4(string s, out Vector4 v)
        {
            v = default;
            var parts = SplitTuple(s);
            if (parts.Count < 4) return false;
            if (!TryParseFloat(parts[0], out var x)) return false;
            if (!TryParseFloat(parts[1], out var y)) return false;
            if (!TryParseFloat(parts[2], out var z)) return false;
            if (!TryParseFloat(parts[3], out var w)) return false;
            v = new Vector4(x, y, z, w);
            return true;
        }

        static bool TryParseColor(string s, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrEmpty(s)) return false;

            // Hex (#fff, #ffff, #ffffff, #ffffffff)
            if (s[0] == '#')
            {
                return TryParseHex(s, out c);
            }

            // rgba/r,g,b tuple
            if (s.Contains(","))
            {
                var parts = SplitTuple(s);
                if (parts.Count == 3 || parts.Count == 4)
                {
                    if (!TryParseFloat(parts[0], out var r)) return false;
                    if (!TryParseFloat(parts[1], out var g)) return false;
                    if (!TryParseFloat(parts[2], out var b)) return false;
                    float a = 1f;
                    if (parts.Count == 4 && !TryParseFloat(parts[3], out a)) return false;
                    // If any channel > 1, treat as 0..255 ints.
                    if (r > 1f || g > 1f || b > 1f) { r /= 255f; g /= 255f; b /= 255f; if (a > 1f) a /= 255f; }
                    c = new Color(r, g, b, a);
                    return true;
                }
            }

            // Named colour
            return TryParseNamedColor(s, out c);
        }

        static bool TryParseHex(string s, out Color c)
        {
            c = Color.white;
            if (s.Length < 2 || s[0] != '#') return false;
            string hex = s.Substring(1);
            try
            {
                if (hex.Length == 3)
                {
                    c = new Color(
                        Convert.ToInt32(new string(hex[0], 2), 16) / 255f,
                        Convert.ToInt32(new string(hex[1], 2), 16) / 255f,
                        Convert.ToInt32(new string(hex[2], 2), 16) / 255f,
                        1f);
                    return true;
                }
                if (hex.Length == 4)
                {
                    c = new Color(
                        Convert.ToInt32(new string(hex[0], 2), 16) / 255f,
                        Convert.ToInt32(new string(hex[1], 2), 16) / 255f,
                        Convert.ToInt32(new string(hex[2], 2), 16) / 255f,
                        Convert.ToInt32(new string(hex[3], 2), 16) / 255f);
                    return true;
                }
                if (hex.Length == 6)
                {
                    c = new Color(
                        Convert.ToInt32(hex.Substring(0, 2), 16) / 255f,
                        Convert.ToInt32(hex.Substring(2, 2), 16) / 255f,
                        Convert.ToInt32(hex.Substring(4, 2), 16) / 255f,
                        1f);
                    return true;
                }
                if (hex.Length == 8)
                {
                    c = new Color(
                        Convert.ToInt32(hex.Substring(0, 2), 16) / 255f,
                        Convert.ToInt32(hex.Substring(2, 2), 16) / 255f,
                        Convert.ToInt32(hex.Substring(4, 2), 16) / 255f,
                        Convert.ToInt32(hex.Substring(6, 2), 16) / 255f);
                    return true;
                }
            }
            catch { /* fall through */ }
            return false;
        }

        static bool TryParseNamedColor(string name, out Color c)
        {
            switch (name)
            {
                case "white": c = Color.white; return true;
                case "black": c = Color.black; return true;
                case "red": c = Color.red; return true;
                case "green": c = Color.green; return true;
                case "blue": c = Color.blue; return true;
                case "yellow": c = Color.yellow; return true;
                case "cyan": c = Color.cyan; return true;
                case "magenta": c = Color.magenta; return true;
                case "gray":
                case "grey": c = Color.gray; return true;
                case "clear": c = Color.clear; return true;
                case "orange": c = new Color(1f, 0.5f, 0f, 1f); return true;
                case "purple": c = new Color(0.5f, 0f, 1f, 1f); return true;
            }
            c = Color.white;
            return false;
        }
    }

    // Compiled output of VMGFxScript. VMGAnimator drives Sample/Stop through
    // these handles. Combines hierarchy (scene), parallel root-level
    // animations, and named timelines.
    public sealed class VMGFxCompiled
    {
        public VMGScene scene;
        public List<VMGAnimate> standaloneAnimates = new List<VMGAnimate>();
        public List<VMGTimeline> timelines = new List<VMGTimeline>();

        // VMGAnimator forwards 'call' statements here. Default impl logs;
        // user wiring can subscribe to scriptEvent on VMGAnimator.
        public event Action<string> OnEvent;

        internal void RaiseEvent(string name)
        {
            if (OnEvent != null) OnEvent.Invoke(name);
            else Debug.Log($"[VMGFx] event '{name}' (no listener)");
        }

        // Compute the longest duration across timelines + standalone animates.
        // Used by VMGAnimator script-mode as the effective clip duration when
        // mapping progress 0..1 → seconds.
        public float TotalDuration
        {
            get
            {
                float max = 0f;
                if (timelines != null)
                {
                    foreach (var tl in timelines)
                    {
                        if (tl == null) continue;
                        if (tl.iterationDuration > max) max = tl.iterationDuration;
                    }
                }
                if (standaloneAnimates != null)
                {
                    foreach (var a in standaloneAnimates)
                    {
                        if (a == null) continue;
                        var anim = a.Animation;
                        if (anim == null) continue;
                        float d = anim.iterationDuration + anim.startDelay;
                        if (d > max) max = d;
                    }
                }
                return max;
            }
        }

        // Seek every owned animation/timeline to the given absolute time.
        // Called by VMGAnimator each LateUpdate so progress drives every
        // script-built motion uniformly.
        public void SeekAll(float seconds)
        {
            if (timelines != null)
            {
                foreach (var tl in timelines)
                {
                    if (tl == null) continue;
                    tl.Seek(seconds);
                }
            }
            if (standaloneAnimates != null)
            {
                foreach (var a in standaloneAnimates)
                {
                    if (a == null) continue;
                    a.Seek(seconds);
                }
            }
        }

        // Detach all engine ownership so VMGAnimator drives Seek instead of
        // the engine. Called immediately after compile.
        public void DetachFromEngine()
        {
            if (timelines != null)
            {
                foreach (var tl in timelines)
                {
                    if (tl == null) continue;
                    tl.Pause();
                    VMGEngine.UnregisterTimeline(tl);
                    VMGEngine.CancelDeferredTimeline(tl);
                }
            }
            if (standaloneAnimates != null)
            {
                foreach (var a in standaloneAnimates)
                {
                    if (a == null) continue;
                    a.Pause();
                    var anim = a.Animation;
                    VMGEngine.Unregister(anim);
                    VMGEngine.CancelDeferred(a);
                }
            }
        }
    }
}
