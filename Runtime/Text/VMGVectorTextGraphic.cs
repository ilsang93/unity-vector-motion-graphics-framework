using UnityEngine;
using UnityEngine.UI;

namespace VMG.Text
{
    /// Internal companion Graphic that submits the vector-text mesh to a
    /// CanvasRenderer. Auto-created on a child of the VMGVectorTextUGUI
    /// object (UGUI permits only one Graphic per CanvasRenderer, and TMP
    /// owns the parent's). Delegates mesh generation back to its owner.
    [RequireComponent(typeof(CanvasRenderer))]
    [ExecuteAlways]
    [AddComponentMenu("")] // hidden from the Add Component menu
    public sealed class VMGVectorTextGraphic : MaskableGraphic
    {
        [System.NonSerialized] public VMGVectorTextUGUI Owner;

        // Same SDF-AA default material as VectorImageGraphic so glyph edges
        // antialias at any zoom. User can override by assigning a material.
        private static Material s_VmgDefaultMat;
        public override Material defaultMaterial
        {
            get
            {
                if (s_VmgDefaultMat == null)
                {
                    var shader = Shader.Find("VMG/UI/VectorSDF");
                    if (shader != null) s_VmgDefaultMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                return s_VmgDefaultMat != null ? s_VmgDefaultMat : base.defaultMaterial;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureCanvasUv1Channel();
            SetVerticesDirty();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnsureCanvasUv1Channel();
        }

        // SDF distance travels through UV1; UGUI strips it unless the owning
        // canvas opts in.
        private void EnsureCanvasUv1Channel()
        {
            var c = canvas;
            if (c == null) return;
            var root = c.rootCanvas != null ? c.rootCanvas : c;
            root.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            if (Owner == null) { vh.Clear(); return; }
            Owner.PopulateMesh(vh, color);
        }
    }
}
