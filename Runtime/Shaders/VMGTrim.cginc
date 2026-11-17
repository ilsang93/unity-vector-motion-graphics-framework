// GPU Trim Paths for the built-in VMG/UI/VectorSDF and VMG/World/VectorSDF
// shaders. The logic lives in VMGTrimCore.hlsl (shared with Shader Graph);
// this file only binds it to the per-renderer _VMGTrim window
// (start, end, offset, enabled). The renderer leaves the mesh untouched
// while trim animates and only updates _VMGTrim.
#ifndef VMG_TRIM_INCLUDED
#define VMG_TRIM_INCLUDED

#include "VMGTrimCore.hlsl"

float4 _VMGTrim;

half VMGTrimAlpha(float arc) { return VMGTrimCoverage(arc, _VMGTrim); }

#endif
