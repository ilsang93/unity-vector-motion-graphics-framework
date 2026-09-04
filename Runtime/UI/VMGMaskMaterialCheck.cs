using System.Collections.Generic;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using VMGObjectId = UnityEngine.EntityId;
#else
using VMGObjectId = System.Int32;
#endif

namespace VMG.UI
{
    /// Helper shared by VMGMaskSource / VMGMaskClient (and usable elsewhere).
    ///
    /// Unity's stencil masking only works if the material's shader declares the
    /// standard UGUI stencil block (`_Stencil`, `_StencilComp`, `_StencilOp`,
    /// `_StencilReadMask`, `_StencilWriteMask`, `_ColorMask`). UI/Default and
    /// VMG/UI/VectorSDF do. Arbitrary FX / ShaderGraph / Amplify materials
    /// (e.g. a dissolve shader) usually do NOT — assigning one to a masked
    /// Graphic makes `StencilMaterial.Add` a no-op and the mask SILENTLY fails.
    ///
    /// This surfaces a one-shot, per-shader warning so the failure is
    /// diagnosable instead of mysterious.
    internal static class VMGMaskMaterialCheck
    {
        static readonly HashSet<VMGObjectId> s_Warned = new HashSet<VMGObjectId>();
#if UNITY_6000_5_OR_NEWER
        static VMGObjectId ObjectId(UnityEngine.Object o) => o.GetEntityId();
#else
        static VMGObjectId ObjectId(UnityEngine.Object o) => o.GetInstanceID();
#endif

        /// Returns true if the material can participate in stencil masking.
        /// Logs a one-shot warning (keyed by shader) when it can't.
        public static bool ValidateStencilCapable(Material mat, Object context, string role)
        {
            if (mat == null || mat.shader == null) return true; // null → default mat handles it
            if (mat.HasProperty("_Stencil") && mat.HasProperty("_StencilComp")) return true;

            VMGObjectId key = ObjectId(mat.shader);
            if (s_Warned.Add(key))
            {
                Debug.LogWarning(
                    $"[VMG Mask] The material '{mat.name}' (shader '{mat.shader.name}') on a {role} " +
                    "has no stencil block, so Unity cannot mask it — the masked content will render " +
                    "unclipped. Use VMG/UI/VectorSDF (or any UI shader that declares _Stencil / " +
                    "_StencilComp / _StencilOp / _StencilReadMask / _StencilWriteMask / _ColorMask), " +
                    "or add that block to your custom shader.", context);
            }
            return false;
        }
    }
}
