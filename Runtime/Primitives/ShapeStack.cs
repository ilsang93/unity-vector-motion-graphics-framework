using System;
using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// One entry of the ShapeStack: a shape plus a [0..1] intensity.
    /// Intensity is the weight in the N-way blend; 0 means the slot
    /// does not contribute (its shape isn't even evaluated).
    [Serializable]
    public struct ShapeSlot
    {
        public PrimitiveShapeSource shape;
        [Range(0f, 1f)]
        [Tooltip("Slot weight in the blend. 0 = this slot does not contribute. All slots are treated symmetrically; there is no special 'base' slot.")]
        public float intensity;
    }

    /// How sibling slots are aligned before the index-by-index blend.
    /// Two slots whose paths start at different positions (Circle at +x,
    /// Rectangle at bottom-left, Polygon at +y) will blend into a
    /// rotated, shrunken intermediate unless their first vertices are
    /// matched up first.
    public enum BlendAlignment
    {
        /// Reorder each closed slot path so it winds CCW and starts at
        /// the node closest to the +x axis. Best for blends between
        /// *different* shapes (Circle ↔ Rectangle ↔ Triangle): keeps
        /// the intermediate shape from shrinking. Discards any rotation
        /// the user encoded by authoring slot N at a different angle.
        Auto = 0,
        /// Use each slot's node order as authored. Best for blends
        /// between *the same shape at different rotations*: the morph
        /// visibly rotates as intensity shifts. The user is responsible
        /// for matching the slots' starting vertices.
        Preserve = 1,
    }

    /// Up to 4 PrimitiveShapeSources blended by arc-length resampling
    /// and per-slot intensity weights. Replaces the old "single shape +
    /// PathMorphModifier" pair — both base shape and morph targets are
    /// now just slots with equal weight semantics.
    ///
    /// All shape mixing collapses to this struct so animations can
    /// keyframe intensities directly. Slot 0 starts at intensity 1 so a
    /// freshly created renderer behaves like a single-shape renderer.
    ///
    /// Fixed 4 slots because Unity's Animation window can't enumerate
    /// list/array elements; flat fields are the only path to per-slot
    /// keyframing (same constraint that drove FlatNode in 0.9.0).
    [Serializable]
    public struct ShapeStack : IShapeSource
    {
        public const int MaxSlots = 4;

        [Range(8, 512)]
        [Tooltip("Vertex count every active slot is resampled to before the weighted blend. Keyframable but changing it mid-animation forces a topology change.")]
        public int resampleCount;

        [Tooltip("How slots are aligned before the blend. Auto reorders each path so morphs between different shapes don't shrink at the midpoint; Preserve keeps node order so same-shape-different-rotation morphs visibly rotate. Keyframable (enum int).")]
        public BlendAlignment alignment;

        public ShapeSlot m_Slot0;
        public ShapeSlot m_Slot1;
        public ShapeSlot m_Slot2;
        public ShapeSlot m_Slot3;

        /// Default: slot 0 = circle at intensity 1, slots 1..3 empty.
        /// Matches the "single-shape renderer" behaviour authors expect
        /// from a fresh GameObject.
        public static ShapeStack Default()
        {
            return new ShapeStack
            {
                resampleCount = 64,
                alignment = BlendAlignment.Auto,
                m_Slot0 = new ShapeSlot { shape = PrimitiveShapeSource.Default(), intensity = 1f },
                m_Slot1 = new ShapeSlot { shape = PrimitiveShapeSource.Default(), intensity = 0f },
                m_Slot2 = new ShapeSlot { shape = PrimitiveShapeSource.Default(), intensity = 0f },
                m_Slot3 = new ShapeSlot { shape = PrimitiveShapeSource.Default(), intensity = 0f },
            };
        }

        /// Magic-but-effective fixup for fields that deserialize as
        /// zero (struct field initializers aren't allowed). Mirrors the
        /// pattern used elsewhere in the package.
        public void Normalize()
        {
            if (resampleCount < 8) resampleCount = 64;
            m_Slot0.shape.Normalize();
            m_Slot1.shape.Normalize();
            m_Slot2.shape.Normalize();
            m_Slot3.shape.Normalize();
        }

        public ShapeSlot GetSlot(int i)
        {
            switch (i)
            {
                case 0: return m_Slot0;
                case 1: return m_Slot1;
                case 2: return m_Slot2;
                case 3: return m_Slot3;
                default: return default;
            }
        }

        public void SetSlot(int i, ShapeSlot v)
        {
            switch (i)
            {
                case 0: m_Slot0 = v; break;
                case 1: m_Slot1 = v; break;
                case 2: m_Slot2 = v; break;
                case 3: m_Slot3 = v; break;
            }
        }

        // Scratch shared across all stacks — Build is main-thread and
        // sequential, and the buffer is fully consumed before any
        // recursive call could re-enter (PathMorphModifier no longer
        // exists, so re-entry isn't a concern here).
        private static readonly VectorPath[] s_paths =
        {
            new VectorPath(), new VectorPath(), new VectorPath(), new VectorPath(),
        };
        private static readonly List<Vector2>[] s_samples =
        {
            new List<Vector2>(128), new List<Vector2>(128),
            new List<Vector2>(128), new List<Vector2>(128),
        };
        // Scratch for AlignClosedPath rotation step.
        private static readonly List<VectorNode> s_alignTmp = new List<VectorNode>(128);

        /// Reorders a closed path's nodes so (1) it winds CCW and
        /// (2) index 0 is the node whose direction from the centroid is
        /// closest to +x. This keeps morphs between primitives whose
        /// "first vertex" conventions differ (Circle at +x, Rectangle at
        /// the bottom-left corner, Polygon at +y) from producing a
        /// rotated and shrunken intermediate.
        private static void AlignClosedPath(VectorPath path)
        {
            int n = path.Count;
            if (n < 3) return;

            // 1. Flip to CCW if signed area is negative. We compute the
            //    shoelace sum and reverse the node order in place.
            float area2 = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = path.GetPoint(i);
                Vector2 b = path.GetPoint((i + 1) % n);
                area2 += a.x * b.y - b.x * a.y;
            }
            if (area2 < 0f)
            {
                int lo = 0, hi = n - 1;
                while (lo < hi)
                {
                    var tmp = path.nodes[lo];
                    path.nodes[lo] = path.nodes[hi];
                    path.nodes[hi] = tmp;
                    lo++; hi--;
                }
            }

            // 2. Centroid of the node positions. Cheap and good enough
            //    for the "which node points toward +x" decision — we don't
            //    need the geometric centroid of the filled polygon.
            Vector2 c = Vector2.zero;
            for (int i = 0; i < n; i++) c += path.GetPoint(i);
            c /= n;

            // 3. Find the node whose angle from the centroid is closest
            //    to 0 (the +x axis). atan2 sorts by angle; we want the
            //    smallest |atan2| value.
            int best = 0;
            float bestAbs = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = path.GetPoint(i) - c;
                if (d.sqrMagnitude < 1e-10f) continue;
                float a = Mathf.Abs(Mathf.Atan2(d.y, d.x));
                if (a < bestAbs) { bestAbs = a; best = i; }
            }
            if (best == 0) return;

            // 4. Rotate the node list so `best` becomes index 0. One
            //    pass into the scratch list, then copy back — keeps the
            //    operation allocation-free on the steady-state path.
            s_alignTmp.Clear();
            for (int i = 0; i < n; i++) s_alignTmp.Add(path.nodes[(best + i) % n]);
            for (int i = 0; i < n; i++) path.nodes[i] = s_alignTmp[i];
        }

        public void Build(VectorPath outPath)
        {
            Normalize();

            // First pass: build each slot's path and tally weight. A
            // slot with intensity == 0 is skipped entirely — we don't
            // even build its path. The "all zeros" case falls back to
            // slot 0 unconditionally so the renderer still shows
            // something.
            float total = 0f;
            int activeCount = 0;
            bool allClosed = true;
            for (int i = 0; i < MaxSlots; i++)
            {
                var slot = GetSlot(i);
                if (slot.intensity <= 0f) continue;
                s_paths[i].Clear();
                slot.shape.Build(s_paths[i]);
                if (s_paths[i].Count < 2) continue;
                total += slot.intensity;
                activeCount++;
                if (!s_paths[i].closed) allClosed = false;
            }

            outPath.Clear();
            if (activeCount == 0 || total < 1e-6f)
            {
                // No slot contributes — fall back to slot 0 raw so the
                // renderer doesn't go blank. Useful default state when
                // every intensity is zero.
                s_paths[0].Clear();
                m_Slot0.shape.Build(s_paths[0]);
                outPath.closed = s_paths[0].closed;
                for (int v = 0; v < s_paths[0].Count; v++) outPath.Add(s_paths[0].GetPoint(v));
                return;
            }

            int N = Mathf.Max(8, resampleCount);

            // Align each contributing closed path before resampling: pick
            // the node closest to the +x axis (relative to the path's
            // centroid) as index 0, and flip CW paths to CCW. Without
            // this, primitives with different "first vertex" conventions
            // (Circle starts at +x, Rectangle at -x/-y, Polygon at +y)
            // blend index-by-index into a rotated, shrunken intermediate
            // shape. Open paths are left alone — their endpoints are
            // semantically meaningful, so cyclic shift would change the
            // shape rather than just reindex it.
            //
            // Preserve mode skips this: the user is morphing between the
            // same shape at different rotations, and wants the rotation
            // to read in the morph instead of being normalized away.
            if (alignment == BlendAlignment.Auto)
            {
                for (int i = 0; i < MaxSlots; i++)
                {
                    var slot = GetSlot(i);
                    if (slot.intensity <= 0f) continue;
                    if (s_paths[i].Count < 2) continue;
                    if (s_paths[i].closed) AlignClosedPath(s_paths[i]);
                }
            }

            // Resample every contributing path to N points.
            for (int i = 0; i < MaxSlots; i++)
            {
                var slot = GetSlot(i);
                if (slot.intensity <= 0f) continue;
                if (s_paths[i].Count < 2) continue;
                ArcLengthResample.Resample(s_paths[i], N, s_samples[i]);
            }

            // Single contributing slot: skip the blend, copy directly.
            // This preserves vertex count when only one slot is active
            // (no quality loss from arc-length resampling either).
            if (activeCount == 1)
            {
                int only = -1;
                for (int i = 0; i < MaxSlots; i++)
                {
                    if (GetSlot(i).intensity > 0f && s_paths[i].Count >= 2) { only = i; break; }
                }
                outPath.closed = s_paths[only].closed;
                for (int v = 0; v < s_paths[only].Count; v++) outPath.Add(s_paths[only].GetPoint(v));
                return;
            }

            // N-way weighted blend.
            outPath.closed = allClosed;
            float inv = 1f / total;
            for (int v = 0; v < N; v++)
            {
                Vector2 acc = Vector2.zero;
                for (int i = 0; i < MaxSlots; i++)
                {
                    var slot = GetSlot(i);
                    if (slot.intensity <= 0f) continue;
                    if (s_samples[i].Count < N) continue;
                    acc += s_samples[i][v] * (slot.intensity * inv);
                }
                outPath.Add(acc);
            }
        }
    }
}
