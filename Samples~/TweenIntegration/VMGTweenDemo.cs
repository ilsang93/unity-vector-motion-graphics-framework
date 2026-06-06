#if VMG_DOTWEEN
using DG.Tweening;
using UnityEngine;
using VMG.Tween;
using VMG.UI;

namespace VMG.Samples
{
    /// Drop on a Vector Image to see DOTween extensions in action.
    /// Plays a short choreographed sequence on Start.
    [RequireComponent(typeof(VectorImageGraphic))]
    public sealed class VMGTweenDemo : MonoBehaviour
    {
        private VectorImageGraphic m_Graphic;

        private void Start()
        {
            m_Graphic = GetComponent<VectorImageGraphic>();

            // Draw-on effect: trim end animates 0 -> 1.
            m_Graphic.TrimModifier.start = 0f;
            m_Graphic.TrimModifier.end = 0f;
            m_Graphic.DOTrim(1f, 0.8f).SetEase(Ease.OutCubic);

            // Stroke width pulse.
            float baseWidth = m_Graphic.Stroke.width;
            m_Graphic.DOStrokeWidth(baseWidth * 2f, 0.4f)
                     .SetEase(Ease.OutSine)
                     .SetLoops(2, LoopType.Yoyo)
                     .SetDelay(0.8f);

            // Color cycle.
            m_Graphic.DOStrokeColor(Color.cyan, 1.6f).SetDelay(0.4f);
        }
    }
}
#endif
