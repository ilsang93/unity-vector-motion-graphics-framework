#if VMG_DOTWEEN
using DG.Tweening;
using UnityEngine;
using VMG.Core;
using VMG.World;

namespace VMG.Tween
{
    /// DOTween extension methods for VectorSpriteRenderer (world-space).
    /// Mirrors the UGUI surface so gameplay code reads the same regardless
    /// of where the vector is rendered.
    public static class VectorSpriteRendererTween
    {
        public static Tweener DOFade(this VectorSpriteRenderer r, float endValue, float duration)
        {
            return DOTween.To(() => r.Fill.color.a, v =>
            {
                var f = r.Fill.color; f.a = v; r.Fill.color = f;
                var s = r.Stroke.color; s.a = v; r.Stroke.color = s;
            }, endValue, duration).SetTarget(r);
        }

        public static Tweener DOColor(this VectorSpriteRenderer r, Color endValue, float duration)
        {
            return DOTween.To(() => r.Fill.color, v => r.Fill.color = v, endValue, duration).SetTarget(r);
        }

        public static Tweener DOStrokeColor(this VectorSpriteRenderer r, Color endValue, float duration)
        {
            return DOTween.To(() => r.Stroke.color, v => r.Stroke.color = v, endValue, duration).SetTarget(r);
        }

        public static Tweener DOStrokeWidth(this VectorSpriteRenderer r, float endValue, float duration)
        {
            return DOTween.To(() => r.Stroke.width, v => r.Stroke.width = v, endValue, duration).SetTarget(r);
        }

        // -- Shape (slot 0) ----------------------------------------------

        public static Tweener DOSize(this VectorSpriteRenderer r, Vector2 endValue, float duration)
        {
            return DOTween.To(() => r.ShapeStack.Slot0.shape.size,
                              v => r.ShapeStack.Slot0.shape.size = v,
                              endValue, duration).SetTarget(r);
        }

        public static Tweener DOCornerRadius(this VectorSpriteRenderer r, float endValue, float duration)
        {
            return DOTween.To(() => r.ShapeStack.Slot0.shape.cornerRadii,
                              v => r.ShapeStack.Slot0.shape.cornerRadii = v,
                              new Vector2(endValue, endValue), duration).SetTarget(r);
        }

        public static Tweener DOCornerRadii(this VectorSpriteRenderer r, Vector2 endValue, float duration)
        {
            return DOTween.To(() => r.ShapeStack.Slot0.shape.cornerRadii,
                              v => r.ShapeStack.Slot0.shape.cornerRadii = v,
                              endValue, duration).SetTarget(r);
        }

        // -- ShapeStack slot intensity (replaces the old DOMorph) ---------

        public static Tweener DOSlotIntensity(this VectorSpriteRenderer r, int slotIndex, float endValue, float duration)
        {
            slotIndex = Mathf.Clamp(slotIndex, 0, ShapeStack.MaxSlots - 1);
            return DOTween.To(() => r.ShapeStack.GetSlot(slotIndex).intensity, v =>
            {
                var slot = r.ShapeStack.GetSlot(slotIndex);
                slot.intensity = v;
                r.ShapeStack.SetSlot(slotIndex, slot);
            }, endValue, duration).SetTarget(r);
        }

        // -- Modifiers ----------------------------------------------------

        public static Tweener DOTrim(this VectorSpriteRenderer r, float endValue, float duration)
        {
            r.Trim.enabled = true;
            return DOTween.To(() => r.Trim.end, v => r.Trim.end = v, endValue, duration).SetTarget(r);
        }

        public static Tweener DOTrimStart(this VectorSpriteRenderer r, float endValue, float duration)
        {
            r.Trim.enabled = true;
            return DOTween.To(() => r.Trim.start, v => r.Trim.start = v, endValue, duration).SetTarget(r);
        }

        public static Tweener DOTrimOffset(this VectorSpriteRenderer r, float endValue, float duration)
        {
            r.Trim.enabled = true;
            return DOTween.To(() => r.Trim.offset, v => r.Trim.offset = v, endValue, duration).SetTarget(r);
        }

        public static Tweener DORoundness(this VectorSpriteRenderer r, float endValue, float duration)
        {
            r.RoundCorners.enabled = true;
            return DOTween.To(() => r.RoundCorners.radius, v => r.RoundCorners.radius = v, endValue, duration).SetTarget(r);
        }
    }
}
#endif
