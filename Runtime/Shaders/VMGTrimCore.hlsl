// GPU Trim Paths core, free of globals so both the built-in VMG shaders
// (VMGTrim.cginc) and Shader Graph (Custom Function node, File mode) can use it.
//
// Stroke vertices carry their normalized arc-length position in UV1.y:
//   [0, 1]  open path       [2, 3]  closed path (position + 2)
//   < 0     not trimmable (fills, AA rings of fills, ...)
// trim = (start, end, offset, enabled). Semantics match the CPU
// TrimPathModifier: open paths clamp the [start+offset, end+offset] window,
// closed paths wrap it (end < start draws the complement).
//
// Shader Graph: Custom Function node, Type = File, Source = this file,
// Name = VMGTrim. Inputs: Arc (Float) = UV node channel UV1, Split -> G;
// Trim (Vector4) = a Vector4 Blackboard property with Reference "_VMGTrim",
// default (0, 1, 0, 0). Output: Alpha (Float) — multiply into the
// fragment Alpha. Fragment stage only (uses fwidth).
#ifndef VMG_TRIM_CORE_INCLUDED
#define VMG_TRIM_CORE_INCLUDED

// Coverage multiplier in [0,1]; the cut edge fades over one pixel.
float VMGTrimCoverage(float arc, float4 trim)
{
    if (trim.w < 0.5 || arc < -0.5) return 1.0;

    bool closed = arc > 1.5;
    float t = closed ? arc - 2.0 : arc;
    float fw = max(fwidth(t), 1e-6);
    float dist;
    if (closed)
    {
        float len = trim.y - trim.x;
        if (len >= 1.0) return 1.0;
        if (len < 0.0) len += 1.0;
        float lo = frac(trim.x + trim.z);
        float d = frac(t - lo + 1.0); // position inside the window
        dist = min(d, len - d);
    }
    else
    {
        float lo = saturate(trim.x + trim.z);
        float hi = saturate(trim.y + trim.z);
        if (hi - lo <= 1e-6) return 0.0;
        // A window edge sitting on the path's own end is no cut at all —
        // leave the real end (and its cap) alone.
        float dLo = lo <= 0.0 ? 1e6 : t - lo;
        float dHi = hi >= 1.0 ? 1e6 : hi - t;
        dist = min(dLo, dHi);
    }
    return saturate(dist / fw + 0.5);
}

// Shader Graph Custom Function entry points.
void VMGTrim_float(float Arc, float4 Trim, out float Alpha) { Alpha = VMGTrimCoverage(Arc, Trim); }
void VMGTrim_half(half Arc, half4 Trim, out half Alpha) { Alpha = VMGTrimCoverage(Arc, Trim); }

#endif
