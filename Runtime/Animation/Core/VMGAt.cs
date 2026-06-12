namespace VMG.Animation.Core
{
    // Typed timeline position. Mirrors anime.js's position strings
    // ('<', '<<', '+=N', '-=N', '*=F', label, label+=N, abs number, and the
    // combined '<+=N' / '<<+=N' / '<-=N' / '<<-=N' forms) in a form that gets
    // IDE autocomplete and compile checking. The string form is also accepted
    // at the Add(...) call site for anime.js text compatibility; both go
    // through the same parser inside VMGTimeline.
    public struct VMGAt
    {
        internal enum Kind
        {
            // Absolute time (seconds) from timeline start.
            Absolute,
            // After the previous child's end + offset (offset can be 0).
            AfterPrev,
            // With the previous child's start (matches anime.js '<<' when
            // offset == 0, plus arbitrary offset).
            WithPrev,
            // Relative to a label.
            FromLabel,
            // anime.js '*=F': position = previous child duration * factor.
            // The numeric factor lives in `offset` (re-used field).
            MultiplyPrev,
            // Default: timeline end (next free slot). Equivalent to omitting
            // a position in anime.js.
            End,
        }

        internal Kind kind;
        internal float offset;
        internal string label;

        public static VMGAt End() => new VMGAt { kind = Kind.End };
        public static VMGAt Time(float seconds) => new VMGAt { kind = Kind.Absolute, offset = seconds };
        public static VMGAt AfterPrev(float gap = 0f) => new VMGAt { kind = Kind.AfterPrev, offset = gap };
        public static VMGAt WithPrev(float offset = 0f) => new VMGAt { kind = Kind.WithPrev, offset = offset };
        public static VMGAt AtLabel(string label, float offset = 0f) => new VMGAt { kind = Kind.FromLabel, label = label, offset = offset };
        public static VMGAt MultiplyPrev(float factor) => new VMGAt { kind = Kind.MultiplyPrev, offset = factor };

        // Implicit conversion from string lets callers write
        // tl.Add(anim, "+=0.2") naturally without choosing a factory.
        public static implicit operator VMGAt(string s) => Parse(s);
        public static implicit operator VMGAt(float seconds) => Time(seconds);

        // Parse an anime.js-style position string. The runtime evaluation
        // (resolving labels and the previous child) happens later in
        // VMGTimeline; this just classifies the syntax.
        public static VMGAt Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return End();

            // Numeric absolute time.
            if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var abs))
            {
                return Time(abs);
            }

            // Previous-child relative: '<' (prev start + prev duration =
            // prev end) or '<<' (prev start exactly). May be followed by
            // a relative operator: '<+=N', '<-=N', '<<+=N', '<<-=N'.
            if (s.Length >= 1 && s[0] == '<')
            {
                bool withStart = s.Length >= 2 && s[1] == '<';
                int rest = withStart ? 2 : 1;
                float gap = 0f;
                if (rest < s.Length) gap = ParseRelativeAdditive(s, rest);
                return withStart ? WithPrev(gap) : AfterPrev(gap);
            }

            // Bare '*=F' = previous-child duration * F. anime.js's docs treat
            // it as a position (offset relative to start) but in practice it
            // anchors on the previous child's start + factor*prevDuration —
            // identical to '<<' + (factor*prevDuration). We surface it as
            // its own Kind so the resolver knows to multiply prev duration.
            if (s.Length >= 3 && s[0] == '*' && s[1] == '=')
            {
                if (float.TryParse(s.Substring(2), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var factor))
                {
                    return MultiplyPrev(factor);
                }
                return End();
            }

            // Label-relative: 'name', 'name+=N', 'name-=N'. Detect relative
            // operator first; whatever's left is the label (possibly empty,
            // which means "timeline end" for +=/-=, or MultiplyPrev for *=).
            int relIdx = IndexOfRelative(s);
            if (relIdx >= 0)
            {
                string label = s.Substring(0, relIdx);
                char op = s[relIdx];
                if (!float.TryParse(s.Substring(relIdx + 2),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var n))
                {
                    return End();
                }

                if (op == '*')
                {
                    // 'label*=F' isn't anime.js syntax; only the bare '*=F'
                    // is. If we got here, return End() — caller wrote
                    // something undefined.
                    return End();
                }

                float gap = (op == '-') ? -n : n;
                if (string.IsNullOrEmpty(label))
                {
                    // '+=N' / '-=N' with no anchor = relative to timeline end.
                    return new VMGAt { kind = Kind.End, offset = gap };
                }
                return AtLabel(label, gap);
            }

            // Plain label: 'name' = at that label, no offset.
            return AtLabel(s, 0f);
        }

        static int IndexOfRelative(string s)
        {
            for (int i = 0; i < s.Length - 1; i++)
            {
                char c = s[i];
                if ((c == '+' || c == '-' || c == '*' || c == '/') && s[i + 1] == '=') return i;
            }
            return -1;
        }

        // Parse only additive relative ops (+=N, -=N). Used after '<' / '<<'
        // where '*=' isn't meaningful.
        static float ParseRelativeAdditive(string s, int startIdx)
        {
            if (startIdx + 2 > s.Length) return 0f;
            char op = s[startIdx];
            if (s[startIdx + 1] != '=') return 0f;
            if (!float.TryParse(s.Substring(startIdx + 2),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n)) return 0f;
            switch (op)
            {
                case '+': return n;
                case '-': return -n;
                default: return 0f;
            }
        }
    }
}
