using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VMG.UI;
using VMG.World;

namespace VMG.EditorTools
{
    internal static class VectorImageMenu
    {
        // Placed right below the default UI/Image entry (sortKey 2030).
        [MenuItem("GameObject/UI/Vector Image", false, 2031)]
        private static void CreateVectorImage(MenuCommand command)
        {
            GameObject parent = command.context as GameObject;
            Canvas canvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
            if (canvas == null) canvas = FindOrCreateCanvas();

            GameObject go = new GameObject("Vector Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectorImageGraphic));
            GameObjectUtility.SetParentAndAlign(go, parent != null ? parent : canvas.gameObject);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160f, 160f);
            rt.anchoredPosition = Vector2.zero;

            Undo.RegisterCreatedObjectUndo(go, "Create Vector Image");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        [MenuItem("GameObject/2D Object/Vector Sprite Renderer", false, 1)]
        private static void CreateVectorSprite(MenuCommand command)
        {
            GameObject parent = command.context as GameObject;
            GameObject go = new GameObject("Vector Sprite", typeof(MeshFilter), typeof(MeshRenderer), typeof(VectorSpriteRenderer));
            GameObjectUtility.SetParentAndAlign(go, parent);

            // World-space default: 1m × 1m. PrimitiveShapeSource's lazy
            // Normalize() picks 100 for pixel-friendly UGUI use, which
            // would fill a default camera's view here. Stroke width also
            // scales down to match (default 4 would be 4m otherwise).
            var renderer = go.GetComponent<VectorSpriteRenderer>();
            ref var stack = ref renderer.ShapeStack;
            var size = new Vector2(1f, 1f);
            stack.m_Slot0.shape.size = size;
            stack.m_Slot1.shape.size = size;
            stack.m_Slot2.shape.size = size;
            stack.m_Slot3.shape.size = size;
            renderer.Stroke.width = 0.04f;

            Undo.RegisterCreatedObjectUndo(go, "Create Vector Sprite Renderer");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        private static Canvas FindOrCreateCanvas()
        {
#if UNITY_2023_1_OR_NEWER
            var canvas = Object.FindFirstObjectByType<Canvas>();
#else
            var canvas = Object.FindObjectOfType<Canvas>();
#endif
            if (canvas != null) return canvas;

            GameObject canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create Canvas");

#if UNITY_2023_1_OR_NEWER
            var es = Object.FindFirstObjectByType<EventSystem>();
#else
            var es = Object.FindObjectOfType<EventSystem>();
#endif
            if (es == null)
            {
                GameObject esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
            }
            return canvas;
        }
    }
}
