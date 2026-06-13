using UnityEditor;
using UnityEngine;
using VMG.Animation;

namespace VMG.EditorTools.Animation
{
    public class VMGTimelineWindow : EditorWindow
    {
        [SerializeField] VMGAnimator m_Animator;
        VMGTimelineView m_Timeline;
        VMGEditorPlayback m_Playback;

        public static void OpenFor(VMGAnimator animator)
        {
            var w = GetWindow<VMGTimelineWindow>(false, "VMG Timeline", true);
            w.Bind(animator);
            w.Show();
        }

        public static void FocusFor(VMGAnimator animator)
        {
            var w = GetWindow<VMGTimelineWindow>(false, "VMG Timeline", true);
            w.Bind(animator);
            w.Focus();
        }

        public static bool IsOpenFor(VMGAnimator animator)
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<VMGTimelineWindow>())
            {
                if (w != null && w.m_Animator == animator) return true;
            }
            return false;
        }

        void Bind(VMGAnimator animator)
        {
            m_Animator = animator;
            EnsureRuntime();
            m_Playback.Bind(animator);
        }

        void EnsureRuntime()
        {
            if (m_Timeline == null) m_Timeline = new VMGTimelineView { DrawAddTrackBarEnabled = true };
            if (m_Playback == null) m_Playback = new VMGEditorPlayback();
        }

        void OnEnable()
        {
            EnsureRuntime();
            if (m_Animator != null) m_Playback.Bind(m_Animator);
            Selection.selectionChanged += OnSelectionChanged;
            VMGTimelineSelection.Changed += OnTimelineSelectionChanged;
            VMGTimelineSelection.DataChanged += OnTimelineSelectionChanged;
            // Needed for key/row hover halo and tooltips.
            wantsMouseMove = true;
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            VMGTimelineSelection.Changed -= OnTimelineSelectionChanged;
            VMGTimelineSelection.DataChanged -= OnTimelineSelectionChanged;
            if (m_Playback != null) m_Playback.Unbind();
        }

        void OnTimelineSelectionChanged()
        {
            Repaint();
        }

        void OnSelectionChanged()
        {
            var sel = Selection.activeGameObject;
            if (sel == null) return;
            var a = sel.GetComponent<VMGAnimator>();
            if (a != null && a != m_Animator)
            {
                Bind(a);
                Repaint();
            }
        }

        void OnGUI()
        {
            // wantsMouseMove fires MouseMove but does not auto-repaint — we
            // need a Repaint to refresh the hover halo.
            if (Event.current.type == EventType.MouseMove) Repaint();

            EnsureRuntime();
            if (m_Animator == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject with a VMGAnimator to edit its clip here.", MessageType.Info);
                return;
            }
            if (m_Animator.clip == null)
            {
                EditorGUILayout.HelpBox($"'{m_Animator.name}' has no clip assigned.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"Animator: {m_Animator.name}", EditorStyles.boldLabel);

            m_Playback.Bind(m_Animator);
            var record = VMGEditorRecord.For(m_Animator);
            if (record != null)
            {
                m_Playback.Record = record;
                record.DrawControls();
            }
            m_Timeline.Playback = m_Playback;
            m_Playback.DrawControls();

            // Hand the timeline the remaining window space so the track
            // area can scroll vertically instead of overflowing the
            // window when many tracks are present. Subtract the bottom
            // Add Track/Group bar (one row of mini-buttons + margin).
            float yCursor = GUILayoutUtility.GetLastRect().yMax;
            float bottomBarReserve = 26f;
            float bodyHeight = Mathf.Max(60f, position.height - yCursor - bottomBarReserve - 4f);
            m_Timeline.Draw(m_Animator, bodyHeight);
        }
    }
}
