using System.Collections.Generic;
using UnityEngine;
using VMG.Core;
using VMG.UI;
using VMG.World;

namespace VMG.Utility
{
    /// Drives the TrimPathModifier of many vector renderers from a single
    /// `Progress` scalar so a group of strokes appears to be drawn on in
    /// sequence — the "magic circle" / handwriting reveal.
    ///
    /// Distribution is arc-length weighted: each stroke owns a share of
    /// the 0..1 range proportional to its own path length, so the pen
    /// moves at a constant apparent speed across the whole group instead
    /// of speeding up on long strokes and crawling on short ones.
    ///
    /// `Overlap` blends neighbouring strokes: 0 is strictly sequential
    /// (a stroke starts only once the previous finished), 0.3 starts the
    /// next stroke when the previous is 70% drawn.
    ///
    /// Trim is a stroke-only modifier in the VMG pipeline (fill skips it
    /// so closed shapes survive the slice), so this component reveals
    /// stroked line art. Filled shapes are left alone — see the
    /// `Fill.enabled` note in the renderers.
    ///
    /// This component owns no playback state. Animate `Progress` from
    /// VMGAnimator / an AnimationClip / a .vmgfx timeline; keeping the
    /// clock in one place avoids two systems fighting over the value.
    [AddComponentMenu("VMG/Utility/Draw-On Group", 1)]
    [ExecuteAlways]
    [DefaultExecutionOrder(-50)]
    public sealed class VMGDrawOnGroup : MonoBehaviour
    {
        [Range(0f, 1f)]
        [Tooltip("Overall draw-on progress. 0 = nothing drawn, 1 = fully drawn. Keyframable — animate this single value instead of every stroke's trim.")]
        public float Progress = 1f;

        [Range(0f, 0.95f)]
        [Tooltip("How much each stroke overlaps the next. 0 = strictly sequential. 0.3 = the next stroke starts when the previous is 70% drawn.")]
        public float Overlap;

        [Tooltip("Strokes to drive, in draw order. Leave empty to collect renderers from children automatically (hierarchy order).")]
        public List<Component> Strokes = new List<Component>();

        [Tooltip("Reverse the draw order without reordering the list or the hierarchy.")]
        public bool ReverseOrder;

        [Tooltip("Disable a stroke's GameObject until its turn comes. Off by default — an untouched stroke already renders nothing once trimmed to zero length.")]
        public bool DeactivateUntilDrawn;

        // Resolved draw order + per-stroke arc length. Rebuilt when the
        // source list or the child set changes; lengths refresh with it
        // because a stroke's shape can be animated too.
        readonly List<Component> m_Resolved = new List<Component>();
        readonly List<float> m_Lengths = new List<float>();
        float m_TotalLength;

        // Scratch path for length measurement. Reused so per-frame
        // measurement doesn't allocate.
        static readonly VectorPath s_MeasurePath = new VectorPath();

        void OnEnable()
        {
            Rebuild();
            Apply();
        }

        void LateUpdate()
        {
            // Shapes can be animated, so lengths are not stable. Measuring
            // is a cheap polyline walk and only runs for the strokes we
            // drive, so refresh every frame rather than caching stale
            // weights that would make the reveal drift.
            Measure();
            Apply();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Keep the scene view honest while the user drags Progress
            // or edits the stroke list in the inspector.
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this == null) return;
                    Rebuild();
                    Apply();
                };
            }
        }
#endif

        /// Re-collects the driven strokes. Call after adding or removing
        /// stroke GameObjects at runtime when relying on auto-collection.
        public void Rebuild()
        {
            m_Resolved.Clear();

            if (Strokes != null && Strokes.Count > 0)
            {
                for (int i = 0; i < Strokes.Count; i++)
                {
                    if (IsDrivable(Strokes[i])) m_Resolved.Add(Strokes[i]);
                }
            }
            else
            {
                CollectChildren();
            }

            if (ReverseOrder) m_Resolved.Reverse();
            Measure();
        }

        void CollectChildren()
        {
            // GetComponentsInChildren returns hierarchy order, which is
            // the order the user sees in the Hierarchy window — the least
            // surprising default draw order.
            var ui = GetComponentsInChildren<VectorImageGraphic>(true);
            for (int i = 0; i < ui.Length; i++) m_Resolved.Add(ui[i]);

            var world = GetComponentsInChildren<VectorSpriteRenderer>(true);
            for (int i = 0; i < world.Length; i++) m_Resolved.Add(world[i]);
        }

        static bool IsDrivable(Component c)
        {
            return c is VectorImageGraphic || c is VectorSpriteRenderer;
        }

        void Measure()
        {
            m_Lengths.Clear();
            m_TotalLength = 0f;
            for (int i = 0; i < m_Resolved.Count; i++)
            {
                float len = MeasureOne(m_Resolved[i]);
                m_Lengths.Add(len);
                m_TotalLength += len;
            }
        }

        static float MeasureOne(Component c)
        {
            s_MeasurePath.Clear();

            var ui = c as VectorImageGraphic;
            if (ui != null)
            {
                // SVG-backed renderers bypass ShapeStack entirely, so there
                // is no procedural path to measure. Fall back to a unit
                // weight — the stroke still gets an even share of the
                // sequence rather than collapsing to zero.
                if (ui.SvgAsset != null) return 1f;
                ui.ShapeStack.Build(s_MeasurePath);
            }
            else
            {
                var world = c as VectorSpriteRenderer;
                if (world == null) return 0f;
                if (world.SvgAsset != null) return 1f;
                world.ShapeStack.Build(s_MeasurePath);
            }

            return PathLength(s_MeasurePath);
        }

        static float PathLength(VectorPath path)
        {
            int n = path.Count;
            if (n < 2) return 0f;
            int segCount = path.closed ? n : n - 1;
            float total = 0f;
            for (int i = 0; i < segCount; i++)
            {
                total += Vector2.Distance(
                    path.nodes[i].position,
                    path.nodes[(i + 1) % n].position);
            }
            return total;
        }

        void Apply()
        {
            int count = m_Resolved.Count;
            if (count == 0) return;

            float p = Mathf.Clamp01(Progress);

            // Overlap shrinks each stroke's slot while keeping the slots
            // spanning 0..1, so neighbouring windows intersect. With
            // overlap o and normalised weight w, a stroke's window is
            // widened by a factor of 1/(1 - o) and the starts stay
            // proportional to the cumulative length before it.
            float o = Mathf.Clamp(Overlap, 0f, 0.95f);
            float widen = 1f / (1f - o);

            // Degenerate total (every stroke zero-length, or all SVG with
            // unit weights zeroed out) → fall back to even distribution so
            // the reveal still sequences instead of dividing by zero.
            bool evenFallback = m_TotalLength <= 1e-6f;

            float cumulative = 0f;
            for (int i = 0; i < count; i++)
            {
                float weight = evenFallback
                    ? 1f / count
                    : m_Lengths[i] / m_TotalLength;

                // Sequential window for this stroke, then widened by the
                // overlap factor about its own start.
                float start = evenFallback ? (float)i / count : cumulative;
                float end = Mathf.Min(1f, start + weight * widen);
                cumulative += weight;

                float local;
                if (end - start <= 1e-6f)
                {
                    // Zero-length stroke: it is "done" the moment the
                    // playhead reaches it, so it never blocks the sequence.
                    local = p >= start ? 1f : 0f;
                }
                else
                {
                    local = Mathf.Clamp01((p - start) / (end - start));
                }

                SetTrim(m_Resolved[i], local);
            }
        }

        void SetTrim(Component c, float local)
        {
            // local is how much of THIS stroke is drawn. Trim start stays
            // at 0 and end sweeps 0→1 so the stroke grows from its path
            // origin — the pen-stroke reading. Trim is force-enabled while
            // this component drives the stroke; a fully drawn stroke keeps
            // end = 1, which the modifier short-circuits as a no-op.
            var ui = c as VectorImageGraphic;
            if (ui != null)
            {
                var t = ui.Trim;
                t.enabled = true;
                t.start = 0f;
                t.end = local;
                ui.Trim = t;
                ui.SetMeshDirty();
                ApplyActivation(ui.gameObject, local);
                return;
            }

            var world = c as VectorSpriteRenderer;
            if (world != null)
            {
                var t = world.Trim;
                t.enabled = true;
                t.start = 0f;
                t.end = local;
                world.Trim = t;
                world.SetMeshDirty();
                ApplyActivation(world.gameObject, local);
            }
        }

        void ApplyActivation(GameObject go, float local)
        {
            if (!DeactivateUntilDrawn) return;
            bool shouldBeActive = local > 0f;
            if (go.activeSelf != shouldBeActive) go.SetActive(shouldBeActive);
        }
    }
}
