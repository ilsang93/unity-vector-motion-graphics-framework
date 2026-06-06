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
