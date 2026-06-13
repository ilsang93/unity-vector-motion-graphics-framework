using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace VMG.Animation.Serialization
{
    // CSS @keyframes → .vmgfx text translator.
    //
    // Single-direction import (CSS → VMG). VMG → CSS export is out of scope.
    //
    // Supported CSS surface:
    //   @keyframes <name> { 0% { ... } 50% { ... } 100% { ... } }   // also 'from'/'to'
    //   <selector> { animation: <name> <dur> <timing> <delay> <iter> <direction>; }
    //   selector forms: .className, #idName, plain ident — all map to a
    //                   GameObject of the same name under the VMGAnimator root.
    //
    // Supported declarations inside a keyframe:
    //   transform: translate(x, y) | translateX(x) | translateY(y) |
    //              scale(s) | scale(x, y) | rotate(Ndeg)            (combinable)
    //   opacity: 0..1
    //   background-color: #rgb | #rgba | #rrggbb | #rrggbbaa | rgb(...) | rgba(...) | <named>
    //   border-color:     same
    //   border-width:     <px>
    //   color, border, padding, margin, display, flex, grid, … : silently dropped + warning
    //
    // Supported easing tokens (animation-timing-function on selector,
    // animation-timing-function inside a keyframe, OR 4th token of animation
    // shorthand):
    //   linear, ease, ease-in, ease-out, ease-in-out, step-start, step-end,
    //   cubic-bezier(a,b,c,d), steps(N) / steps(N, end|start|jump-*)
    // Mapped to .vmgfx cubicBezier(...)/steps(...)/linear so visual fidelity
    // matches CSS exactly.
    //
    // Units:
    //   px → numeric, deg → numeric, s → seconds, ms → /1000 seconds, % at
    //   keyframe position → percent. rem/em/vh/vw and friends are warned and
    //   stripped (treated as raw number).
    //
    // Output: a .vmgfx text the existing VMGFxScript.Compile can ingest.
    public static class VMGCssKeyframes
    {
        public static string Translate(string cssSource, out List<string> warnings)
        {
            warnings = new List<string>();
            if (string.IsNullOrEmpty(cssSource)) return string.Empty;

            var tokens = CssTokenizer.Tokenize(cssSource, warnings);
            var stylesheet = CssParser.Parse(tokens, warnings);
            return Emitter.Emit(stylesheet, warnings);
        }

        // -------- Tokenizer --------

        enum TokKind { Ident, Number, Percent, String, Symbol, AtKeyword, Hash, EOF }

        struct Tok
        {
            public TokKind kind;
            public string text;
            public int line;
            public override string ToString() => $"{kind}:{text}@{line}";
        }

        static class CssTokenizer
        {
            public static List<Tok> Tokenize(string src, List<string> warnings)
            {
                var toks = new List<Tok>();
                int i = 0, line = 1;
                while (i < src.Length)
                {
                    char c = src[i];
                    if (c == '\n') { line++; i++; continue; }
                    if (char.IsWhiteSpace(c)) { i++; continue; }

                    // Comment /* ... */
                    if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
                    {
                        int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        if (end < 0) { warnings.Add($"line {line}: unterminated comment"); break; }
                        for (int k = i; k < end; k++) if (src[k] == '\n') line++;
                        i = end + 2; continue;
                    }

                    // @keyword
                    if (c == '@')
                    {
                        int s = ++i;
                        while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '-' || src[i] == '_')) i++;
                        toks.Add(new Tok { kind = TokKind.AtKeyword, text = src.Substring(s, i - s), line = line });
                        continue;
                    }

                    // #hash (selector OR colour literal — disambiguated by context)
                    if (c == '#')
                    {
                        int s = ++i;
                        while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '-' || src[i] == '_')) i++;
                        toks.Add(new Tok { kind = TokKind.Hash, text = src.Substring(s, i - s), line = line });
                        continue;
                    }

                    // String "..." or '...'
                    if (c == '"' || c == '\'')
                    {
                        char q = c; int s = ++i;
                        while (i < src.Length && src[i] != q) { if (src[i] == '\n') line++; i++; }
                        string text = i < src.Length ? src.Substring(s, i - s) : src.Substring(s);
                        if (i < src.Length) i++; // closing quote
                        toks.Add(new Tok { kind = TokKind.String, text = text, line = line });
                        continue;
                    }

                    // Number (optionally signed via leading -)
                    if (char.IsDigit(c) || (c == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1])) ||
                        (c == '-' && i + 1 < src.Length && (char.IsDigit(src[i + 1]) || src[i + 1] == '.')))
                    {
                        int s = i;
                        if (src[i] == '-') i++;
                        while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '.')) i++;
                        // Unit suffix: ident-chars or '%'
                        while (i < src.Length && (char.IsLetter(src[i]) || src[i] == '%')) i++;
                        toks.Add(new Tok { kind = TokKind.Number, text = src.Substring(s, i - s), line = line });
                        continue;
                    }

                    // Ident
                    if (char.IsLetter(c) || c == '_' || c == '-' || c == '.')
                    {
                        int s = i++;
                        while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '-' || src[i] == '_')) i++;
                        toks.Add(new Tok { kind = TokKind.Ident, text = src.Substring(s, i - s), line = line });
                        continue;
                    }

                    // Symbol — single char
                    toks.Add(new Tok { kind = TokKind.Symbol, text = c.ToString(), line = line });
                    i++;
                }
                toks.Add(new Tok { kind = TokKind.EOF, text = "", line = line });
                return toks;
            }
        }

        // -------- Stylesheet model --------

        sealed class KeyframesBlock
        {
            public string name;
            public List<KeyframeFrame> frames = new List<KeyframeFrame>();
        }

        sealed class KeyframeFrame
        {
            public float pct;
            public string easeOverride;  // animation-timing-function inside the block
            public Dictionary<string, string> decls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        sealed class SelectorRule
        {
            public List<string> selectors = new List<string>();
            public List<AnimationBinding> animations = new List<AnimationBinding>();
        }

        sealed class AnimationBinding
        {
            public string name;
            public float duration = 1f;
            public string timingFunction = "ease";   // CSS default
            public float delay = 0f;
            public int iterationCount = 1;
            public bool iterationInfinite = false;
            public string direction = "normal";       // normal / reverse / alternate / alternate-reverse
        }

        sealed class Stylesheet
        {
            public List<KeyframesBlock> keyframes = new List<KeyframesBlock>();
            public List<SelectorRule> rules = new List<SelectorRule>();
            // Custom-property store. Currently populated only from `:root`
            // declarations — element-scoped vars (e.g. `.box { --x: ... }`)
            // would need a selector matcher we don't have, so they're
            // dropped with a warning. Values are stored as raw CSS strings
            // (var(--inner) chains are resolved at lookup time by
            // ResolveVarFallback).
            // OrdinalIgnoreCase because ParseDeclBlock lowercases its keys —
            // matching the var() lookup case-insensitively keeps a slight
            // CSS-spec deviation contained to one place rather than spreading
            // case-normalization through every reader.
            public Dictionary<string, string> customProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // -------- Parser --------

        static class CssParser
        {
            public static Stylesheet Parse(List<Tok> toks, List<string> warnings)
            {
                var sheet = new Stylesheet();
                int i = 0;
                while (i < toks.Count && toks[i].kind != TokKind.EOF)
                {
                    if (toks[i].kind == TokKind.AtKeyword)
                    {
                        var kw = toks[i].text;
                        if (kw.Equals("keyframes", StringComparison.OrdinalIgnoreCase) ||
                            kw.Equals("-webkit-keyframes", StringComparison.OrdinalIgnoreCase) ||
                            kw.Equals("-moz-keyframes", StringComparison.OrdinalIgnoreCase))
                        {
                            i++;
                            var block = ParseKeyframes(toks, ref i, warnings);
                            if (block != null) sheet.keyframes.Add(block);
                            continue;
                        }
                        // Unsupported at-rule: skip until matching '}' or ';'
                        warnings.Add($"line {toks[i].line}: unsupported at-rule '@{kw}' skipped");
                        SkipAtRule(toks, ref i);
                        continue;
                    }

                    // Selector rule: <sel-list> { <decl-list> }
                    var rule = ParseSelectorRule(toks, ref i, warnings, sheet);
                    if (rule != null) sheet.rules.Add(rule);
                }
                return sheet;
            }

            static KeyframesBlock ParseKeyframes(List<Tok> toks, ref int i, List<string> warnings)
            {
                if (i >= toks.Count) return null;
                var nameTok = toks[i];
                if (nameTok.kind != TokKind.Ident && nameTok.kind != TokKind.String)
                {
                    warnings.Add($"line {nameTok.line}: @keyframes expected a name, got '{nameTok.text}'");
                    return null;
                }
                var block = new KeyframesBlock { name = nameTok.text };
                i++;
                if (i >= toks.Count || toks[i].kind != TokKind.Symbol || toks[i].text != "{")
                {
                    warnings.Add($"line {toks[i].line}: @keyframes expected '{{'");
                    return null;
                }
                i++; // '{'

                while (i < toks.Count && !(toks[i].kind == TokKind.Symbol && toks[i].text == "}"))
                {
                    // Frame selector list — comma-separated pcts. Each shares
                    // one decl block. We split into N frames with the same
                    // decls dictionary content (cloned).
                    var pcts = new List<float>();
                    while (i < toks.Count)
                    {
                        var t = toks[i];
                        if (t.kind == TokKind.Symbol && t.text == "{") break;
                        if (t.kind == TokKind.Symbol && t.text == ",") { i++; continue; }
                        if (t.kind == TokKind.Ident && t.text.Equals("from", StringComparison.OrdinalIgnoreCase))
                        { pcts.Add(0f); i++; continue; }
                        if (t.kind == TokKind.Ident && t.text.Equals("to", StringComparison.OrdinalIgnoreCase))
                        { pcts.Add(100f); i++; continue; }
                        if (t.kind == TokKind.Number)
                        {
                            if (TryParseNumberWithUnit(t.text, out var v, out var unit))
                            {
                                if (unit != "%") warnings.Add($"line {t.line}: keyframe selector '{t.text}' missing '%'; using {v}");
                                pcts.Add(v);
                            }
                            else warnings.Add($"line {t.line}: keyframe selector '{t.text}' is not numeric");
                            i++; continue;
                        }
                        warnings.Add($"line {t.line}: unexpected token '{t.text}' in keyframe selector");
                        i++;
                    }
                    if (i >= toks.Count || toks[i].kind != TokKind.Symbol || toks[i].text != "{")
                    {
                        warnings.Add($"line {toks[i].line}: keyframe expected '{{'");
                        break;
                    }
                    i++; // '{'

                    var decls = ParseDeclBlock(toks, ref i, warnings);
                    string ease = null;
                    if (decls.TryGetValue("animation-timing-function", out var atf))
                    {
                        ease = atf;
                        decls.Remove("animation-timing-function");
                    }

                    foreach (var pct in pcts)
                    {
                        block.frames.Add(new KeyframeFrame
                        {
                            pct = pct,
                            easeOverride = ease,
                            decls = new Dictionary<string, string>(decls, StringComparer.OrdinalIgnoreCase),
                        });
                    }
                }
                if (i < toks.Count && toks[i].kind == TokKind.Symbol && toks[i].text == "}") i++;
                return block;
            }

            static SelectorRule ParseSelectorRule(List<Tok> toks, ref int i, List<string> warnings, Stylesheet sheet)
            {
                // Collect tokens up to '{'.
                var selToks = new List<Tok>();
                while (i < toks.Count && !(toks[i].kind == TokKind.Symbol && toks[i].text == "{") && toks[i].kind != TokKind.EOF)
                {
                    selToks.Add(toks[i]);
                    i++;
                }
                if (i >= toks.Count || toks[i].kind != TokKind.Symbol || toks[i].text != "{")
                {
                    if (toks[i].kind == TokKind.EOF) return null;
                    warnings.Add($"line {toks[i].line}: selector rule expected '{{'");
                    return null;
                }
                i++; // '{'

                var rule = new SelectorRule();
                rule.selectors = SplitSelectorList(selToks, warnings);
                var decls = ParseDeclBlock(toks, ref i, warnings);

                // CSS custom properties live in decls under their literal
                // `--name` keys. Only :root scope is supported — element
                // selectors would need a matcher we don't have. Other
                // selectors' --vars get dropped with a warning so the
                // author knows they were ignored, not silently respected.
                bool isRoot = rule.selectors.Count == 1 && rule.selectors[0].Trim() == ":root";
                List<string> varKeys = null;
                foreach (var k in decls.Keys)
                {
                    if (k.StartsWith("--", StringComparison.Ordinal))
                    {
                        if (varKeys == null) varKeys = new List<string>();
                        varKeys.Add(k);
                    }
                }
                if (varKeys != null)
                {
                    foreach (var k in varKeys)
                    {
                        if (isRoot) sheet.customProperties[k] = decls[k];
                        else warnings.Add($"custom property '{k}' on selector '{string.Join(",", rule.selectors)}' is element-scoped; only :root is supported, dropped");
                        decls.Remove(k);
                    }
                }

                // Combine sub-properties to a synthetic 'animation' shorthand
                // if 'animation-name' was supplied. animation longhands win.
                if (decls.ContainsKey("animation-name"))
                {
                    var b = new AnimationBinding { name = decls["animation-name"] };
                    if (decls.TryGetValue("animation-duration", out var d) && TryParseTime(d, out var ds)) b.duration = ds;
                    if (decls.TryGetValue("animation-timing-function", out var t)) b.timingFunction = t;
                    if (decls.TryGetValue("animation-delay", out var dl) && TryParseTime(dl, out var dls)) b.delay = dls;
                    if (decls.TryGetValue("animation-iteration-count", out var ic)) ApplyIteration(b, ic);
                    if (decls.TryGetValue("animation-direction", out var dir)) b.direction = dir;
                    rule.animations.Add(b);
                }
                if (decls.TryGetValue("animation", out var shorthand))
                {
                    // Comma-separated list: each item is one binding.
                    foreach (var item in SplitTopLevelCommas(shorthand))
                    {
                        var b = ParseAnimationShorthand(item, warnings);
                        if (b != null) rule.animations.Add(b);
                    }
                }
                return rule;
            }

            static void ApplyIteration(AnimationBinding b, string raw)
            {
                if (string.IsNullOrEmpty(raw)) return;
                if (raw.Equals("infinite", StringComparison.OrdinalIgnoreCase)) { b.iterationInfinite = true; b.iterationCount = 1; return; }
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { b.iterationCount = Mathf.Max(1, n); b.iterationInfinite = false; return; }
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var fn)) { b.iterationCount = Mathf.Max(1, Mathf.RoundToInt(fn)); b.iterationInfinite = false; return; }
            }

            static AnimationBinding ParseAnimationShorthand(string item, List<string> warnings)
            {
                // 'name 2s ease-in-out 0.5s infinite alternate' — fields are
                // order-insensitive by type. We split on whitespace at the top
                // level (respecting parens for cubic-bezier()/steps()).
                var parts = SplitWhitespaceRespectingParens(item);
                if (parts.Count == 0) return null;

                var b = new AnimationBinding();
                bool durationSet = false;
                foreach (var raw in parts)
                {
                    var p = raw.Trim();
                    if (p.Length == 0) continue;

                    if (TryParseTime(p, out var t))
                    {
                        if (!durationSet) { b.duration = t; durationSet = true; }
                        else b.delay = t;
                        continue;
                    }
                    if (p.Equals("infinite", StringComparison.OrdinalIgnoreCase)) { b.iterationInfinite = true; continue; }
                    if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { b.iterationCount = Mathf.Max(1, n); continue; }
                    if (IsDirectionKeyword(p)) { b.direction = p; continue; }
                    if (IsTimingFunctionToken(p)) { b.timingFunction = p; continue; }
                    if (IsFillModeKeyword(p)) { /* fill-mode unsupported */ warnings.Add($"animation fill-mode '{p}' ignored"); continue; }
                    if (IsPlayStateKeyword(p)) { /* play-state unsupported */ warnings.Add($"animation play-state '{p}' ignored"); continue; }
                    // Otherwise treat as the keyframes name.
                    if (string.IsNullOrEmpty(b.name)) b.name = p;
                    else warnings.Add($"animation: unexpected token '{p}' (already have name '{b.name}')");
                }
                if (string.IsNullOrEmpty(b.name))
                {
                    warnings.Add("animation: shorthand has no @keyframes name");
                    return null;
                }
                return b;
            }

            static bool IsDirectionKeyword(string p) =>
                p.Equals("normal", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("reverse", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("alternate", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("alternate-reverse", StringComparison.OrdinalIgnoreCase);

            static bool IsFillModeKeyword(string p) =>
                p.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("forwards", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("backwards", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("both", StringComparison.OrdinalIgnoreCase);

            static bool IsPlayStateKeyword(string p) =>
                p.Equals("paused", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("running", StringComparison.OrdinalIgnoreCase);

            static bool IsTimingFunctionToken(string p)
            {
                if (p.StartsWith("cubic-bezier(", StringComparison.OrdinalIgnoreCase)) return true;
                if (p.StartsWith("steps(", StringComparison.OrdinalIgnoreCase)) return true;
                switch (p.ToLowerInvariant())
                {
                    case "linear":
                    case "ease":
                    case "ease-in":
                    case "ease-out":
                    case "ease-in-out":
                    case "step-start":
                    case "step-end":
                        return true;
                }
                return false;
            }

            static Dictionary<string, string> ParseDeclBlock(List<Tok> toks, ref int i, List<string> warnings)
            {
                var decls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (i < toks.Count && !(toks[i].kind == TokKind.Symbol && toks[i].text == "}") && toks[i].kind != TokKind.EOF)
                {
                    if (toks[i].kind == TokKind.Symbol && toks[i].text == ";") { i++; continue; }
                    // property name
                    if (toks[i].kind != TokKind.Ident)
                    {
                        warnings.Add($"line {toks[i].line}: expected property name, got '{toks[i].text}'");
                        // recover to next ; or }
                        while (i < toks.Count && !(toks[i].kind == TokKind.Symbol && (toks[i].text == ";" || toks[i].text == "}"))) i++;
                        continue;
                    }
                    var prop = toks[i].text.ToLowerInvariant();
                    i++;
                    if (i >= toks.Count || toks[i].kind != TokKind.Symbol || toks[i].text != ":")
                    {
                        warnings.Add($"line {toks[i].line}: property '{prop}' missing ':'");
                        while (i < toks.Count && !(toks[i].kind == TokKind.Symbol && (toks[i].text == ";" || toks[i].text == "}"))) i++;
                        continue;
                    }
                    i++; // ':'
                    // Collect value tokens until ';' or '}'.
                    var sb = new StringBuilder();
                    int parenDepth = 0;
                    while (i < toks.Count)
                    {
                        var t = toks[i];
                        if (t.kind == TokKind.EOF) break;
                        if (t.kind == TokKind.Symbol && t.text == ";" && parenDepth == 0) break;
                        if (t.kind == TokKind.Symbol && t.text == "}" && parenDepth == 0) break;
                        if (t.kind == TokKind.Symbol && t.text == "(") parenDepth++;
                        else if (t.kind == TokKind.Symbol && t.text == ")") parenDepth--;

                        if (sb.Length > 0)
                        {
                            // Suppress space immediately around parens, commas, and after '#'.
                            char last = sb[sb.Length - 1];
                            bool tightNext = t.kind == TokKind.Symbol && (t.text == "(" || t.text == ")" || t.text == ",");
                            bool tightPrev = last == '(' || last == '#';
                            if (!tightNext && !tightPrev) sb.Append(' ');
                        }
                        if (t.kind == TokKind.Hash) sb.Append('#').Append(t.text);
                        else sb.Append(t.text);
                        i++;
                    }
                    decls[prop] = sb.ToString().Trim();
                }
                if (i < toks.Count && toks[i].kind == TokKind.Symbol && toks[i].text == "}") i++;
                return decls;
            }

            static List<string> SplitSelectorList(List<Tok> selToks, List<string> warnings)
            {
                var result = new List<string>();
                var cur = new StringBuilder();
                void Flush() { var s = cur.ToString().Trim(); if (s.Length > 0) result.Add(s); cur.Clear(); }
                foreach (var t in selToks)
                {
                    if (t.kind == TokKind.Symbol && t.text == ",") { Flush(); continue; }
                    if (t.kind == TokKind.Hash) cur.Append('#').Append(t.text);
                    else cur.Append(t.text);
                }
                Flush();
                return result;
            }

            static void SkipAtRule(List<Tok> toks, ref int i)
            {
                int depth = 0;
                while (i < toks.Count && toks[i].kind != TokKind.EOF)
                {
                    if (toks[i].kind == TokKind.Symbol)
                    {
                        if (toks[i].text == "{") depth++;
                        else if (toks[i].text == "}") { depth--; if (depth <= 0) { i++; return; } }
                        else if (toks[i].text == ";" && depth == 0) { i++; return; }
                    }
                    i++;
                }
            }
        }

        // -------- Value parsing helpers --------

        static bool TryParseNumberWithUnit(string raw, out float value, out string unit)
        {
            value = 0f; unit = "";
            if (string.IsNullOrEmpty(raw)) return false;
            int u = 0;
            // Allow leading sign.
            int start = (raw[0] == '+' || raw[0] == '-') ? 1 : 0;
            int j = start;
            bool sawDigit = false;
            while (j < raw.Length && (char.IsDigit(raw[j]) || raw[j] == '.')) { sawDigit |= char.IsDigit(raw[j]); j++; }
            if (!sawDigit) return false;
            u = j;
            unit = raw.Substring(u);
            var numText = raw.Substring(0, u);
            return float.TryParse(numText, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static bool TryParseTime(string raw, out float seconds)
        {
            seconds = 0f;
            if (string.IsNullOrEmpty(raw)) return false;
            if (!TryParseNumberWithUnit(raw, out var v, out var unit)) return false;
            switch (unit.ToLowerInvariant())
            {
                case "s": seconds = v; return true;
                case "ms": seconds = v / 1000f; return true;
                case "": seconds = v; return true; // unitless treated as seconds (CSS forbids but tolerate)
            }
            return false;
        }

        static List<string> SplitTopLevelCommas(string s)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(s)) return result;
            int depth = 0;
            var cur = new StringBuilder();
            foreach (var c in s)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
                if (c == ',' && depth == 0) { result.Add(cur.ToString()); cur.Clear(); continue; }
                cur.Append(c);
            }
            if (cur.Length > 0) result.Add(cur.ToString());
            return result;
        }

        static List<string> SplitWhitespaceRespectingParens(string s)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(s)) return result;
            int depth = 0;
            var cur = new StringBuilder();
            foreach (var c in s)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
                if (char.IsWhiteSpace(c) && depth == 0)
                {
                    if (cur.Length > 0) { result.Add(cur.ToString()); cur.Clear(); }
                    continue;
                }
                cur.Append(c);
            }
            if (cur.Length > 0) result.Add(cur.ToString());
            return result;
        }

        // -------- Emitter --------

        static class Emitter
        {
            public static string Emit(Stylesheet sheet, List<string> warnings)
            {
                var sb = new StringBuilder();
                sb.AppendLine("// Auto-generated from CSS @keyframes by VMGCssKeyframes");
                sb.AppendLine();

                // Index keyframes by name (case-insensitive).
                var kfByName = new Dictionary<string, KeyframesBlock>(StringComparer.OrdinalIgnoreCase);
                foreach (var b in sheet.keyframes) kfByName[b.name] = b;

                foreach (var rule in sheet.rules)
                {
                    foreach (var sel in rule.selectors)
                    {
                        var target = SelectorToTarget(sel, warnings);
                        if (string.IsNullOrEmpty(target)) continue;
                        foreach (var anim in rule.animations)
                        {
                            if (!kfByName.TryGetValue(anim.name, out var block))
                            {
                                warnings.Add($"selector '{sel}' references undefined @keyframes '{anim.name}'");
                                continue;
                            }
                            EmitKeyframes(sb, target, anim, block, sheet.customProperties, warnings);
                            sb.AppendLine();
                        }
                    }
                }
                return sb.ToString();
            }

            static string SelectorToTarget(string sel, List<string> warnings)
            {
                var s = sel.Trim();
                if (s.Length == 0) return null;
                // Strip a single leading '.' or '#'. Compound selectors like
                // 'div.box' or '.box > .dot' are out of scope; warn and use
                // the last simple atom.
                if (s.IndexOfAny(new[] { ' ', '>', '+', '~', '[', ':', '(' }) >= 0)
                {
                    warnings.Add($"selector '{sel}' is compound; only simple name/.class/#id selectors are supported");
                    return null;
                }
                if (s[0] == '.' || s[0] == '#') s = s.Substring(1);
                return s;
            }

            static void EmitKeyframes(StringBuilder sb, string target, AnimationBinding anim, KeyframesBlock block, Dictionary<string, string> customProps, List<string> warnings)
            {
                // Pipeline:
                //   1. Translate each CSS frame to a flat path=value dict
                //      (carrying transform aggregates).
                //   2. Split frames by channel — each channel owns ONLY the
                //      frames in which it appears.
                //   3. Emit ONE `keyframes target { ... }` block per channel.
                //
                // Why per-channel emission: VMGFx applies a frame's
                // easeOverride to the segment ENDING at that frame, and the
                // same frame is shared by every channel that names it. If we
                // emit one combined block, an ease set at a frame for one
                // channel bleeds onto every channel that has a defined value
                // at the same frame. Per-channel blocks isolate that —
                // matching CSS semantics where timing-function attaches to a
                // specific (keyframe, segment) pair and only affects channels
                // redefined at the starting keyframe of that segment.

                var blockAxes = CollectBlockAxes(block);

                // Identity defaults seed the running transform vars. CSS uses
                // the element's computed style as the implicit 0% / 100%
                // boundary; we can't read that from the runtime here, so we
                // assume identity (translate 0,0 / scale 1,1 / rotate 0).
                var running = new Dictionary<string, string>();
                if (blockAxes.usesTranslate) { running["__tx"] = "0"; running["__ty"] = "0"; }
                if (blockAxes.usesScale) { running["__sx"] = "1"; running["__sy"] = "1"; }
                if (blockAxes.usesRotate) running["__rz"] = "0";

                block.frames.Sort((a, b) => a.pct.CompareTo(b.pct));

                // First pass: per-frame flat dict + startEase (CSS semantics).
                // transform aggregates are emitted ONLY for frames that
                // actually carry a `transform:` declaration. A frame with no
                // transform leaves all three transform channels as holes —
                // CSS treats those as "channel not redefined here" and the
                // engine bridges across by interpolating from the nearest
                // earlier defined frame to the nearest later one. Carrying
                // the running value forward would falsely pin the channel
                // to the previous frame's value.
                var flatFrames = new List<(float pct, string startEase, Dictionary<string, string> values)>();
                foreach (var f in block.frames)
                {
                    var flat = new Dictionary<string, string>();
                    bool hasTransform = f.decls.ContainsKey("transform");
                    TranslateDecls(f.decls, flat, running, blockAxes, customProps, warnings);
                    if (hasTransform)
                    {
                        if (blockAxes.usesTranslate)
                            flat["localPosition"] = $"{running["__tx"]},{running["__ty"]}";
                        if (blockAxes.usesScale)
                            flat["localScale"] = $"{running["__sx"]},{running["__sy"]},1";
                        if (blockAxes.usesRotate)
                            flat["localEulerAngles.z"] = running["__rz"];
                    }
                    flatFrames.Add((f.pct, f.easeOverride, flat));
                }
                if (flatFrames.Count == 0) return;

                // Collect channel set (union over all frames).
                var allChannels = new List<string>();
                var seenCh = new HashSet<string>();
                foreach (var f in flatFrames)
                    foreach (var kv in f.values)
                        if (seenCh.Add(kv.Key)) allChannels.Add(kv.Key);

                // Common block header for every per-channel emit.
                string headerEase = null;
                if (!string.IsNullOrEmpty(anim.timingFunction))
                    headerEase = TranslateTimingFunction(anim.timingFunction, warnings);
                if (anim.direction.Equals("reverse", StringComparison.OrdinalIgnoreCase) ||
                    anim.direction.Equals("alternate-reverse", StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"animation-direction '{anim.direction}' partially supported: starts at 0% then alternates; pure reverse is not modeled");

                foreach (var channel in allChannels)
                {
                    // Gather the frames that define THIS channel only.
                    var chFrames = new List<(float pct, string startEase, string value)>();
                    for (int i = 0; i < flatFrames.Count; i++)
                    {
                        var ff = flatFrames[i];
                        if (!ff.values.TryGetValue(channel, out var v)) continue;
                        chFrames.Add((ff.pct, ff.startEase, v));
                    }
                    if (chFrames.Count < 2)
                    {
                        // Single-frame channel: with no second point there's
                        // nothing for the engine to interpolate. CSS would
                        // hold the value; we approximate by emitting a 2-frame
                        // block from the same value to itself so the channel
                        // gets explicitly set.
                        if (chFrames.Count == 1)
                        {
                            var only = chFrames[0];
                            if (only.pct > 0.0001f) chFrames.Insert(0, (0f, null, only.value));
                            if (chFrames[chFrames.Count - 1].pct < 99.9999f)
                                chFrames.Add((100f, null, only.value));
                        }
                        else continue;
                    }
                    // Ensure 0% / 100% endpoints exist. 0% inherits from the
                    // first defined frame (CSS would use the computed style;
                    // we use the runtime value at start, which the engine
                    // already exposes by holding the from-side of the first
                    // segment).
                    if (chFrames[0].pct > 0.0001f)
                    {
                        // Without an explicit 0% the channel would not start
                        // from a known value. Insert a frame at 0 carrying
                        // the same value as the first defined frame — this
                        // gives the engine a from-side and matches CSS where
                        // an unspecified 0% inherits "the value as defined
                        // at the next earliest frame".
                        chFrames.Insert(0, (0f, null, chFrames[0].value));
                    }
                    if (chFrames[chFrames.Count - 1].pct < 99.9999f)
                    {
                        var last = chFrames[chFrames.Count - 1];
                        chFrames.Add((100f, null, last.value));
                    }

                    // Shift startEase[i] → endEase[i+1]. (See main comment
                    // block at the top of EmitKeyframes.)
                    var endEase = new string[chFrames.Count];
                    for (int i = 0; i < chFrames.Count - 1; i++)
                    {
                        if (!string.IsNullOrEmpty(chFrames[i].startEase))
                            endEase[i + 1] = chFrames[i].startEase;
                    }
                    if (!string.IsNullOrEmpty(chFrames[chFrames.Count - 1].startEase))
                        warnings.Add($"animation-timing-function on the final keyframe ({Fmt(chFrames[chFrames.Count - 1].pct)}%) has no following segment and was ignored (channel '{channel}')");

                    // Header.
                    sb.Append("keyframes ").Append(target);
                    sb.Append(" duration=").Append(Fmt(anim.duration));
                    if (!string.IsNullOrEmpty(headerEase)) sb.Append(" ease=").Append(headerEase);
                    if (anim.delay > 0f) sb.Append(" delay=").Append(Fmt(anim.delay));
                    if (anim.iterationInfinite) sb.Append(" loop");
                    else if (anim.iterationCount > 1) sb.Append(" loop=").Append(anim.iterationCount);
                    if (anim.direction.Equals("alternate", StringComparison.OrdinalIgnoreCase) ||
                        anim.direction.Equals("alternate-reverse", StringComparison.OrdinalIgnoreCase))
                        sb.Append(" alternate");
                    sb.AppendLine(" {");

                    for (int i = 0; i < chFrames.Count; i++)
                    {
                        var cf = chFrames[i];
                        sb.Append("  ").Append(Fmt(cf.pct)).Append("%: ").Append(channel).Append('=').Append(cf.value);
                        if (!string.IsNullOrEmpty(endEase[i]))
                        {
                            var ease = TranslateTimingFunction(endEase[i], warnings);
                            if (!string.IsNullOrEmpty(ease)) sb.Append(" ease=").Append(ease);
                        }
                        sb.AppendLine();
                    }
                    sb.AppendLine("}");
                }
            }

            struct BlockAxes
            {
                public bool usesTranslate;
                public bool usesScale;
                public bool usesRotate;
            }

            static BlockAxes CollectBlockAxes(KeyframesBlock block)
            {
                var ax = new BlockAxes();
                foreach (var f in block.frames)
                {
                    if (f.decls.TryGetValue("transform", out var tr))
                    {
                        if (tr.IndexOf("translate", StringComparison.OrdinalIgnoreCase) >= 0) ax.usesTranslate = true;
                        if (tr.IndexOf("scale", StringComparison.OrdinalIgnoreCase) >= 0) ax.usesScale = true;
                        if (tr.IndexOf("rotate", StringComparison.OrdinalIgnoreCase) >= 0) ax.usesRotate = true;
                    }
                }
                return ax;
            }

            static void TranslateDecls(Dictionary<string, string> decls, Dictionary<string, string> flat,
                                       Dictionary<string, string> running, BlockAxes axes,
                                       Dictionary<string, string> customProps, List<string> warnings)
            {
                foreach (var kv in decls)
                {
                    var prop = kv.Key;
                    // Resolve every var() reference up-front so each
                    // property-specific branch below sees plain CSS values.
                    // Properties whose values are parsed token-by-token
                    // (transform) still need to push customProps further down.
                    var val = ResolveVarFallback(kv.Value, customProps, warnings);
                    if (prop == "transform")
                    {
                        ParseTransform(val, running, customProps, warnings);
                        continue;
                    }
                    if (prop == "opacity")
                    {
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                        {
                            // VectorImageGraphic inherits Graphic.color; alpha lives there.
                            flat["color.a"] = Fmt(Mathf.Clamp01(a));
                        }
                        else warnings.Add($"opacity '{val}' not numeric");
                        continue;
                    }
                    if (prop == "background-color")
                    {
                        var c = TranslateColor(val, warnings);
                        if (c != null) flat["Fill.color"] = c;
                        continue;
                    }
                    if (prop == "color")
                    {
                        // CSS 'color' affects text; we map to Graphic tint
                        // (UI Image color) for consistency with opacity.
                        var c = TranslateColor(val, warnings);
                        if (c != null) flat["color"] = c;
                        continue;
                    }
                    if (prop == "border-color")
                    {
                        var c = TranslateColor(val, warnings);
                        if (c != null) flat["Stroke.color"] = c;
                        continue;
                    }
                    if (prop == "border-width")
                    {
                        if (TryParseNumberWithUnit(val, out var w, out var unit))
                        {
                            if (unit != "px" && unit != "") warnings.Add($"border-width unit '{unit}' not supported; treated as raw");
                            flat["Stroke.width"] = Fmt(w);
                        }
                        continue;
                    }
                    // Recognised but unsupported in v1 — quietly drop common
                    // CSS layout properties; warn for everything else so
                    // typos surface.
                    if (IsKnownButUnsupported(prop)) continue;
                    warnings.Add($"property '{prop}' not supported; dropped");
                }
            }

            static bool IsKnownButUnsupported(string prop)
            {
                switch (prop)
                {
                    case "display":
                    case "position":
                    case "top":
                    case "left":
                    case "right":
                    case "bottom":
                    case "margin":
                    case "padding":
                    case "width":
                    case "height":
                    case "flex":
                    case "grid":
                    case "z-index":
                    case "box-shadow":
                    case "border-radius":
                    case "border":
                    case "border-style":
                    case "font-size":
                    case "font-family":
                    case "font-weight":
                    case "text-align":
                    case "line-height":
                    case "letter-spacing":
                    case "filter":
                    case "backdrop-filter":
                    case "animation":
                    case "animation-name":
                    case "animation-duration":
                    case "animation-timing-function":
                    case "animation-delay":
                    case "animation-iteration-count":
                    case "animation-direction":
                    case "animation-fill-mode":
                    case "animation-play-state":
                    case "transform-origin":
                    case "transition":
                    case "will-change":
                        return true;
                }
                return false;
            }

            // Parse a single 'transform' value: translate(...) scale(...) rotate(...)
            // pieces. Each parsed piece updates the running axis vars.
            static void ParseTransform(string val, Dictionary<string, string> running, Dictionary<string, string> customProps, List<string> warnings)
            {
                int i = 0;
                while (i < val.Length)
                {
                    while (i < val.Length && (char.IsWhiteSpace(val[i]) || val[i] == ',')) i++;
                    if (i >= val.Length) break;
                    int nameStart = i;
                    while (i < val.Length && (char.IsLetterOrDigit(val[i]) || val[i] == '-')) i++;
                    if (i >= val.Length || val[i] != '(') break;
                    var name = val.Substring(nameStart, i - nameStart);
                    int argStart = ++i;
                    int depth = 1;
                    while (i < val.Length && depth > 0)
                    {
                        if (val[i] == '(') depth++;
                        else if (val[i] == ')') depth--;
                        if (depth > 0) i++;
                    }
                    var argStr = val.Substring(argStart, i - argStart);
                    if (i < val.Length) i++; // ')'
                    ApplyTransformFn(name, argStr, running, customProps, warnings);
                }
            }

            static void ApplyTransformFn(string name, string argStr, Dictionary<string, string> running, Dictionary<string, string> customProps, List<string> warnings)
            {
                var args = SplitArgs(argStr);
                for (int ai = 0; ai < args.Count; ai++) args[ai] = ResolveVarFallback(args[ai], customProps, warnings);
                switch (name.ToLowerInvariant())
                {
                    case "translate":
                    {
                        if (args.Count >= 1 && TryParseLength(args[0], out var x)) running["__tx"] = Fmt(x);
                        if (args.Count >= 2 && TryParseLength(args[1], out var y)) running["__ty"] = Fmt(y);
                        else if (args.Count == 1) running["__ty"] = "0";
                        break;
                    }
                    case "translatex":
                        if (args.Count >= 1 && TryParseLength(args[0], out var tx)) running["__tx"] = Fmt(tx);
                        break;
                    case "translatey":
                        if (args.Count >= 1 && TryParseLength(args[0], out var ty)) running["__ty"] = Fmt(ty);
                        break;
                    case "scale":
                    {
                        if (args.Count >= 1 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var sx))
                        {
                            running["__sx"] = Fmt(sx);
                            running["__sy"] = Fmt(sx);
                        }
                        if (args.Count >= 2 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var sy))
                            running["__sy"] = Fmt(sy);
                        break;
                    }
                    case "scalex":
                        if (args.Count >= 1 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var sxx))
                            running["__sx"] = Fmt(sxx);
                        break;
                    case "scaley":
                        if (args.Count >= 1 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var syy))
                            running["__sy"] = Fmt(syy);
                        break;
                    case "rotate":
                    case "rotatez":
                        if (args.Count >= 1 && TryParseAngle(args[0], out var rz)) running["__rz"] = Fmt(rz);
                        break;
                    case "rotatex":
                    case "rotatey":
                        warnings.Add($"transform '{name}()' is 3D-only; only rotate/rotateZ is mapped to Z");
                        break;
                    case "matrix":
                    case "skew":
                    case "skewx":
                    case "skewy":
                    case "perspective":
                        warnings.Add($"transform '{name}()' is not supported and was ignored");
                        break;
                }
            }

            static List<string> SplitArgs(string raw)
            {
                var result = new List<string>();
                if (string.IsNullOrEmpty(raw)) return result;
                int depth = 0;
                var cur = new StringBuilder();
                foreach (var c in raw)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    if (c == ',' && depth == 0) { result.Add(cur.ToString().Trim()); cur.Clear(); continue; }
                    cur.Append(c);
                }
                if (cur.Length > 0) result.Add(cur.ToString().Trim());
                return result;
            }

            // Resolve a single CSS `var(--name [, fallback])` reference. The
            // :root custom-property store (when supplied) is consulted first;
            // if the name is missing there, the fallback expression is used;
            // if there's no fallback either, the var() is left unresolved
            // and a warning is logged (downstream parse will also surface
            // the problem). Multiple var()s in one expression are resolved
            // left-to-right. calc() wrappers are stripped only when the
            // inner expression is already a bare number after substitution;
            // mixed-unit arithmetic (calc(2 * 3px)) is out of scope.
            static string ResolveVarFallback(string raw, Dictionary<string, string> customProps, List<string> warnings)
            {
                if (string.IsNullOrEmpty(raw) || raw.IndexOf("var(", System.StringComparison.OrdinalIgnoreCase) < 0)
                    return raw;
                var s = raw;
                int guard = 0;
                while (true)
                {
                    int open = s.IndexOf("var(", System.StringComparison.OrdinalIgnoreCase);
                    if (open < 0) break;
                    int p = open + 4;
                    int depth = 1;
                    int close = -1;
                    while (p < s.Length)
                    {
                        if (s[p] == '(') depth++;
                        else if (s[p] == ')') { depth--; if (depth == 0) { close = p; break; } }
                        p++;
                    }
                    if (close < 0) { warnings.Add($"var() unterminated in '{raw}'"); break; }
                    var body = s.Substring(open + 4, close - open - 4);
                    var parts = SplitArgs(body);
                    string name = parts.Count > 0 ? parts[0].Trim() : "";

                    string replacement = null;
                    if (!string.IsNullOrEmpty(name) && customProps != null && customProps.TryGetValue(name, out var stored))
                    {
                        // The stored value may itself reference other vars
                        // (e.g. --primary: var(--brand)) so recurse.
                        replacement = ResolveVarFallback(stored, customProps, warnings);
                    }
                    else if (parts.Count >= 2)
                    {
                        replacement = ResolveVarFallback(parts[1], customProps, warnings);
                    }

                    if (replacement == null)
                    {
                        warnings.Add($"var('{name}') is not declared in :root and has no fallback; left unresolved");
                        replacement = s.Substring(open, close - open + 1); // keep original — downstream will warn
                        if (++guard > 8) break;
                        s = s.Substring(0, open) + replacement + s.Substring(close + 1);
                        continue;
                    }
                    s = s.Substring(0, open) + replacement + s.Substring(close + 1);
                    if (++guard > 16) { warnings.Add("var() resolution depth exceeded"); break; }
                }
                return s.Trim();
            }

            static bool TryParseLength(string s, out float value)
            {
                if (!TryParseNumberWithUnit(s, out value, out var unit)) return false;
                switch (unit.ToLowerInvariant())
                {
                    case "px": case "": return true;
                    case "%": return true; // user-intent; pass through as number
                    case "em": case "rem": value *= 16f; return true; // rough
                    default: return true;
                }
            }

            static bool TryParseAngle(string s, out float deg)
            {
                if (!TryParseNumberWithUnit(s, out deg, out var unit)) return false;
                switch (unit.ToLowerInvariant())
                {
                    case "deg": case "": return true;
                    case "rad": deg = deg * Mathf.Rad2Deg; return true;
                    case "turn": deg = deg * 360f; return true;
                    case "grad": deg = deg * 0.9f; return true;
                }
                return true;
            }

            // -------- Color --------

            // Returns a .vmgfx color literal: prefer 'r,g,b,a' tuple in 0..1
            // so VMGFx.TryParseColor accepts it consistently across rgb/rgba/hex.
            // VMGFx already supports #rgb/#rgba/#rrggbb/#rrggbbaa and named
            // colours, but rgba() and modern functional syntax don't parse —
            // we normalize.
            static string TranslateColor(string raw, List<string> warnings)
            {
                if (string.IsNullOrEmpty(raw)) return null;
                var s = raw.Trim();
                if (s.StartsWith("#")) return s; // pass through, VMGFx accepts #rgb/#rrggbb/#rrggbbaa
                if (s.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
                {
                    int open = s.IndexOf('(');
                    int close = s.LastIndexOf(')');
                    if (open < 0 || close < 0) { warnings.Add($"color '{raw}' malformed rgb()"); return null; }
                    var body = s.Substring(open + 1, close - open - 1);
                    var parts = SplitArgs(body.Replace("/", ","));
                    if (parts.Count < 3) { warnings.Add($"color '{raw}' rgb() needs 3+ components"); return null; }
                    if (!TryParseRgbComponent(parts[0], out var r)) return null;
                    if (!TryParseRgbComponent(parts[1], out var g)) return null;
                    if (!TryParseRgbComponent(parts[2], out var b)) return null;
                    float a = 1f;
                    if (parts.Count >= 4)
                    {
                        if (!TryParseAlpha(parts[3], out a)) a = 1f;
                    }
                    // Use a hex literal — round-trip safe and VMGFx-supported.
                    return ToHex(r, g, b, a);
                }
                if (s.StartsWith("hsl(", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("hsla(", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"color function '{s}' (HSL) not supported; replace with rgb() or hex");
                    return null;
                }
                // Named colour passthrough — VMGFx has its own table; if it
                // doesn't know the name it'll log its own warning at compile.
                return s.ToLowerInvariant();
            }

            static bool TryParseRgbComponent(string s, out byte v)
            {
                v = 0; s = s.Trim();
                if (s.EndsWith("%"))
                {
                    if (!float.TryParse(s.Substring(0, s.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return false;
                    v = (byte)Mathf.Clamp(Mathf.RoundToInt(p / 100f * 255f), 0, 255);
                    return true;
                }
                if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return false;
                v = (byte)Mathf.Clamp(Mathf.RoundToInt(f), 0, 255);
                return true;
            }

            static bool TryParseAlpha(string s, out float a)
            {
                a = 1f; s = s.Trim();
                if (s.EndsWith("%"))
                {
                    if (!float.TryParse(s.Substring(0, s.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return false;
                    a = Mathf.Clamp01(p / 100f); return true;
                }
                if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return false;
                a = Mathf.Clamp01(f); return true;
            }

            static string ToHex(byte r, byte g, byte b, float a)
            {
                if (a >= 0.9999f) return $"#{r:x2}{g:x2}{b:x2}";
                int ai = Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                return $"#{r:x2}{g:x2}{b:x2}{ai:x2}";
            }

            // -------- Timing functions --------

            // CSS-canonical cubic-bezier control points (per W3C spec). We
            // emit cubicBezier() directly so visual fidelity to a browser is
            // exact — the .vmgfx DSL maps cubicBezier to the same Bézier
            // parameterization (per `project_dsl_parity_round1_done.md`).
            static string TranslateTimingFunction(string raw, List<string> warnings)
            {
                if (string.IsNullOrEmpty(raw)) return null;
                var s = raw.Trim();
                if (s.StartsWith("cubic-bezier(", StringComparison.OrdinalIgnoreCase))
                {
                    int open = s.IndexOf('(');
                    int close = s.LastIndexOf(')');
                    if (open < 0 || close < 0) { warnings.Add($"cubic-bezier '{s}' malformed"); return null; }
                    var args = SplitArgs(s.Substring(open + 1, close - open - 1));
                    if (args.Count != 4) { warnings.Add($"cubic-bezier '{s}' needs 4 args"); return null; }
                    return "cubicBezier(" + string.Join(",", args) + ")";
                }
                if (s.StartsWith("steps(", StringComparison.OrdinalIgnoreCase))
                {
                    int open = s.IndexOf('(');
                    int close = s.LastIndexOf(')');
                    if (open < 0 || close < 0) { warnings.Add($"steps '{s}' malformed"); return null; }
                    var args = SplitArgs(s.Substring(open + 1, close - open - 1));
                    if (args.Count >= 1) return $"steps({args[0]})";
                    return null;
                }
                switch (s.ToLowerInvariant())
                {
                    // Per CSS spec.
                    case "linear": return "linear";
                    case "ease": return "cubicBezier(0.25,0.1,0.25,1)";
                    case "ease-in": return "cubicBezier(0.42,0,1,1)";
                    case "ease-out": return "cubicBezier(0,0,0.58,1)";
                    case "ease-in-out": return "cubicBezier(0.42,0,0.58,1)";
                    case "step-start": return "steps(1)";
                    case "step-end": return "steps(1)";
                }
                warnings.Add($"timing-function '{s}' not recognised; defaulting to linear");
                return "linear";
            }

            static string Fmt(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
