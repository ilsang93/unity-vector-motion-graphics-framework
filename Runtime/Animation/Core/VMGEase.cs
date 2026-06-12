using UnityEngine;

namespace VMG.Animation.Core
{
    // Easing spec used by code-driven tweens. Wraps the existing
    // bezier-control-point easing system (VMGEasing) but accepts either a
    // VMGEasingPreset, a custom bezier (p1, p2), or an anime.js-style name
    // string. All three resolve to the same (outTangent, inTangent) pair so
    // evaluation is a single code path.
    public struct VMGEase
    {
        // Bezier path: m_OutTangent = p1, m_InTangent = p2.
        // Spring path: m_OutTangent.x = stiffness, m_OutTangent.y = damping,
        //              m_InTangent.x = mass, m_InTangent.y = initialVelocity.
        Vector2 m_OutTangent;
        Vector2 m_InTangent;
        byte m_Kind; // 0 = bezier (default), 1 = spring

        public static VMGEase Linear => From(VMGEasingPreset.Linear);

        public static VMGEase From(VMGEasingPreset preset)
        {
            VMGEasingPresets.GetTangents(preset, out var o, out var i);
            return new VMGEase { m_OutTangent = o, m_InTangent = i };
        }

        public static VMGEase Bezier(float p1x, float p1y, float p2x, float p2y)
        {
            return new VMGEase
            {
                m_OutTangent = new Vector2(p1x, p1y),
                m_InTangent = new Vector2(p2x, p2y),
            };
        }

        // anime.js-style spring. Parameters match createSpring() defaults:
        // stiffness=100, damping=10, mass=1, velocity=0. The output curve is
        // remapped so that normT=1 lines up with the spring's settle time —
        // a tween using Spring will reach steady state at the end of its
        // user-set duration. Read RecommendedDuration to size the tween
        // duration around the spring's natural settle time instead.
        public static VMGEase Spring(float stiffness = 100f, float damping = 10f, float mass = 1f, float velocity = 0f)
        {
            if (stiffness <= 0f) stiffness = 1f;
            if (mass <= 0f) mass = 1f;
            if (damping < 0f) damping = 0f;
            return new VMGEase
            {
                m_OutTangent = new Vector2(stiffness, damping),
                m_InTangent = new Vector2(mass, velocity),
                m_Kind = 1,
            };
        }

        // Estimated time (seconds) for the spring to settle within 0.5% of
        // the target. Only meaningful for Spring-kind ease; returns 0 for
        // bezier. Useful for `.Duration(spring.RecommendedDuration)`.
        public float RecommendedDuration
        {
            get
            {
                if (m_Kind != 1) return 0f;
                return VMGSpringSolver.SettleTime(m_OutTangent.x, m_OutTangent.y, m_InTangent.x, m_InTangent.y);
            }
        }

        // anime.js-style names ("linear", "outQuad", "inOutSine", ...). Unknown
        // names fall back to linear; this is intentional (LLM-friendly: never
        // crashes the play call). The bare string "spring" also resolves to
        // a default spring.
        public static VMGEase From(string name)
        {
            if (string.IsNullOrEmpty(name)) return Linear;
            if (name == "spring") return Spring();
            return From(ParsePreset(name));
        }

        public float Evaluate(float normT)
        {
            if (m_Kind == 1)
                return VMGSpringSolver.Evaluate(m_OutTangent.x, m_OutTangent.y, m_InTangent.x, m_InTangent.y, normT);
            return VMGEasing.Evaluate(m_OutTangent, m_InTangent, normT);
        }

        static VMGEasingPreset ParsePreset(string s)
        {
            // Tolerate a few common spellings: anime.js uses "outQuad",
            // VMG's enum uses QuadOut. Accept both shapes.
            switch (s)
            {
                case "linear": return VMGEasingPreset.Linear;
                case "ease": return VMGEasingPreset.Ease;
                case "in":
                case "easeIn": return VMGEasingPreset.EaseIn;
                case "out":
                case "easeOut": return VMGEasingPreset.EaseOut;
                case "inOut":
                case "easeInOut": return VMGEasingPreset.EaseInOut;

                case "inQuad":
                case "quadIn":
                case "QuadIn": return VMGEasingPreset.QuadIn;
                case "outQuad":
                case "quadOut":
                case "QuadOut": return VMGEasingPreset.QuadOut;

                case "inCubic":
                case "cubicIn":
                case "CubicIn": return VMGEasingPreset.CubicIn;
                case "outCubic":
                case "cubicOut":
                case "CubicOut": return VMGEasingPreset.CubicOut;

                case "inSine":
                case "sineIn":
                case "SineIn": return VMGEasingPreset.SineIn;
                case "outSine":
                case "sineOut":
                case "SineOut": return VMGEasingPreset.SineOut;

                case "hold":
                case "Hold": return VMGEasingPreset.Hold;
            }
            return VMGEasingPreset.Linear;
        }
    }
}
