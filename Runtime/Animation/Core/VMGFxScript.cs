using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using VMG.Animation;
using VMG.Core;
using VMG.Svg;
using VMG.UI;

namespace VMG.Animation.Core
{
    // VMGFx DSL — flat statement list that mirrors VMGFx.Scene / VMGFx.Animate
    // / VMGFx.Timeline calls one-to-one. Authored as plain text (TextAsset)
    // and compiled by VMGAnimator at OnEnable.
    //
    // Statement keywords:
    //   add <name> <shape> [key=value ...]
    //   group <name> { ... }
    //   mask <name> { ... }              (stencil-mask group; see VMGMaskGroup)
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
    //              cornerRadii, points, closed
    //   animate:   duration, ease, at, delay, endDelay, loop, alternate, from,
    //              refreshOnLoop
    //
    // Generator helpers (FunctionValue):
    //   random(min, max [, seed])     — float in [min, max]
    //   rangeInt(min, max [, seed])   — int in [min, max] inclusive
    // Usable in any -> value, from= value, or keyframes path=value position
    // on numeric channels. Seed (int) is optional — supply for deterministic
    // sequences; omit for global UnityEngine.Random.
    //
    // Asset references:
    //   motionPath <target> path=asset(<name>) [subShape=<int>]
    //                       [autoRotate ...] [duration= ease= ...]
    // The named entry must exist in VMGAnimator.assets and resolve to a
    // VMGShapeAsset (anime.js parity — any SVG path is a usable motion
    // curve). When 'path' is supplied, inline 'points='/'closed=' are
    // ignored. subShape (default 0) picks which sub-shape to follow.
    //   set/call/label: at
    public static class VMGFxScript
    {
        public static VMGFxCompiled Compile(string source, Transform root)
            => Compile(source, root, null);

        // assetLookup: optional name→UnityEngine.Object map. Currently consumed
        // by `motionPath path=asset(name)` to resolve a VMGShapeAsset by name.
        // Pass null when no asset references are expected — the legacy 2-arg
        // overload routes here with null.
        public static VMGFxCompiled Compile(string source, Transform root, IReadOnlyDictionary<string, UnityEngine.Object> assetLookup)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var compiled = new VMGFxCompiled();
            compiled.assetLookup = assetLookup;
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
                        // tuple values so it's allowed inside. Whitespace
                        // inside balanced parens is preserved — this lets the
                        // user write `random(-100, 100)` or
                        // `cubicBezier(0.25, 0.1, 0.25, 1)` with readable
                        // spacing instead of being forced into compact form.
                        // Newlines still terminate the value (no
                        // multi-line generator calls).
                        int parenDepth = 0;
                        var sb = new StringBuilder();
                        while (i < src.Length)
                        {
                            char vc = src[i];
                            if (vc == '\n' || vc == '\r') break;
                            if (parenDepth == 0)
                            {
                                if (vc == ' ' || vc == '\t') break;
                                if (vc == ';' || vc == '{' || vc == '}') break;
                                // '//' inside a value ends the value (line comment).
                                if (vc == '/' && i + 1 < src.Length && src[i + 1] == '/') break;
                            }
                            if (vc == '(') parenDepth++;
                            else if (vc == ')' && parenDepth > 0) parenDepth--;
                            // Drop spaces and tabs from inside parens so
                            // downstream `SplitTuple` / `TryParseFloat` see the
                            // same compact form as before. ',' is unchanged.
                            if (parenDepth > 0 && (vc == ' ' || vc == '\t')) { i++; continue; }
                            sb.Append(vc);
                            i++;
                        }
                        if (sb.Length > 0)
                            list.Add(new Tok { kind = TokKind.Ident, text = sb.ToString(), line = line });
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
                    if (c == '{' || c == '}' || c == ',' || c == '=' || c == ';' || c == '%' || c == ':')
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
                    // parser interprets context. This makes hex colours (#fff), paths (Trim.end),
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
                // '*' is allowed in body (not start) so the stagger wildcard
                // target `dots/*` tokenises as a single ident; it stays
                // illegal at start to avoid stealing tokens elsewhere.
                if (c == '_' || c == '.' || c == '/' || c == '#' || c == '<' || c == '*') return true;
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

        // Stencil-mask group. Same shape as GroupStmt — a named container
        // with child statements — but the apply step also attaches
        // VMGMaskGroup to the container GameObject and VMGMaskSource to
        // each child Graphic. Children added later via `in=<maskName>`
        // (handled by ApplyAdd) get VMGMaskClient instead.
        internal sealed class MaskStmt : Stmt
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

        // Follow an arc-length parametrized curve. Mirrors the code-API
        // VMGAnimate.AlongPath / AutoRotate pair. The target's
        // transform.position is driven along the path; with autoRotate set,
        // transform.eulerAngles.z is also written.
        //
        // Syntax: `motionPath <target> points=x1,y1,x2,y2,... [closed=true]
        //          [autoRotate] [autoRotate=-90] [duration= ease= delay=
        //          endDelay= loop= alternate at=]`
        //
        // `animate` can't host MotionPath cleanly because its
        // `<target> <fieldPath> -> <toValue>` grammar requires a `to` value
        // that MotionPath has no use for (the path drives position itself).
        // A separate statement is cleaner than a dummy `-> 0,0`.
        //
        // Inline `points` only this round — asset binding is deferred to a
        // future round that decides DSL asset registration in general.
        internal sealed class MotionPathStmt : Stmt
        {
            public string target;
            public Dictionary<string, string> attrs = new Dictionary<string, string>();
        }

        internal sealed class TimelineStmt : Stmt
        {
            public Dictionary<string, string> attrs = new Dictionary<string, string>();
            public List<Stmt> children = new List<Stmt>();
        }

        // Inside a timeline only. Subscribes an event name to a timeline
        // lifecycle callback. The handler dispatches through
        // VMGFxCompiled.RaiseEvent, sharing the same listener channel as
        // `call`. Syntax: `on <event> -> <eventName>` where <event> is one of
        // begin / beforeUpdate / update / render / loop / complete / pause.
        internal sealed class OnStmt : Stmt
        {
            public string evt;
            public string eventName;
        }

        // Inside a top-level or timeline scope. Multi-keyframe CSS-style
        // block. Compiles into one or more segment animates (per channel,
        // per gap between adjacent keyframes) added to the enclosing
        // timeline. Syntax:
        //   keyframes <target> [attrs] {
        //     <pct>%: <path>=<value> [<path>=<value> ...]
        //     <pct>%: ...
        //   }
        // Top-level keyframes implicitly wrap themselves in a timeline.
        internal sealed class KeyframesStmt : Stmt
        {
            public string target;
            public Dictionary<string, string> attrs = new Dictionary<string, string>();
            public List<Keyframe> frames = new List<Keyframe>();
        }

        internal sealed class Keyframe
        {
            public float pct;            // 0..100
            public int line;
            public Dictionary<string, string> values = new Dictionary<string, string>();
            public string easeOverride;  // optional per-frame ease
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

        // Repeat-with-index block. Mirrors VMGTimeline.Stagger(targets, build,
        // step, from, seed) from the code API. Resolves a wildcard target spec
        // like `dots/*` (direct children of a named group, scene order) into
        // N components, then runs each child statement once per target with
        // `it`/`i`/`n` substituted at target/value positions before the
        // existing AnimateStmt / MotionPathStmt code paths take over.
        //
        // Syntax:
        //   stagger <group>/* [step=F] [from=first|center|last|random]
        //                     [seed=N] [at=POS] {
        //       animate it <path> -> <value> [attrs ...]
        //       animate it.transform <path> -> <value> [attrs ...]
        //       motionPath it points=... [attrs ...]
        //   }
        //
        // Per-block bindings inside the body:
        //   it — current child component (target position only)
        //   i  — index (0..n-1)            (value position only)
        //   n  — total target count        (value position only)
        // These names are stagger-scoped; outside the block they remain
        // ordinary identifiers (the compiler only substitutes when expanding
        // child statements under a StaggerStmt parent).
        //
        // Allowed child statements: animate, motionPath. set/call/label are
        // not allowed (label-per-child / call-per-child semantics are
        // ambiguous). Block is legal both inside a `timeline { ... }` and at
        // top level — top-level wraps itself in an implicit timeline,
        // mirroring `keyframes`.
        internal sealed class StaggerStmt : Stmt
        {
            public string targetWildcard;  // e.g. "dots/*"
            public Dictionary<string, string> attrs = new Dictionary<string, string>();
            public List<Stmt> children = new List<Stmt>();
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
                    case "mask": return ParseMask(p);
                    case "animate": return ParseAnimate(p);
                    case "motionPath": return ParseMotionPath(p);
                    case "timeline": return ParseTimeline(p);
                    case "keyframes": return ParseKeyframes(p);
                    case "stagger": return ParseStagger(p);
                    case "set": if (insideTimeline) return ParseSet(p); break;
                    case "call": if (insideTimeline) return ParseCall(p); break;
                    case "label": if (insideTimeline) return ParseLabel(p); break;
                    case "on": if (insideTimeline) return ParseOn(p); break;
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

            static MaskStmt ParseMask(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'mask'
                string name = p.ExpectIdent("mask name");
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
                return new MaskStmt { line = line, name = name, children = children };
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

            static MotionPathStmt ParseMotionPath(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'motionPath'
                string target = p.ExpectIdent("motionPath target");
                var attrs = ParseAttributes(p);
                p.EndStatement();
                return new MotionPathStmt { line = line, target = target, attrs = attrs };
            }

            static TimelineStmt ParseTimeline(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'timeline'
                // Header attrs: `timeline duration=2 ease=outQuad rate=1.5 { ... }`.
                // Parsed up to the opening brace.
                var attrs = ParseAttributes(p);
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
                return new TimelineStmt { line = line, attrs = attrs, children = children };
            }

            static OnStmt ParseOn(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'on'
                string evt = p.ExpectIdent("event name (begin/beforeUpdate/update/render/loop/complete/pause)");
                p.ExpectArrow();
                string eventName = ParseValue(p);
                p.EndStatement();
                return new OnStmt { line = line, evt = evt, eventName = eventName };
            }

            // `keyframes <target> [attrs] { <pct>%: k=v ...; <pct>%: ...; ... }`.
            // The body is a sequence of "<pct>%:" lines, each carrying a flat
            // key=value attribute list of per-channel values. ';' or newline
            // separates frames.
            static KeyframesStmt ParseKeyframes(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'keyframes'
                string target = p.ExpectIdent("keyframes target");
                var attrs = ParseAttributes(p);
                p.ExpectSymbol("{");
                var frames = new List<Keyframe>();
                p.SkipNewlines();
                while (!p.AtEnd)
                {
                    var t = p.Peek();
                    if (t.kind == TokKind.Symbol && t.text == "}") break;

                    // Each frame: '<pct>%:' header followed by attr list.
                    var pctTok = p.Peek();
                    if (pctTok.kind != TokKind.Ident && pctTok.kind != TokKind.Number)
                    {
                        p.Error($"expected keyframe percent, got '{pctTok.text}'");
                        p.SkipToNewline();
                        p.SkipNewlines();
                        continue;
                    }
                    string pctRaw = pctTok.text;
                    p.Consume();

                    // Accept either '12%' baked into the ident, or split tokens
                    // '12' '%' (the tokenizer emits '%' as its own Symbol).
                    if (pctRaw.EndsWith("%")) pctRaw = pctRaw.Substring(0, pctRaw.Length - 1);
                    else
                    {
                        var maybePct = p.Peek();
                        if (maybePct.kind == TokKind.Symbol && maybePct.text == "%") p.Consume();
                    }

                    // Allow CSS keywords from/to in place of 0/100.
                    if (pctRaw == "from") pctRaw = "0";
                    else if (pctRaw == "to") pctRaw = "100";

                    if (!TryParseFloat(pctRaw, out var pct))
                    {
                        p.Error($"keyframe percent '{pctRaw}' is not numeric");
                        p.SkipToNewline();
                        p.SkipNewlines();
                        continue;
                    }

                    // ':' separator before the value list. Required; if
                    // missing, attribute parsing still works but a misplaced
                    // identifier would be confusing.
                    var sep = p.Peek();
                    if (sep.kind == TokKind.Symbol && sep.text == ":") p.Consume();

                    var values = ParseAttributes(p);
                    string easeOverride = null;
                    if (values.TryGetValue("ease", out var ev)) { easeOverride = ev; values.Remove("ease"); }
                    frames.Add(new Keyframe { pct = pct, line = pctTok.line, values = values, easeOverride = easeOverride });
                    p.SkipNewlines();
                }
                p.ExpectSymbol("}");
                p.EndStatement();
                return new KeyframesStmt { line = line, target = target, attrs = attrs, frames = frames };
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

            // `stagger <wildcard> [attrs] { <animate|motionPath ...>; ... }`.
            // The wildcard is a target spec terminated by '/*' — only that
            // form is supported this round. Body grammar matches the
            // `timeline { ... }` block (statements separated by newlines);
            // body statements are validated at compile time to be just
            // animate / motionPath (the compiler logs an error otherwise).
            static StaggerStmt ParseStagger(ParseState p)
            {
                int line = p.Peek().line;
                p.Consume(); // 'stagger'
                string wildcard = p.ExpectIdent("stagger target wildcard (e.g. 'dots/*')");
                var attrs = ParseAttributes(p);
                p.ExpectSymbol("{");
                var children = new List<Stmt>();
                p.SkipNewlines();
                while (!p.AtEnd)
                {
                    var t = p.Peek();
                    if (t.kind == TokKind.Symbol && t.text == "}") break;
                    // insideTimeline=false so set/call/label/on are rejected
                    // at parse time. animate / motionPath are the only legal
                    // statements inside a stagger block.
                    var c = ParseStatement(p, insideTimeline: false);
                    if (c != null) children.Add(c);
                    p.SkipNewlines();
                }
                p.ExpectSymbol("}");
                p.EndStatement();
                return new StaggerStmt { line = line, targetWildcard = wildcard, attrs = attrs, children = children };
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
            //
            // Bare keys (no `=value`) are accepted and registered with an
            // empty-string value. Downstream attr handlers treat empty /
            // missing as "on" for flag attrs (`loop`, `alternate`,
            // `autoRotate`, `refreshOnLoop`). This matches the doc-comment
            // examples (`loop alternate`) and anime.js's flag form.
            static Dictionary<string, string> ParseAttributes(ParseState p)
            {
                var attrs = new Dictionary<string, string>();
                while (true)
                {
                    var t = p.Peek();
                    if (t.kind == TokKind.Newline || t.kind == TokKind.EOF) break;
                    // `{` terminates an attribute list because block statements
                    // (timeline / keyframes / group / stagger) can carry header
                    // attrs on the same line as the opening brace, e.g.
                    // `stagger dots/* step=0.1 from=center { ... }`. Without
                    // this stop the parser would treat `{` as an attribute key
                    // and fail. `}` / `;` were already terminators.
                    if (t.kind == TokKind.Symbol && (t.text == "}" || t.text == ";" || t.text == "{")) break;

                    if (t.kind != TokKind.Ident)
                    {
                        p.Error($"expected attribute key, got '{t.text}'");
                        p.SkipToNewline();
                        break;
                    }

                    string key = t.text;
                    p.Consume();

                    var eq = p.Peek();
                    if (eq.kind == TokKind.Symbol && eq.text == "=")
                    {
                        p.Consume(); // '='
                        string value = ParseValue(p);
                        attrs[key] = value;
                    }
                    else
                    {
                        // Bare flag — register key with empty value; handlers
                        // treat empty as "on" for the flag-style attrs.
                        attrs[key] = "";
                    }
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

                // Pass 1: hierarchy (add / group / mask).
                foreach (var s in statements)
                {
                    if (s is AddStmt addS) ApplyAdd(addS, scene, compiled);
                    else if (s is MaskStmt maskS) ApplyMask(maskS, scene, compiled);
                    else if (s is GroupStmt grpS) ApplyGroup(grpS, scene, compiled);
                }

                // Pass 2: animations / timelines / keyframes. We resolve
                // names via the scene's child lookup; if a target isn't in
                // the scene we fall back to looking up a child Transform
                // under root. Top-level keyframes / stagger auto-wrap in a
                // one-off timeline so the expansion has somewhere to live.
                foreach (var s in statements)
                {
                    if (s is AnimateStmt anim) BuildStandaloneAnimate(anim, scene, root, compiled);
                    else if (s is MotionPathStmt mp) BuildStandaloneMotionPath(mp, scene, root, compiled);
                    else if (s is TimelineStmt tl) BuildTimeline(tl, scene, root, compiled);
                    else if (s is KeyframesStmt kf) BuildStandaloneKeyframes(kf, scene, root, compiled);
                    else if (s is StaggerStmt stg) BuildStandaloneStagger(stg, scene, root, compiled);
                }
            }

            // Top-level stagger: wrap an implicit timeline around the block so
            // tl.Stagger has somewhere to add its children. Mirrors
            // BuildStandaloneKeyframes — `at=` on a top-level stagger is
            // ignored (no enclosing timeline to position within); other
            // header attrs (step/from/seed) flow to the Stagger call.
            static void BuildStandaloneStagger(StaggerStmt stg, VMGScene scene, Transform root, VMGFxCompiled compiled)
            {
                var tl = VMGFx.Timeline();
                compiled.timelines.Add(tl);
                ExpandStaggerIntoTimeline(tl, stg, scene, root, compiled);
            }

            // Resolve the wildcard target, build a fresh mini-timeline per
            // child with each statement (animate / motionPath / keyframes)
            // added in order with `it`/`i`/`n` substituted. The parent
            // timeline anchors each mini-timeline at the same per-child
            // offset so every statement inside a child stays in lockstep.
            //
            // Substitution happens on freshly-cloned per-child copies of the
            // AST so the original StaggerStmt remains untouched for any
            // future recompile (script-mode OnEnable re-runs).
            static void ExpandStaggerIntoTimeline(VMGTimeline tl, StaggerStmt stg, VMGScene scene, Transform root, VMGFxCompiled compiled)
            {
                if (stg.children == null || stg.children.Count == 0)
                {
                    Debug.LogWarning("[VMGFx] stagger: empty block, nothing to do");
                    return;
                }

                // Validate child statement types up-front so the error points
                // at the stagger block instead of the deeper compile path.
                foreach (var c in stg.children)
                {
                    if (c is AnimateStmt || c is MotionPathStmt || c is KeyframesStmt) continue;
                    Debug.LogError($"[VMGFx] stagger line {c.line}: only 'animate', 'motionPath' and 'keyframes' are allowed inside a stagger block (got {c.GetType().Name})");
                    return;
                }

                var targets = ResolveStaggerTargets(stg.targetWildcard, scene, root, stg);
                if (targets == null || targets.Count == 0)
                {
                    Debug.LogError($"[VMGFx] stagger: target '{stg.targetWildcard}' resolved to no components");
                    return;
                }

                // Header attrs.
                float step = 0.1f;
                VMGStaggerFrom from = VMGStaggerFrom.First;
                int? seed = null;
                VMGAt at = VMGAt.End();
                if (stg.attrs != null)
                {
                    foreach (var kv in stg.attrs)
                    {
                        switch (kv.Key)
                        {
                            case "step": if (TryParseFloat(kv.Value, out var sv)) step = sv; break;
                            case "from": from = ParseStaggerFrom(kv.Value); break;
                            case "seed": if (TryParseInt(kv.Value, out var sd)) seed = sd; break;
                            case "at": at = string.IsNullOrEmpty(kv.Value) ? VMGAt.End() : VMGAt.Parse(kv.Value); break;
                            default:
                                Debug.LogWarning($"[VMGFx] unknown stagger attribute '{kv.Key}'");
                                break;
                        }
                    }
                }

                // Capture scene for non-`it` target fallback inside the
                // lambda. Most bodies use `it`, but a stagger body is also
                // free to reference a sibling/global by name — that path
                // goes through the standard ResolveTarget and needs the
                // scene + root.
                var sceneRef = scene;
                tl.Stagger<Component>(targets, (Component childTarget, int idx, int n) =>
                {
                    // Build a fresh mini-timeline per child and add every
                    // statement to it. Parent's per-child offset is applied
                    // once to the whole mini, so a 3-statement body (pulse +
                    // spin + tint) stays in lockstep within each child.
                    var mini = VMGFx.Timeline();
                    tl.CopyDefaultsTo(mini);
                    foreach (var c in stg.children)
                    {
                        if (c is AnimateStmt aSrc)
                        {
                            var a = CloneAnimateForStagger(aSrc, idx, n);
                            var resolved = ResolveStaggerChildTarget(a.target, childTarget, sceneRef, root, a.path);
                            if (resolved == null)
                            {
                                Debug.LogError($"[VMGFx] stagger line {a.line}: target '{a.target}' did not resolve for child #{idx} ('{childTarget?.name}')");
                                continue;
                            }
                            var builder = VMGFx.Animate(resolved);
                            ConfigureAnimate(builder, a.path, a.toValue, a.attrs, resolved);
                            // Mirror BuildStandaloneAnimate's positioning: use
                            // the per-statement `at=` attr if present (lets a
                            // body interleave statements via at=+0.1 etc.),
                            // otherwise default to End() so statements
                            // sequence by appearance order inside the mini.
                            var stmtAt = ExtractAtAttr(a.attrs);
                            mini.Add(builder, stmtAt);
                        }
                        else if (c is MotionPathStmt mSrc)
                        {
                            var m = CloneMotionPathForStagger(mSrc, idx, n);
                            var resolved = ResolveStaggerChildTarget(m.target, childTarget, sceneRef, root, null);
                            if (resolved == null)
                            {
                                Debug.LogError($"[VMGFx] stagger line {m.line}: motionPath target '{m.target}' did not resolve for child #{idx} ('{childTarget?.name}')");
                                continue;
                            }
                            var builder = VMGFx.Animate(resolved);
                            ConfigureMotionPath(builder, m.attrs, compiled);
                            var stmtAt = ExtractAtAttr(m.attrs);
                            mini.Add(builder, stmtAt);
                        }
                        else if (c is KeyframesStmt kSrc)
                        {
                            var k = CloneKeyframesForStagger(kSrc, idx, n);
                            var resolved = ResolveStaggerChildTarget(k.target, childTarget, sceneRef, root, null);
                            if (resolved == null)
                            {
                                Debug.LogError($"[VMGFx] stagger line {k.line}: keyframes target '{k.target}' did not resolve for child #{idx} ('{childTarget?.name}')");
                                continue;
                            }
                            // ExpandKeyframesIntoTimeline already handles
                            // the block's `at=` attr against the timeline's
                            // current end, so within the mini the same
                            // sequencing logic applies for free.
                            ExpandKeyframesIntoTimeline(mini, k, resolved);
                        }
                    }
                    return mini;
                }, step, from, at, seed);
            }

            // Pull and strip the `at=` attribute from a per-statement attr
            // bag. The remaining attrs continue to flow to
            // ConfigureAnimate / ConfigureMotionPath, which both also peek
            // at `at=` — leaving it in would be harmless but redundant.
            // Returning End() when absent matches mini.Add's default and
            // lets statements sequence in declaration order.
            static VMGAt ExtractAtAttr(Dictionary<string, string> attrs)
            {
                if (attrs == null) return VMGAt.End();
                if (!attrs.TryGetValue("at", out var raw)) return VMGAt.End();
                return string.IsNullOrEmpty(raw) ? VMGAt.End() : VMGAt.Parse(raw);
            }

            // Wildcard target resolution. Only `<group>/*` is supported this
            // round — direct children of the named group, scene order. The
            // group is looked up first in the scene's group dict (via
            // Transform.Find on root, which handles nested 'a/b/c' paths
            // too), then each direct child is walked for its renderer
            // Component; if no renderer is found, the Transform itself is
            // used as the target so attribute paths like 'localScale' still
            // work. Returns a fresh list (caller mutates / iterates).
            static List<Component> ResolveStaggerTargets(string spec, VMGScene scene, Transform root, StaggerStmt stg)
            {
                if (string.IsNullOrEmpty(spec))
                {
                    Debug.LogError($"[VMGFx] stagger line {stg.line}: empty target");
                    return null;
                }
                if (!spec.EndsWith("/*"))
                {
                    Debug.LogError($"[VMGFx] stagger line {stg.line}: target '{spec}' must end with '/*' (only wildcard form is supported this round)");
                    return null;
                }
                string groupName = spec.Substring(0, spec.Length - 2);
                Transform groupTr = string.IsNullOrEmpty(groupName) || groupName == "/" || groupName == "root" || groupName == "self"
                    ? root
                    : root.Find(groupName);
                if (groupTr == null)
                {
                    Debug.LogError($"[VMGFx] stagger line {stg.line}: group '{groupName}' not found under root");
                    return null;
                }
                var list = new List<Component>(groupTr.childCount);
                for (int i = 0; i < groupTr.childCount; i++)
                {
                    var ct = groupTr.GetChild(i);
                    // Prefer a renderer component (VectorImageGraphic /
                    // VectorSpriteRenderer) so `Fill.color`, `Trim.end`,
                    // etc. resolve straight away. Fall back to Transform.
                    Component pick = ct.GetComponent<VMG.UI.VectorImageGraphic>();
                    if (pick == null) pick = ct.GetComponent<VMG.World.VectorSpriteRenderer>();
                    if (pick == null) pick = ct;
                    list.Add(pick);
                }
                return list;
            }

            // Inside a stagger body, the child's literal target token is
            // either `it` (current wildcard match), `it.transform` (its
            // Transform), or a normal target name that falls through to the
            // standard ResolveTarget. We treat `it` specially because the
            // standard ResolveTarget would try to look up an object literally
            // named "it" under root.
            static Component ResolveStaggerChildTarget(string targetSpec, Component childPrimary, VMGScene scene, Transform root, string path)
            {
                if (string.IsNullOrEmpty(targetSpec)) return null;
                if (targetSpec == "it")
                {
                    if (childPrimary == null) return null;
                    if (string.IsNullOrEmpty(path)) return childPrimary;
                    // Reserved Transform leaves and primary-misses fall back
                    // to the Transform — same rule as PickByPath.
                    if (IsTransformReservedPath(path)) return childPrimary.transform;
                    if (VMG.Animation.VMGFieldPathCompiler.TryCompile(childPrimary.GetType(), path, out _, out _))
                        return childPrimary;
                    return childPrimary.transform;
                }
                if (targetSpec == "it.transform")
                {
                    return childPrimary != null ? childPrimary.transform : null;
                }
                // Non-`it` target: fall back to the standard resolver. Useful
                // for animating a sibling/global thing from inside the loop,
                // though the body usually targets `it`.
                return ResolveTarget(targetSpec, scene, root, path);
            }

            // Clone an AnimateStmt for a specific stagger index. The original
            // is preserved (so a script-mode OnEnable re-run sees the same
            // AST), while the clone's path / toValue / each attr value are
            // walked for standalone `i` / `n` tokens (word-boundary aware) and
            // substituted with the current index / total. Target keeps `it`
            // / `it.transform` literal so ResolveStaggerChildTarget can
            // recognise it.
            static AnimateStmt CloneAnimateForStagger(AnimateStmt src, int index, int total)
            {
                var attrs = new Dictionary<string, string>();
                if (src.attrs != null)
                {
                    foreach (var kv in src.attrs)
                        attrs[kv.Key] = SubstituteStaggerVars(kv.Value, index, total);
                }
                return new AnimateStmt
                {
                    line = src.line,
                    target = src.target,
                    path = SubstituteStaggerVars(src.path, index, total),
                    toValue = SubstituteStaggerVars(src.toValue, index, total),
                    attrs = attrs,
                };
            }

            static MotionPathStmt CloneMotionPathForStagger(MotionPathStmt src, int index, int total)
            {
                var attrs = new Dictionary<string, string>();
                if (src.attrs != null)
                {
                    foreach (var kv in src.attrs)
                        attrs[kv.Key] = SubstituteStaggerVars(kv.Value, index, total);
                }
                return new MotionPathStmt
                {
                    line = src.line,
                    target = src.target,
                    attrs = attrs,
                };
            }

            // Clone a KeyframesStmt for a specific stagger index. Block-
            // level attrs, every frame's channel values, and each frame's
            // easeOverride are walked for `i`/`n` token substitution so
            // expressions like `0%: localPosition=random(...,i)` or
            // `delay=i*0.05` work the same way they do in animate /
            // motionPath bodies. Frames are deep-copied so the original
            // AST stays intact for OnEnable re-runs.
            static KeyframesStmt CloneKeyframesForStagger(KeyframesStmt src, int index, int total)
            {
                var attrs = new Dictionary<string, string>();
                if (src.attrs != null)
                {
                    foreach (var kv in src.attrs)
                        attrs[kv.Key] = SubstituteStaggerVars(kv.Value, index, total);
                }
                var frames = new List<Keyframe>(src.frames != null ? src.frames.Count : 0);
                if (src.frames != null)
                {
                    foreach (var f in src.frames)
                    {
                        var values = new Dictionary<string, string>();
                        if (f.values != null)
                        {
                            foreach (var kv in f.values)
                                values[kv.Key] = SubstituteStaggerVars(kv.Value, index, total);
                        }
                        frames.Add(new Keyframe
                        {
                            pct = f.pct,
                            line = f.line,
                            values = values,
                            easeOverride = SubstituteStaggerVars(f.easeOverride, index, total),
                        });
                    }
                }
                return new KeyframesStmt
                {
                    line = src.line,
                    target = src.target,
                    attrs = attrs,
                    frames = frames,
                };
            }

            // Replace the standalone tokens `i` and `n` (word-boundary aware:
            // boundary = anything not in [A-Za-z0-9_]) with the index / total
            // numeric literals. Guards against mangling `inOutQuad`, `linear`,
            // identifier substrings inside generator names, etc.
            //
            // The check at start-of-string treats position 0 as a boundary;
            // same for end-of-string. This makes `i` alone, `i,` (inside
            // tuples), `random(0, 10, i)` all substitute correctly.
            static string SubstituteStaggerVars(string s, int index, int total)
            {
                if (string.IsNullOrEmpty(s)) return s;
                if (s.IndexOf('i') < 0 && s.IndexOf('n') < 0) return s;
                var sb = new StringBuilder(s.Length);
                int len = s.Length;
                for (int i = 0; i < len; i++)
                {
                    char c = s[i];
                    if ((c == 'i' || c == 'n') && IsStaggerVarBoundary(i == 0 ? '\0' : s[i - 1]) && IsStaggerVarBoundary(i + 1 >= len ? '\0' : s[i + 1]))
                    {
                        sb.Append((c == 'i' ? index : total).ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
            }

            static bool IsStaggerVarBoundary(char c)
            {
                if (c == '\0') return true;
                if (c >= 'a' && c <= 'z') return false;
                if (c >= 'A' && c <= 'Z') return false;
                if (c >= '0' && c <= '9') return false;
                if (c == '_') return false;
                return true;
            }

            static VMGStaggerFrom ParseStaggerFrom(string raw)
            {
                if (string.IsNullOrEmpty(raw)) return VMGStaggerFrom.First;
                switch (raw)
                {
                    case "first": return VMGStaggerFrom.First;
                    case "center": return VMGStaggerFrom.Center;
                    case "last": return VMGStaggerFrom.Last;
                    case "random": return VMGStaggerFrom.Random;
                    default:
                        Debug.LogWarning($"[VMGFx] unknown stagger from='{raw}' (expected first/center/last/random)");
                        return VMGStaggerFrom.First;
                }
            }

            // Top-level keyframes: wrap a fresh timeline around the block.
            // The block's `loop` / `alternate` / `delay` attrs are migrated
            // from the keyframes to the wrapping timeline so they take effect
            // (inside-a-timeline ExpandKeyframesIntoTimeline rejects them).
            static void BuildStandaloneKeyframes(KeyframesStmt kf, VMGScene scene, Transform root, VMGFxCompiled compiled)
            {
                var target = ResolveTarget(kf.target, scene, root, FirstKeyframePath(kf));
                if (target == null) { Debug.LogError($"[VMGFx] keyframes: target '{kf.target}' not found"); return; }
                var tl = VMGFx.Timeline();
                compiled.timelines.Add(tl);

                // Migrate timeline-level attrs from the keyframes block to the
                // wrapping timeline so the user can write
                //   keyframes box { ... } duration=2 loop alternate
                // and get the expected behaviour.
                var tlAttrs = new Dictionary<string, string>();
                if (kf.attrs != null)
                {
                    foreach (var kv in kf.attrs)
                    {
                        if (kv.Key == "loop" || kv.Key == "loopDelay" || kv.Key == "alternate" ||
                            kv.Key == "reversed" || kv.Key == "paused" ||
                            kv.Key == "rate" || kv.Key == "playbackRate" ||
                            kv.Key == "playbackEase")
                        {
                            tlAttrs[kv.Key] = kv.Value;
                        }
                    }
                }
                ApplyTimelineHeaderAttrs(tl, tlAttrs, compiled);

                // Strip the migrated keys so the block-level expansion doesn't
                // re-apply them and warn.
                var trimmed = new Dictionary<string, string>();
                if (kf.attrs != null)
                {
                    foreach (var kv in kf.attrs)
                    {
                        if (!tlAttrs.ContainsKey(kv.Key)) trimmed[kv.Key] = kv.Value;
                    }
                }
                var tmp = new KeyframesStmt { line = kf.line, target = kf.target, attrs = trimmed, frames = kf.frames };
                ExpandKeyframesIntoTimeline(tl, tmp, target);
            }

            static void ApplyAdd(AddStmt s, VMGScene scene, VMGFxCompiled compiled)
            {
                var d = MakeDescriptor(s.shape, s.attrs, compiled);
                if (d == null) return;
                ApplyDescriptorAttrs(d, s.attrs);
                // `in=<parent>` re-roots this Add under an earlier child,
                // forming a parent-child chain. The parent must have been
                // added earlier in the same script — order matters here,
                // matching the top-down read flow of the .vmgfx file.
                var target = scene;
                bool intoMask = false;
                if (s.attrs != null && s.attrs.TryGetValue("in", out var parentName) && !string.IsNullOrEmpty(parentName))
                {
                    try { target = scene.ChildScene(parentName.Trim()); }
                    catch (System.InvalidOperationException ex)
                    {
                        Debug.LogError($"[VMGFx] add '{s.name}' in={parentName}: {ex.Message}");
                        return;
                    }
                    // If the parent is a stencil mask group, the added
                    // graphic becomes a mask CLIENT (visible only inside
                    // the union of the group's sources) rather than a
                    // plain nested child.
                    intoMask = target.Root != null && target.Root.GetComponent<VMGMaskGroup>() != null;
                }
                target.Add(s.name, d);
                if (intoMask)
                {
                    var child = target[s.name];
                    if (child is UnityEngine.UI.Graphic)
                    {
                        if (child.gameObject.GetComponent<VMGMaskClient>() == null)
                            child.gameObject.AddComponent<VMGMaskClient>();
                    }
                }
            }

            static void ApplyMask(MaskStmt s, VMGScene scene, VMGFxCompiled compiled)
            {
                scene.Group(s.name, sub =>
                {
                    // Attach the group marker BEFORE iterating children so
                    // ApplyAdd's mask-client detection works for any nested
                    // `in=<this-mask>` references inside the block (not the
                    // common case, but supported).
                    if (sub.Root != null && sub.Root.GetComponent<VMGMaskGroup>() == null)
                        sub.Root.gameObject.AddComponent<VMGMaskGroup>();

                    foreach (var c in s.children)
                    {
                        if (c is AddStmt addS) ApplyAdd(addS, sub, compiled);
                        else if (c is MaskStmt maskS) ApplyMask(maskS, sub, compiled);
                        else if (c is GroupStmt grpS) ApplyGroup(grpS, sub, compiled);
                    }

                    // After children are materialised, tag each direct-child
                    // Graphic as a mask SOURCE. Children created via
                    // `add` show up under sub.Root as the immediate
                    // descendants.
                    var root = sub.Root;
                    if (root != null)
                    {
                        for (int i = 0; i < root.childCount; i++)
                        {
                            var g = root.GetChild(i).GetComponent<UnityEngine.UI.Graphic>();
                            if (g != null && g.GetComponent<VMGMaskSource>() == null)
                                g.gameObject.AddComponent<VMGMaskSource>();
                        }
                    }
                });
            }

            static void ApplyGroup(GroupStmt s, VMGScene scene, VMGFxCompiled compiled)
            {
                scene.Group(s.name, sub =>
                {
                    foreach (var c in s.children)
                    {
                        if (c is AddStmt addS) ApplyAdd(addS, sub, compiled);
                        else if (c is MaskStmt maskS) ApplyMask(maskS, sub, compiled);
                        else if (c is GroupStmt grpS) ApplyGroup(grpS, sub, compiled);
                    }
                });
            }

            static VMGShapeDescriptor MakeDescriptor(string shape, Dictionary<string, string> attrs, VMGFxCompiled compiled)
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
                        // Prefer the X/Y form when explicit; otherwise the
                        // single-radius form expands to (r, r).
                        if (attrs != null && attrs.TryGetValue("cornerRadii", out var cr2))
                        {
                            if (TryParseVector2(cr2, out var v)) d.CornerRadii(v);
                            else if (TryParseFloat(cr2, out var f)) d.CornerRadius(f);
                        }
                        else if (attrs != null && (attrs.TryGetValue("cornerRadius", out var cr) || attrs.TryGetValue("corner", out cr)))
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
                            d.Closed(ParseFlag(closed));
                        return d;
                    }
                    case "svg":
                    {
                        // `add <name> svg asset=<name> [referenceSize=auto]` —
                        // bind a VMGShapeAsset registered on the VMGAnimator
                        // (or its `assets` dictionary) to the spawned
                        // renderer's SvgAsset slot. The asset= attr accepts
                        // the same asset(name) form as motionPath
                        // path=asset(name) for consistency, and a bare name
                        // as shorthand. referenceSize=auto sizes the host
                        // RectTransform to the asset's SVG viewBox so demos
                        // captured at a known viewport (HtmlCapture --viewport
                        // WxH) come in at their authored pixel dimensions.
                        var d = VMGFx.Svg();
                        if (attrs == null || !attrs.TryGetValue("asset", out var raw))
                        {
                            Debug.LogError("[VMGFx] add svg requires asset=<name> or asset=asset(<name>)");
                            return null;
                        }
                        VMGShapeAsset svgAsset = null;
                        if (TryResolveAssetExpr(raw, compiled, out var obj))
                        {
                            svgAsset = obj as VMGShapeAsset;
                        }
                        else if (compiled != null && compiled.assetLookup != null &&
                                 compiled.assetLookup.TryGetValue(raw.Trim(), out var bareObj))
                        {
                            svgAsset = bareObj as VMGShapeAsset;
                        }
                        if (svgAsset == null)
                        {
                            // Diagnose: show what's actually in the lookup so a
                            // typo, an empty Assets entry, or a wrong asset type
                            // (.svg instead of .vmgshape.asset) is obvious from
                            // the message.
                            string have = "(empty)";
                            if (compiled != null && compiled.assetLookup != null && compiled.assetLookup.Count > 0)
                            {
                                var entries = new List<string>();
                                foreach (var kv in compiled.assetLookup)
                                    entries.Add($"'{kv.Key}'={(kv.Value != null ? kv.Value.GetType().Name : "null")}");
                                have = string.Join(", ", entries);
                            }
                            Debug.LogError($"[VMGFx] add svg: asset='{raw}' not found as a VMGShapeAsset on VMGAnimator.Assets. Registered: {have}");
                            return null;
                        }
                        d.Asset(svgAsset);
                        // referenceSize=auto pulls the captured viewBox onto
                        // the descriptor's Size BEFORE ApplyDescriptorAttrs
                        // runs, so an explicit size= in the same stmt still
                        // wins (Apply iterates attrs and overwrites).
                        if (attrs.TryGetValue("referenceSize", out var refMode))
                        {
                            var mode = (refMode ?? "").Trim().ToLowerInvariant();
                            if (mode == "auto")
                            {
                                ApplySize(d, svgAsset.viewBoxSize);
                            }
                            else if (mode != "" && mode != "off" && mode != "none" && mode != "false")
                            {
                                Debug.LogWarning($"[VMGFx] add svg referenceSize='{refMode}' not recognized (expected 'auto' or 'off')");
                            }
                        }
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
                        case "pivot":
                        {
                            // RectTransform pivot in 0..1 (CSS transform-origin
                            // equivalent for the rotation anchor). Single-value
                            // form `pivot=0.5` sets both axes.
                            if (TryParseVector2(kv.Value, out var v)) ApplyPivot(d, v);
                            else if (TryParseFloat(kv.Value, out var f)) ApplyPivot(d, new Vector2(f, f));
                            break;
                        }
                        case "anchor":
                        {
                            // Single-point anchor (anchorMin == anchorMax).
                            // Lets the rect attach to one spot in the parent's
                            // frame — e.g. anchor=0.5,0 pins to the parent's
                            // bottom-center, so sibling growth shifts this
                            // rect along the parent's bottom edge.
                            if (TryParseVector2(kv.Value, out var v)) ApplyAnchor(d, v);
                            else if (TryParseFloat(kv.Value, out var f)) ApplyAnchor(d, new Vector2(f, f));
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
                            ApplyFitToRect(d, ParseFlag(kv.Value));
                            break;
                        }
                        // shape-specific keys handled in MakeDescriptor:
                        case "sides":
                        case "corner":
                        case "cornerRadius":
                        case "cornerRadii":
                        case "points":
                        case "closed":
                        case "asset":
                        case "referenceSize":
                        // handled in ApplyAdd, not here:
                        case "in":
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
            static void ApplyPivot(VMGShapeDescriptor d, Vector2 v) { d.m_Pivot = v; d.m_HasPivot = true; }
            static void ApplyAnchor(VMGShapeDescriptor d, Vector2 v) { d.m_Anchor = v; d.m_HasAnchor = true; }
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

            static void BuildStandaloneMotionPath(MotionPathStmt mp, VMGScene scene, Transform root, VMGFxCompiled compiled)
            {
                var target = ResolveTarget(mp.target, scene, root, null);
                if (target == null) { Debug.LogError($"[VMGFx] motionPath: target '{mp.target}' not found"); return; }
                var builder = VMGFx.Animate(target);
                ConfigureMotionPath(builder, mp.attrs, compiled);
                compiled.standaloneAnimates.Add(builder);
            }

            static void BuildTimeline(TimelineStmt t, VMGScene scene, Transform root, VMGFxCompiled compiled)
            {
                var tl = VMGFx.Timeline();
                compiled.timelines.Add(tl);
                ApplyTimelineHeaderAttrs(tl, t.attrs, compiled);
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
                        case MotionPathStmt mp:
                        {
                            var target = ResolveTarget(mp.target, scene, root, null);
                            if (target == null) { Debug.LogError($"[VMGFx] motionPath: target '{mp.target}' not found"); break; }
                            var builder = VMGFx.Animate(target);
                            ConfigureMotionPath(builder, mp.attrs, compiled);
                            var at = ExtractAt(mp.attrs);
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
                        case OnStmt onS:
                        {
                            BindTimelineCallback(tl, onS.evt, onS.eventName, compiled, onS.line);
                            break;
                        }
                        case KeyframesStmt kf:
                        {
                            var target = ResolveTarget(kf.target, scene, root, FirstKeyframePath(kf));
                            if (target == null) { Debug.LogError($"[VMGFx] keyframes: target '{kf.target}' not found"); break; }
                            ExpandKeyframesIntoTimeline(tl, kf, target);
                            break;
                        }
                        case StaggerStmt stg:
                        {
                            ExpandStaggerIntoTimeline(tl, stg, scene, root, compiled);
                            break;
                        }
                    }
                }
            }

            // Apply attrs from a `timeline <attrs> { ... }` header. Keys map
            // 1:1 to the corresponding VMGTimeline setter (Duration, Defaults,
            // PlaybackEase, PlaybackRate, Loop, Alternate, etc).
            static void ApplyTimelineHeaderAttrs(VMGTimeline tl, Dictionary<string, string> attrs, VMGFxCompiled compiled)
            {
                if (attrs == null || attrs.Count == 0) return;
                foreach (var kv in attrs)
                {
                    switch (kv.Key)
                    {
                        case "duration": if (TryParseFloat(kv.Value, out var dur)) tl.Duration(dur); break;
                        case "ease": tl.Defaults(ResolveEase(kv.Value)); break;
                        case "playbackEase": tl.PlaybackEase(ResolveEase(kv.Value)); break;
                        case "rate":
                        case "playbackRate": if (TryParseFloat(kv.Value, out var r)) tl.PlaybackRate = r; break;
                        case "loop":
                        {
                            if (string.IsNullOrEmpty(kv.Value) || ParseBool(kv.Value)) tl.Loop();
                            else if (TryParseInt(kv.Value, out var ln)) tl.Loop(ln);
                            break;
                        }
                        case "loopDelay": if (TryParseFloat(kv.Value, out var ld)) tl.LoopDelay(ld); break;
                        case "alternate": tl.Alternate(ParseFlag(kv.Value)); break;
                        case "reversed": tl.Reversed(ParseFlag(kv.Value)); break;
                        case "paused": tl.Paused(ParseFlag(kv.Value)); break;
                        default:
                            Debug.LogWarning($"[VMGFx] unknown timeline attribute '{kv.Key}'");
                            break;
                    }
                }
            }

            // Hook a script event name onto a timeline lifecycle callback.
            // Dispatch goes through VMGFxCompiled.RaiseEvent so the wiring
            // matches `call` exactly — one listener channel for everything
            // user-visible.
            static void BindTimelineCallback(VMGTimeline tl, string evt, string eventName, VMGFxCompiled compiled, int line)
            {
                Action handler = () => compiled.RaiseEvent(eventName);
                switch (evt)
                {
                    case "begin": tl.OnBegin(handler); break;
                    case "beforeUpdate": tl.OnBeforeUpdate(handler); break;
                    case "update": tl.OnUpdate(handler); break;
                    case "render": tl.OnRender(handler); break;
                    case "loop": tl.OnLoop(handler); break;
                    case "complete": tl.OnComplete(handler); break;
                    case "pause": tl.OnPause(handler); break;
                    default:
                        Debug.LogError($"[VMGFx] line {line}: unknown event '{evt}' (expected begin/beforeUpdate/update/render/loop/complete/pause)");
                        break;
                }
            }

            // Pick any non-attr key in the first keyframe to disambiguate
            // renderer-vs-Transform target resolution. ResolveTarget only
            // needs *a* candidate path for that decision.
            static string FirstKeyframePath(KeyframesStmt kf)
            {
                if (kf.frames == null || kf.frames.Count == 0) return null;
                foreach (var f in kf.frames)
                {
                    if (f.values == null) continue;
                    foreach (var kv in f.values) return kv.Key;
                }
                return null;
            }

            // Expand a CSS-style keyframes block into a series of segment
            // tweens added to the enclosing timeline. Each (channel, gap)
            // pair becomes one FromTo animate of duration =
            // (pct_to - pct_from)/100 * blockDuration, positioned at
            // pct_from/100 * blockDuration from the timeline anchor (default
            // anchor = current timeline end).
            //
            // CSS semantics: a channel that's not redefined at an intermediate
            // keyframe holds its last defined value. We emit a segment only
            // between adjacent frames that *both* define the channel; gaps
            // around missing frames are absorbed by holding the last value
            // (the engine does this naturally — no segment, no write).
            //
            // The block-level `at=` attr (optional) positions the entire
            // block within the parent timeline. Defaults to End().
            static void ExpandKeyframesIntoTimeline(VMGTimeline tl, KeyframesStmt kf, Component target)
            {
                if (kf.frames == null || kf.frames.Count == 0) return;

                // Block attrs.
                float blockDuration = 1f;
                VMGEase blockEase = VMGEase.Linear;
                bool hasBlockEase = false;
                VMGAt blockAt = VMGAt.End();
                int loopCount = 1;
                bool loopInfinite = false;
                bool alternate = false;
                float delay = 0f;
                if (kf.attrs != null)
                {
                    foreach (var kv in kf.attrs)
                    {
                        switch (kv.Key)
                        {
                            case "duration": if (TryParseFloat(kv.Value, out var d)) blockDuration = Mathf.Max(0.0001f, d); break;
                            case "ease": blockEase = ResolveEase(kv.Value); hasBlockEase = true; break;
                            case "at": blockAt = string.IsNullOrEmpty(kv.Value) ? VMGAt.End() : VMGAt.Parse(kv.Value); break;
                            case "loop":
                            {
                                if (string.IsNullOrEmpty(kv.Value) || ParseBool(kv.Value)) loopInfinite = true;
                                else if (TryParseInt(kv.Value, out var ln)) loopCount = Mathf.Max(1, ln);
                                break;
                            }
                            case "alternate": alternate = ParseFlag(kv.Value); break;
                            case "delay": if (TryParseFloat(kv.Value, out var dl)) delay = Mathf.Max(0f, dl); break;
                            default:
                                Debug.LogWarning($"[VMGFx] unknown keyframes attribute '{kv.Key}'");
                                break;
                        }
                    }
                }

                // Capture the anchor BEFORE adding any segments. As we add
                // segments to the timeline its iterationDuration grows, so we
                // must lock the block's start once and reference it for every
                // segment instead of re-reading tl.iterationDuration. Without
                // this, channel-2's segments would land after channel-1's
                // segments end instead of overlapping in time.
                float anchorSec;
                if (blockAt.kind == VMGAt.Kind.Absolute) anchorSec = blockAt.offset;
                else anchorSec = tl.iterationDuration;
                anchorSec += delay;

                // Sort frames by pct.
                kf.frames.Sort((a, b) => a.pct.CompareTo(b.pct));

                // Collect channel set (union of all keys across frames).
                var channels = new List<string>();
                var seen = new HashSet<string>();
                foreach (var f in kf.frames)
                {
                    foreach (var kv in f.values)
                    {
                        if (seen.Add(kv.Key)) channels.Add(kv.Key);
                    }
                }

                // Handle loop/alternate for in-timeline blocks by emitting
                // multiple copies inline. A child-timeline approach doesn't
                // work cleanly here because the parent's Seek clamps each
                // child to one iteration's worth of time, so a looping child
                // timeline never advances past iteration 0. Inline copies
                // sidestep that by laying every cycle on the parent's clock.
                if (loopInfinite)
                {
                    Debug.LogError("[VMGFx] keyframes 'loop' (infinite) inside a timeline is not supported — repetition span is undefined when the parent doesn't loop. Use 'loop=<count>' to expand inline, or move the keyframes outside the timeline and loop the whole timeline.");
                    return;
                }
                int cycles = Mathf.Max(1, loopCount);

                // For each channel, walk adjacent frame pairs that BOTH define
                // it and emit a FromTo segment animate. With cycles > 1 we
                // lay them back-to-back; with `alternate` every odd cycle
                // swaps from/to.
                foreach (var path in channels)
                {
                    var channelType = ResolveChannelType(target, path);

                    for (int cycle = 0; cycle < cycles; cycle++)
                    {
                        float cycleStartSec = anchorSec + cycle * blockDuration;
                        bool reverseCycle = alternate && (cycle & 1) == 1;

                        Keyframe prev = null;
                        foreach (var cur in kf.frames)
                        {
                            if (!cur.values.ContainsKey(path)) continue;
                            if (prev == null) { prev = cur; continue; }

                            // Emit prev → cur segment.
                            string fromRaw = prev.values[path];
                            string toRaw = cur.values[path];
                            float segStartPct = prev.pct;
                            float segEndPct = cur.pct;
                            float segDurationSec = Mathf.Max(0.0001f, (segEndPct - segStartPct) / 100f * blockDuration);
                            float segStartSec = reverseCycle
                                ? (100f - segEndPct) / 100f * blockDuration
                                : segStartPct / 100f * blockDuration;

                            var seg = VMGFx.Animate(target);
                            // Swap from/to on reverse cycles so the channel
                            // plays back the other way through the same frames.
                            if (reverseCycle) ApplyTypedFromTo(seg, path, channelType, toRaw, fromRaw);
                            else ApplyTypedFromTo(seg, path, channelType, fromRaw, toRaw);
                            seg.Duration(segDurationSec);
                            // Per-segment ease = the *target* (cur) frame's ease
                            // override if set, else the block ease. Matches CSS's
                            // "animation-timing-function on a keyframe applies to
                            // the segment ENDING at that keyframe" rule (anime.js
                            // does the same).
                            if (!string.IsNullOrEmpty(cur.easeOverride)) seg.Ease(ResolveEase(cur.easeOverride));
                            else if (hasBlockEase) seg.Ease(blockEase);

                            // All segments anchor against anchorSec captured at
                            // function entry. Critical: do NOT re-read
                            // tl.iterationDuration here — it grows with each Add
                            // and would push later channels past the first.
                            tl.Add(seg, VMGAt.Time(cycleStartSec + segStartSec));

                            prev = cur;
                        }
                    }
                }
            }

            // FromTo variant of ApplyTypedTo. Always emits a FromTo for the
            // given channel type; required by keyframe expansion because each
            // segment has an explicit from and to.
            static void ApplyTypedFromTo(VMGAnimate builder, string path, VMGChannelType type, string fromRaw, string toRaw)
            {
                switch (type)
                {
                    case VMGChannelType.Float:
                    {
                        var fromFn = TryParseFloatGenerator(fromRaw);
                        var toFn = TryParseFloatGenerator(toRaw);
                        if (fromFn != null || toFn != null)
                        {
                            if (fromFn == null && !TryParseFloat(fromRaw, out var fLit)) break;
                            else if (fromFn == null) { float c = 0f; TryParseFloat(fromRaw, out c); var captured = c; fromFn = () => captured; }
                            if (toFn == null && !TryParseFloat(toRaw, out var tLit)) break;
                            else if (toFn == null) { float c = 0f; TryParseFloat(toRaw, out c); var captured = c; toFn = () => captured; }
                            builder.FromTo(path, fromFn, toFn);
                            break;
                        }
                        if (TryParseFloat(fromRaw, out var ff) && TryParseFloat(toRaw, out var ft)) builder.FromTo(path, ff, ft);
                        break;
                    }
                    case VMGChannelType.Int:
                    {
                        var fromFn = TryParseIntGenerator(fromRaw);
                        var toFn = TryParseIntGenerator(toRaw);
                        if (fromFn != null || toFn != null)
                        {
                            if (fromFn == null && !TryParseInt(fromRaw, out _)) break;
                            else if (fromFn == null) { int c = 0; TryParseInt(fromRaw, out c); var captured = c; fromFn = () => captured; }
                            if (toFn == null && !TryParseInt(toRaw, out _)) break;
                            else if (toFn == null) { int c = 0; TryParseInt(toRaw, out c); var captured = c; toFn = () => captured; }
                            builder.FromTo(path, fromFn, toFn);
                            break;
                        }
                        if (TryParseInt(fromRaw, out var ifr) && TryParseInt(toRaw, out var it)) builder.FromTo(path, ifr, it);
                        break;
                    }
                    case VMGChannelType.Bool:
                        builder.FromTo(path, ParseBool(fromRaw), ParseBool(toRaw));
                        break;
                    case VMGChannelType.Color:
                        if (TryParseColor(fromRaw, out var cf) && TryParseColor(toRaw, out var ct)) builder.FromTo(path, cf, ct);
                        break;
                    case VMGChannelType.Vector2:
                        if (TryParseVector2(fromRaw, out var v2f) && TryParseVector2(toRaw, out var v2t)) builder.FromTo(path, v2f, v2t);
                        break;
                    case VMGChannelType.Vector3:
                        if (TryParseVector3(fromRaw, out var v3f) && TryParseVector3(toRaw, out var v3t)) builder.FromTo(path, v3f, v3t);
                        break;
                    case VMGChannelType.Vector4:
                        if (TryParseVector4(fromRaw, out var v4f) && TryParseVector4(toRaw, out var v4t)) builder.FromTo(path, v4f, v4t);
                        break;
                }
            }

            // Resolve an ease attribute value. Accepts:
            //   - preset name: "outQuad", "linear", "spring"
            //   - cubicBezier(p1x,p1y,p2x,p2y) — CSS-compatible
            //   - steps(N) — CSS step ease, mapped to N quantized samples
            //   - spring(stiffness, damping, mass, velocity) — 1..4 args,
            //     trailing args default per VMGEase.Spring(); bare `spring`
            //     keeps working via VMGEase.From below.
            // Unknown forms fall back to Linear via VMGEase.From, which is
            // intentional LLM-friendly behaviour.
            static VMGEase ResolveEase(string raw)
            {
                if (string.IsNullOrEmpty(raw)) return VMGEase.Linear;
                // Function-form: name(arg, arg, ...)
                int open = raw.IndexOf('(');
                if (open > 0 && raw[raw.Length - 1] == ')')
                {
                    string fname = raw.Substring(0, open);
                    string args = raw.Substring(open + 1, raw.Length - open - 2);
                    var parts = SplitTuple(args);
                    if (fname == "cubicBezier" || fname == "bezier")
                    {
                        if (parts.Count >= 4 &&
                            TryParseFloat(parts[0], out var x1) && TryParseFloat(parts[1], out var y1) &&
                            TryParseFloat(parts[2], out var x2) && TryParseFloat(parts[3], out var y2))
                        {
                            return VMGEase.Bezier(x1, y1, x2, y2);
                        }
                        Debug.LogWarning($"[VMGFx] cubicBezier expects 4 numeric args, got '{raw}'");
                        return VMGEase.Linear;
                    }
                    if (fname == "steps")
                    {
                        if (parts.Count >= 1 && TryParseInt(parts[0], out var n) && n >= 1)
                            return BuildStepsEase(n);
                        Debug.LogWarning($"[VMGFx] steps expects a positive integer, got '{raw}'");
                        return VMGEase.Linear;
                    }
                    if (fname == "spring")
                    {
                        // Argument order matches VMGEase.Spring(stiffness,
                        // damping, mass, velocity). 0..4 positional args;
                        // missing trailing args take the C# default. Bare
                        // `spring` keeps working via VMGEase.From below.
                        // Empty/whitespace-only inside parens collapses to
                        // "0 args" so `spring( )` == `spring()` == `spring`.
                        float stiffness = 100f, damping = 10f, mass = 1f, velocity = 0f;
                        int n = (parts.Count == 1 && string.IsNullOrWhiteSpace(parts[0])) ? 0 : parts.Count;
                        bool ok = n <= 4;
                        if (ok && n >= 1) ok &= TryParseFloat(parts[0].Trim(), out stiffness);
                        if (ok && n >= 2) ok &= TryParseFloat(parts[1].Trim(), out damping);
                        if (ok && n >= 3) ok &= TryParseFloat(parts[2].Trim(), out mass);
                        if (ok && n >= 4) ok &= TryParseFloat(parts[3].Trim(), out velocity);
                        if (ok) return VMGEase.Spring(stiffness, damping, mass, velocity);
                        Debug.LogWarning($"[VMGFx] spring expects up to 4 numeric args (stiffness, damping, mass, velocity), got '{raw}'");
                        return VMGEase.Linear;
                    }
                }
                return VMGEase.From(raw);
            }

            // CSS `steps(N)` collapses to Hold (constant-until-end) for
            // now — VMGEase has no native staircase curve, and the engine
            // assumes a continuous tangent pair. N is accepted for forward
            // compatibility but ignored. A future round can extend VMGEase
            // with a staircase Kind.
            static VMGEase BuildStepsEase(int n)
            {
                return VMGEase.From(VMGEasingPreset.Hold);
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

                if (name == "/" || name == "" || name == "root" || name == "self")
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
            // wins for the reserved local* leaves regardless of whether the
            // primary happens to resolve a same-named member, and otherwise
            // when the primary doesn't resolve the path at all.
            static Component PickByPath(Component primary, Transform tr, string path)
            {
                if (primary == null) return tr;
                if (string.IsNullOrEmpty(path)) return primary;
                if (tr != null && IsTransformReservedPath(path)) return tr;
                if (VMG.Animation.VMGFieldPathCompiler.TryCompile(primary.GetType(), path, out _, out _))
                    return primary;
                if (tr != null && VMG.Animation.VMGFieldPathCompiler.TryCompile(tr.GetType(), path, out _, out _))
                    return tr;
                return primary;
            }

            // Paths whose first segment names a universal Transform channel.
            // Anchored here (not in VMGFieldPathCompiler) because this is a
            // target-routing rule, not a member-resolution rule.
            static bool IsTransformReservedPath(string path)
            {
                if (string.IsNullOrEmpty(path)) return false;
                int dot = path.IndexOf('.');
                string head = dot < 0 ? path : path.Substring(0, dot);
                switch (head)
                {
                    case "localPosition":
                    case "localScale":
                    case "localRotation":
                    case "localEulerAngles":
                        return true;
                    default:
                        return false;
                }
            }

            // motionPath statement → builder.AlongPath(points, closed) plus
            // optional .AutoRotate(offset). Standard animate-attrs (duration,
            // ease, delay, endDelay, loop, alternate) are accepted and apply
            // to the underlying motion path tween. `at` is consumed by
            // ExtractAt at the caller. Asset-mode binding (`asset=`,
            // `subShape=`) is deferred to a future DSL round.
            // Recognise `asset(name)` and return the registered Object, or
            // false if the expression is not in asset(...) form or the name
            // is not in the compiled lookup. Whitespace inside the parens is
            // tolerated (the tokenizer preserves it because of paren-aware
            // value mode).
            static bool TryResolveAssetExpr(string raw, VMGFxCompiled compiled, out UnityEngine.Object asset)
            {
                asset = null;
                if (string.IsNullOrEmpty(raw)) return false;
                int open = raw.IndexOf('(');
                if (open < 0 || raw[raw.Length - 1] != ')') return false;
                if (raw.Substring(0, open).Trim() != "asset") return false;
                string name = raw.Substring(open + 1, raw.Length - open - 2).Trim();
                // Allow optional quotes so authors can write either
                // asset(myCurve) or asset("myCurve") — both feel natural.
                if (name.Length >= 2 &&
                    ((name[0] == '"' && name[name.Length - 1] == '"') ||
                     (name[0] == '\'' && name[name.Length - 1] == '\'')))
                {
                    name = name.Substring(1, name.Length - 2);
                }
                if (string.IsNullOrEmpty(name)) return false;
                if (compiled == null || compiled.assetLookup == null) return false;
                return compiled.assetLookup.TryGetValue(name, out asset) && asset != null;
            }

            static void ConfigureMotionPath(VMGAnimate builder, Dictionary<string, string> attrs, VMGFxCompiled compiled)
            {
                if (attrs == null) { Debug.LogError("[VMGFx] motionPath requires points=... or path=asset(name); got no attributes"); return; }

                // `path=asset(name)` takes priority over inline `points=...`.
                // The two are mutually exclusive — if both are present, the
                // asset wins and we log a warning. Order of definition would
                // be ambiguous otherwise (asset is a single curve, points is
                // a polyline — there is no sensible merge).
                int subShapeIndex = 0;
                if (attrs.TryGetValue("subShape", out var subRaw) && TryParseInt(subRaw, out var subParsed))
                    subShapeIndex = subParsed;

                if (attrs.TryGetValue("path", out var pathRaw) && !string.IsNullOrEmpty(pathRaw))
                {
                    if (attrs.ContainsKey("points"))
                        Debug.LogWarning("[VMGFx] motionPath: both 'path' and 'points' supplied; using 'path' and ignoring 'points'");
                    if (!TryResolveAssetExpr(pathRaw, compiled, out var assetObj))
                    {
                        Debug.LogError($"[VMGFx] motionPath: path='{pathRaw}' is not an asset(...) reference or the named asset is not registered on this VMGAnimator");
                        return;
                    }
                    if (!(assetObj is VMGShapeAsset shape))
                    {
                        Debug.LogError($"[VMGFx] motionPath: asset bound to '{pathRaw}' is {assetObj.GetType().Name}, expected VMGShapeAsset");
                        return;
                    }
                    builder.AlongPath(shape, subShapeIndex);
                }
                else
                {
                    List<Vector2> pts = null;
                    bool closed = false;
                    if (attrs.TryGetValue("points", out var ptsRaw) && !string.IsNullOrEmpty(ptsRaw))
                    {
                        var parts = ptsRaw.Split(',');
                        pts = new List<Vector2>();
                        for (int i = 0; i + 1 < parts.Length; i += 2)
                        {
                            if (TryParseFloat(parts[i], out var x) && TryParseFloat(parts[i + 1], out var y))
                                pts.Add(new Vector2(x, y));
                        }
                    }
                    if (attrs.TryGetValue("closed", out var closedRaw)) closed = ParseFlag(closedRaw);

                    if (pts == null || pts.Count == 0)
                    {
                        Debug.LogError("[VMGFx] motionPath requires points=x1,y1,x2,y2,... with at least one point, or path=asset(name)");
                        return;
                    }
                    builder.AlongPath(pts, closed);
                }

                // autoRotate: bare key = true with offset 0, value =
                // true/false, value = number → offsetDeg (anime.js parity).
                if (attrs.TryGetValue("autoRotate", out var arRaw))
                {
                    if (string.IsNullOrEmpty(arRaw) || ParseBool(arRaw)) builder.AutoRotate();
                    else if (TryParseFloat(arRaw, out var deg)) builder.AutoRotate(deg);
                }

                foreach (var kv in attrs)
                {
                    switch (kv.Key)
                    {
                        case "duration": if (TryParseFloat(kv.Value, out var dur)) builder.Duration(dur); break;
                        case "delay": if (TryParseFloat(kv.Value, out var dl)) builder.Delay(dl); break;
                        case "endDelay": if (TryParseFloat(kv.Value, out var ed)) builder.EndDelay(ed); break;
                        case "ease": builder.Ease(ResolveEase(kv.Value)); break;
                        case "loop":
                        {
                            if (string.IsNullOrEmpty(kv.Value) || ParseBool(kv.Value)) builder.Loop();
                            else if (TryParseInt(kv.Value, out var ln)) builder.Loop(ln);
                            break;
                        }
                        case "alternate": builder.Alternate(ParseFlag(kv.Value)); break;
                        case "points":
                        case "closed":
                        case "autoRotate":
                        case "at":
                        case "path":
                        case "subShape":
                            break; // handled above / by caller
                        default:
                            Debug.LogWarning($"[VMGFx] unknown motionPath attribute '{kv.Key}'");
                            break;
                    }
                }
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
                        case "ease": builder.Ease(ResolveEase(kv.Value)); break;
                        case "loop":
                        {
                            if (string.IsNullOrEmpty(kv.Value) || ParseBool(kv.Value)) builder.Loop();
                            else if (TryParseInt(kv.Value, out var ln)) builder.Loop(ln);
                            break;
                        }
                        case "alternate": builder.Alternate(ParseFlag(kv.Value)); break;
                        case "refreshOnLoop":
                            // anime.js parity: re-evaluate Func<T> tween values at every
                            // loop boundary. Bare key (no value) means on; `=true/false`
                            // gives explicit control.
                            builder.RefreshOnLoop(ParseFlag(kv.Value));
                            break;
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
                        var toFn = TryParseFloatGenerator(toRaw);
                        var fromFn = fromRaw != null ? TryParseFloatGenerator(fromRaw) : null;
                        if (toFn != null)
                        {
                            if (fromFn != null) builder.FromTo(path, fromFn, toFn);
                            else if (fromRaw != null && TryParseFloat(fromRaw, out var fromLit)) builder.FromTo(path, () => fromLit, toFn);
                            else builder.To(path, toFn);
                            break;
                        }
                        if (!TryParseFloat(toRaw, out var to)) return;
                        if (fromFn != null) builder.FromTo(path, fromFn, () => to);
                        else if (fromRaw != null && TryParseFloat(fromRaw, out var from)) builder.FromTo(path, from, to);
                        else builder.To(path, to);
                        break;
                    }
                    case VMGChannelType.Int:
                    {
                        var toFn = TryParseIntGenerator(toRaw);
                        var fromFn = fromRaw != null ? TryParseIntGenerator(fromRaw) : null;
                        if (toFn != null)
                        {
                            if (fromFn != null) builder.FromTo(path, fromFn, toFn);
                            else if (fromRaw != null && TryParseInt(fromRaw, out var fromLit)) builder.FromTo(path, () => fromLit, toFn);
                            else builder.To(path, toFn);
                            break;
                        }
                        if (!TryParseInt(toRaw, out var to)) return;
                        if (fromFn != null) builder.FromTo(path, fromFn, () => to);
                        else if (fromRaw != null && TryParseInt(fromRaw, out var from)) builder.FromTo(path, from, to);
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

        // ----- Generator helpers (FunctionValue) -----

        // Recognise `random(min, max [, seed])` / `rangeInt(min, max [, seed])`
        // at a value position and emit a Func<float>/Func<int> that the
        // builder consumes via VMGAnimate.To(Func<T>) / .FromTo(Func<T>, ...).
        // Seed is optional; supplying it gives deterministic sequences via a
        // captured System.Random. Without it the helper falls back to
        // UnityEngine.Random (global, non-deterministic — matches the
        // anime.js default).
        //
        // `random` returns a continuous float in [min, max].
        // `rangeInt` returns an integer in [min, max] (inclusive on both
        // ends — anime.js convention).
        static Func<float> TryParseFloatGenerator(string raw)
        {
            if (!TryParseGenerator(raw, out var fname, out var parts)) return null;
            if (fname == "random")
            {
                if (parts.Count < 2 || parts.Count > 3) { Debug.LogWarning($"[VMGFx] random expects (min, max [, seed]), got '{raw}'"); return null; }
                if (!TryParseFloat(parts[0].Trim(), out var lo) || !TryParseFloat(parts[1].Trim(), out var hi))
                { Debug.LogWarning($"[VMGFx] random: non-numeric range in '{raw}'"); return null; }
                if (parts.Count == 3)
                {
                    if (!TryParseInt(parts[2].Trim(), out var seed)) { Debug.LogWarning($"[VMGFx] random seed must be integer, got '{raw}'"); return null; }
                    var rng = new System.Random(seed);
                    return () => lo + (float)rng.NextDouble() * (hi - lo);
                }
                return () => UnityEngine.Random.Range(lo, hi);
            }
            if (fname == "rangeInt")
            {
                if (parts.Count < 2 || parts.Count > 3) { Debug.LogWarning($"[VMGFx] rangeInt expects (min, max [, seed]), got '{raw}'"); return null; }
                if (!TryParseInt(parts[0].Trim(), out var lo) || !TryParseInt(parts[1].Trim(), out var hi))
                { Debug.LogWarning($"[VMGFx] rangeInt: non-integer range in '{raw}'"); return null; }
                if (parts.Count == 3)
                {
                    if (!TryParseInt(parts[2].Trim(), out var seed)) { Debug.LogWarning($"[VMGFx] rangeInt seed must be integer, got '{raw}'"); return null; }
                    var rng = new System.Random(seed);
                    return () => lo + (float)(rng.Next(0, (hi - lo) + 1));
                }
                return () => UnityEngine.Random.Range(lo, hi + 1);
            }
            return null;
        }

        static Func<int> TryParseIntGenerator(string raw)
        {
            if (!TryParseGenerator(raw, out var fname, out var parts)) return null;
            if (fname == "rangeInt")
            {
                if (parts.Count < 2 || parts.Count > 3) { Debug.LogWarning($"[VMGFx] rangeInt expects (min, max [, seed]), got '{raw}'"); return null; }
                if (!TryParseInt(parts[0].Trim(), out var lo) || !TryParseInt(parts[1].Trim(), out var hi))
                { Debug.LogWarning($"[VMGFx] rangeInt: non-integer range in '{raw}'"); return null; }
                if (parts.Count == 3)
                {
                    if (!TryParseInt(parts[2].Trim(), out var seed)) { Debug.LogWarning($"[VMGFx] rangeInt seed must be integer, got '{raw}'"); return null; }
                    var rng = new System.Random(seed);
                    return () => lo + rng.Next(0, (hi - lo) + 1);
                }
                return () => UnityEngine.Random.Range(lo, hi + 1);
            }
            // `random(...)` is also valid on Int channels — truncates the
            // float result. Useful for non-integer-only fields that happen
            // to be ints.
            if (fname == "random")
            {
                var ff = TryParseFloatGenerator(raw);
                if (ff == null) return null;
                return () => Mathf.RoundToInt(ff());
            }
            return null;
        }

        // Match `name(args)` at value position. Returns false for non-generator
        // strings so callers fall back to literal parsing.
        static bool TryParseGenerator(string raw, out string fname, out List<string> parts)
        {
            fname = null; parts = null;
            if (string.IsNullOrEmpty(raw)) return false;
            int open = raw.IndexOf('(');
            if (open <= 0 || raw[raw.Length - 1] != ')') return false;
            fname = raw.Substring(0, open);
            if (fname != "random" && fname != "rangeInt") return false;
            string args = raw.Substring(open + 1, raw.Length - open - 2);
            parts = SplitTuple(args);
            return true;
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

        // Flag-style attr: bare key (empty value) means "on"; explicit
        // `=true/false/yes/...` honoured. Used by attrs the user can write
        // either as `alternate` or `alternate=false`.
        static bool ParseFlag(string s) => string.IsNullOrEmpty(s) || ParseBool(s);

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

        // Optional name→Object map populated by the Compile entry point.
        // Statements reference entries via `asset(name)` in their value
        // position. Null when no registry was supplied; callers should
        // check before consuming.
        internal IReadOnlyDictionary<string, UnityEngine.Object> assetLookup;

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
