#if VMG_DOTWEEN
using DG.Tweening;
using UnityEngine;
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

        // -- Shape -------------------------------------------------------

        public static Tweener DOSize(this VectorImageGraphic g, Vector2 endValue, float duration)
        {
            return DOTween.To(() => g.Shape.size, v => g.Shape.size = v, endValue, duration).SetTarget(g);
        }

        public static Tweener DOCornerRadius(this VectorImageGraphic g, float endValue, float duration)
        {
            return DOTween.To(() => g.Shape.cornerRadius, v => g.Shape.cornerRadius = v, endValue, duration).SetTarget(g);
        }

        // -- Modifiers ---------------------------------------------------

        // Modifiers are structs now, so the local `var trim = ...`
        // pattern would copy and the tween setter would mutate that
        // copy. Each lambda reaches through `g.XModifier` every call so
        // the ref-returning property forwards the write to the real
        // field. Enabling the modifier is done eagerly via the same
        // ref-returning getter before the tween starts.

        public static Tweener DOTrim(this VectorImageGraphic g, float endValue, float duration)
        {
            g.TrimModifier.enabled = true;
            return DOTween.To(() => g.TrimModifier.end, v => g.TrimModifier.end = v, endValue, duration).SetTarget(g);
        }

        public static Tweener DOTrimStart(this VectorImageGraphic g, float endValue, float duration)
        {
            g.TrimModifier.enabled = true;
            return DOTween.To(() => g.TrimModifier.start, v => g.TrimModifier.start = v, endValue, duration).SetTarget(g);
        }

        public static Tweener DOTrimOffset(this VectorImageGraphic g, float endValue, float duration)
        {
            g.TrimModifier.enabled = true;
            return DOTween.To(() => g.TrimModifier.offset, v => g.TrimModifier.offset = v, endValue, duration).SetTarget(g);
        }

        public static Tweener DORoundness(this VectorImageGraphic g, float endValue, float duration)
        {
            g.RoundCornerModifier.enabled = true;
            return DOTween.To(() => g.RoundCornerModifier.radius, v => g.RoundCornerModifier.radius = v, endValue, duration).SetTarget(g);
        }

        public static Tweener DOMorph(this VectorImageGraphic g, float endValue, float duration)
        {
            g.MorphModifier.enabled = true;
            return DOTween.To(() => g.MorphModifier.progress, v => g.MorphModifier.progress = v, endValue, duration).SetTarget(g);
        }
    }
}
#endif
