#if VMG_DOTWEEN
using DG.Tweening;
using UnityEngine;
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

        public static Tweener DOSize(this VectorSpriteRenderer r, Vector2 endValue, float duration)
        {
            return DOTween.To(() => r.Shape.size, v => r.Shape.size = v, endValue, duration).SetTarget(r);
        }

        public static Tweener DOCornerRadius(this VectorSpriteRenderer r, float endValue, float duration)
        {
            return DOTween.To(() => r.Shape.cornerRadius, v => r.Shape.cornerRadius = v, endValue, duration).SetTarget(r);
        }

        // Modifiers are structs now, so the local `var trim = ...`
        // pattern would copy and the tween setter would mutate that
        // copy. Each lambda reaches through `r.XModifier` every call so
        // the ref-returning property forwards the write to the real
        // field.

        public static Tweener DOTrim(this VectorSpriteRenderer r, float endValue, float duration)
        {
            r.TrimModifier.enabled = true;
            return DOTween.To(() => r.TrimModifier.end, v => r.TrimModifier.end = v, endValue, duration).SetTarget(r);
        }

        public static Tweener DOTrimStart(this VectorSpriteRenderer r, float endValue, float duration)
        {
            r.TrimModifier.enabled = true;
            return DOTween.To(() => r.TrimModifier.start, v => r.TrimModifier.start = v, endValue, duration).SetTarget(r);
        }

        public static Tweener DOTrimOffset(this VectorSpriteRenderer r, float endValue, float duration)
        {
            r.TrimModifier.enabled = true;
            return DOTween.To(() => r.TrimModifier.offset, v => r.TrimModifier.offset = v, endValue, duration).SetTarget(r);
        }

        public static Tweener DORoundness(this VectorSpriteRenderer r, float endValue, float duration)
        {
            r.RoundCornerModifier.enabled = true;
            return DOTween.To(() => r.RoundCornerModifier.radius, v => r.RoundCornerModifier.radius = v, endValue, duration).SetTarget(r);
        }

        public static Tweener DOMorph(this VectorSpriteRenderer r, float endValue, float duration)
        {
            r.MorphModifier.enabled = true;
            return DOTween.To(() => r.MorphModifier.progress, v => r.MorphModifier.progress = v, endValue, duration).SetTarget(r);
        }
    }
}
#endif
