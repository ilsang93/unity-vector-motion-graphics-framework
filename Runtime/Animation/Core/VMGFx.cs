using UnityEngine;

namespace VMG.Animation.Core
{
    // Facade for the code-driven motion API. anime.js's
    //   animate(target, { x: 100, duration: 0.5 })
    // maps to
    //   VMGFx.Animate(target).To("x", 100f).Duration(0.5f);
    //
    // Named "VMGFx" not "VMG" because Unity's compiler resolves the simple
    // name "VMG" to the namespace, never the type, even with a using directive.
    // VMGFx (motion / effects facade) avoids the shadowing and keeps a short
    // call site. Future siblings (planned): VMGFx.Timeline(), VMGFx.Scene().
    public static class VMGFx
    {
        public static VMGAnimate Animate(Component target)
        {
            return new VMGAnimate(target);
        }

        public static VMGTimeline Timeline()
        {
            return new VMGTimeline();
        }

        // Scene composition. The root's Transform flavour decides which
        // renderer Add() materialises: RectTransform → VectorImageGraphic
        // (UGUI), plain Transform → VectorSpriteRenderer (World).
        public static VMGScene Scene(Transform root)
        {
            return new VMGScene(root);
        }

        // ----- Shape descriptor factories (consumed by VMGScene.Add) -----

        public static VMGCircleDescriptor Circle() => new VMGCircleDescriptor();
        public static VMGEllipseDescriptor Ellipse() => new VMGEllipseDescriptor();
        public static VMGRectangleDescriptor Rectangle() => new VMGRectangleDescriptor();
        public static VMGRoundedRectangleDescriptor RoundedRectangle() => new VMGRoundedRectangleDescriptor();
        public static VMGPolygonDescriptor Polygon() => new VMGPolygonDescriptor();
        public static VMGPathDescriptor Path() => new VMGPathDescriptor();
    }
}
