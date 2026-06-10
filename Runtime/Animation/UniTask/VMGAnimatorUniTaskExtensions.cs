#if VMG_UNITASK
using System.Threading;
using Cysharp.Threading.Tasks;
using VMG.Animation;

namespace VMG.Animation.UniTaskSupport
{
    public static class VMGAnimatorUniTaskExtensions
    {
        /// UniTask-flavored wrapper for VMGAnimator.PlayAsync. Same semantics
        /// as the Task version: completes after the first 0→1 cycle in
        /// Internal mode; throws InvalidOperationException in External mode.
        public static UniTask PlayAsUniTask(this VMGAnimator animator, CancellationToken cancellationToken = default)
        {
            if (animator == null) return UniTask.CompletedTask;
            return animator.PlayAsync(cancellationToken).AsUniTask();
        }
    }
}
#endif
