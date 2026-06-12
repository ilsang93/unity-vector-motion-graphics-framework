#if VMG_DOTWEEN
using DG.Tweening;
using UnityEngine;
using VMG.Core;
using VMG.UI;

namespace VMG.Tween
{
    /// DOTween extension methods for VectorImageGraphic (UGUI).
    ///
    /// All tweens go through DOTween.To(getter, setter, ...), so DOTween's
    /// existing UniTask integration (.AsyncWaitForCompletion(),
    /// .WithCancellation()) works without extra adapters.
    ///
    /// LateUpdate in VectorImageGraphic already marks vertices dirty every
    /// frame, so setters don't need to call SetVerticesDirty themselves.
    ///
    /// The "Shape" tweens here target slot 0 of the ShapeStack — that's
    /// the slot that holds the renderer's "primary" shape in a freshly
    /// created renderer. For tweens targeting other slots, use
    /// DOSlotIntensity or just write to `g.ShapeStack.SlotN.shape.*`
    /// directly inside a DOTween.To setter.
    public static class VectorImageGraphicTween
    {
        // -- Color / alpha -----------------------------------------------

        public static Tweener DOFade(this VectorImageGraphic g, float endValue, float duration)
        {
            return DOTween.To(() => g.color.a, v =>
            {
                var c = g.color; c.a = v; g.color = c;
            }, endValue, duration).SetTarget(g);
        }

        public static Tweener DOColor(this VectorImageGraphic g, Color endValue, float duration)
        {
            return DOTween.To(() => g.Fill.color, v => g.Fill.color = v, endValue, duration).SetTarget(g);
        }

        public static Tweener DOStrokeColor(this VectorImageGraphic g, Color endValue, float duration)
        {
            return DOTween.To(() => g.Stroke.color, v => g.Stroke.color = v, endValue, duration).SetTarget(g);
        }

        // -- Stroke ------------------------------------------------------

        public static Tweener DOStrokeWidth(this VectorImageGraphic g, float endValue, float duration)
        {
            return DOTween.To(() => g.Stroke.width, v => g.Stroke.width = v, endValue, duration).SetTarget(g);
        }

        // -- Shape (slot 0) ----------------------------------------------

        public static Tweener DOSize(this VectorImageGraphic g, Vector2 endValue, float duration)
        {
            return DOTween.To(() => g.ShapeStack.Slot0.shape.size,
                              v => g.ShapeStack.Slot0.shape.size = v,
                              endValue, duration).SetTarget(g);
        }

        public static Tweener DOCornerRadius(this VectorImageGraphic g, float endValue, float duration)
        {
            return DOTween.To(() => g.ShapeStack.Slot0.shape.cornerRadii,
                              v => g.ShapeStack.Slot0.shape.cornerRadii = v,
                              new Vector2(endValue, endValue), duration).SetTarget(g);
        }

        public static Tweener DOCornerRadii(this VectorImageGraphic g, Vector2 endValue, float duration)
        {
            return DOTween.To(() => g.ShapeStack.Slot0.shape.cornerRadii,
                              v => g.ShapeStack.Slot0.shape.cornerRadii = v,
                              endValue, duration).SetTarget(g);
        }

        // -- ShapeStack slot intensity (replaces the old DOMorph) ---------

        /// Tween the intensity of a specific stack slot. This is the
        /// modern equivalent of the old DOMorph API — to morph from
        /// shape A to shape B, put A in slot 0 (intensity 1), B in
        /// slot 1 (intensity 0), then tween slot 1's intensity to 1
        /// (and optionally slot 0's down to 0).
        public static Tweener DOSlotIntensity(this VectorImageGraphic g, int slotIndex, float endValue, float duration)
        {
            slotIndex = Mathf.Clamp(slotIndex, 0, ShapeStack.MaxSlots - 1);
            return DOTween.To(() => g.ShapeStack.GetSlot(slotIndex).intensity, v =>
            {
                var slot = g.ShapeStack.GetSlot(slotIndex);
                slot.intensity = v;
                g.ShapeStack.SetSlot(slotIndex, slot);
            }, endValue, duration).SetTarget(g);
        }

        // -- Modifiers ----------------------------------------------------

        // Modifiers are structs exposed as public fields; lambda captures
        // operate on the field in place.

        public static Tweener DOTrim(this VectorImageGraphic g, float endValue, float duration)
        {
            g.Trim.enabled = true;
            return DOTween.To(() => g.Trim.end, v => g.Trim.end = v, endValue, duration).SetTarget(g);
        }

        public static Tweener DOTrimStart(this VectorImageGraphic g, float endValue, float duration)
        {
            g.Trim.enabled = true;
            return DOTween.To(() => g.Trim.start, v => g.Trim.start = v, endValue, duration).SetTarget(g);
        }

        public static Tweener DOTrimOffset(this VectorImageGraphic g, float endValue, float duration)
        {
            g.Trim.enabled = true;
            return DOTween.To(() => g.Trim.offset, v => g.Trim.offset = v, endValue, duration).SetTarget(g);
        }

        public static Tweener DORoundness(this VectorImageGraphic g, float endValue, float duration)
        {
            g.RoundCorners.enabled = true;
            return DOTween.To(() => g.RoundCorners.radius, v => g.RoundCorners.radius = v, endValue, duration).SetTarget(g);
        }
    }
}
#endif
