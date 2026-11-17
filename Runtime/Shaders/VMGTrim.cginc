// GPU Trim Paths, shared by VMG/UI/VectorSDF and VMG/World/VectorSDF.
//
// Stroke vertices carry their normalized arc-length position in UV1.y:
//   [0, 1]  open path       [2, 3]  closed path (position + 2)
//   < 0     not trimmable (fills, AA rings of fills, ...)
// _VMGTrim = (start, end, offset, enabled). Semantics match the CPU
// TrimPathModifier: open paths clamp the [start+offset, end+offset] window,
// closed paths wrap it (end < start draws the complement). The renderer
// leaves the mesh untouched while trim animates and only updates _VMGTrim.
#ifndef VMG_TRIM_INCLUDED
#define VMG_TRIM_INCLUDED

float4 _VMGTrim;

// Coverage multiplier in [0,1]; the cut edge fades over one pixel.
half VMGTrimAlpha(float arc)
{
    if (_VMGTrim.w < 0.5 || arc < -0.5) return 1.0;

    bool closed = arc > 1.5;
    float t = closed ? arc - 2.0 : arc;
    float fw = max(fwidth(t), 1e-6);
    float dist;
    if (closed)
    {
        float len = _VMGTrim.y - _VMGTrim.x;
        if (len >= 1.0) return 1.0;
        if (len < 0.0) len += 1.0;
        float lo = frac(_VMGTrim.x + _VMGTrim.z);
        float d = frac(t - lo + 1.0); // position inside the window
        dist = min(d, len - d);
    }
    else
    {
        float lo = saturate(_VMGTrim.x + _VMGTrim.z);
        float hi = saturate(_VMGTrim.y + _VMGTrim.z);
        // A window edge sitting on the path's own end is no cut at all —
        // leave the real end (and its cap) alone.
        float dLo = lo <= 0.0 ? 1e6 : t - lo;
        float dHi = hi >= 1.0 ? 1e6 : hi - t;
        dist = min(dLo, dHi);
        if (hi - lo <= 1e-6) return 0.0;
    }
    return saturate(dist / fw + 0.5);
}

#endif
