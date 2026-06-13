using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VMG.Animation;

namespace VMG.EditorTools.Animation
{
    internal class VMGTimelineView
    {
        const float k_LabelWidth = 140f;
        const float k_RowHeight = 22f;
        const float k_RulerHeight = 18f;
        const float k_EventRowHeight = 18f;
        const float k_KeyHalfWidth = 5f;   // diamond half-width (px)
        const float k_KeyHalfHeight = 6f;  // diamond half-height (px)
        const float k_KeyHitRadius = 8f;
        const float k_RightPad = 10f;

        // Unity Animation-window-style palette.
        static readonly Color k_BgColor = new Color(0.18f, 0.18f, 0.18f, 1f);
        static readonly Color k_AltRowColor = new Color(1f, 1f, 1f, 0.035f);
        static readonly Color k_RowHoverColor = new Color(1f, 1f, 1f, 0.05f);
        static readonly Color k_RulerColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        static readonly Color k_GridColor = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color k_GridMinorColor = new Color(1f, 1f, 1f, 0.04f);
        static readonly Color k_SnapGridColor = new Color(1f, 1f, 1f, 0.04f);
        const float k_MinSnapGridPixels = 6f;
        static readonly Color k_BorderColor = new Color(0f, 0f, 0f, 0.6f);
        // Unity key colors: white = normal, blue = selected, yellow = recording.
        static readonly Color k_KeyColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        static readonly Color k_KeySelectedColor = new Color(0.30f, 0.55f, 0.95f, 1f);
        static readonly Color k_KeyRecordingColor = new Color(0.95f, 0.78f, 0.20f, 1f);
        static readonly Color k_KeyOutline = new Color(0f, 0f, 0f, 0.85f);
        // White hover halo, Unity-style.
        static readonly Color k_KeyHoverHalo = new Color(1f, 1f, 1f, 0.35f);
        const float k_KeyHoverHaloPad = 2.5f;
        static readonly Color k_TrackSelectedColor = new Color(0.30f, 0.55f, 0.95f, 0.14f);
        static readonly Color k_TrackSelectedBorder = new Color(0.30f, 0.55f, 0.95f, 0.85f);
        static readonly Color k_EventColor = new Color(0.4f, 0.85f, 1f, 1f);
        static readonly Color k_EventSelectedColor = new Color(0.30f, 0.55f, 0.95f, 1f);
        static readonly Color k_EventSelectedOutline = new Color(1f, 1f, 1f, 0.9f);
        static readonly Color k_PlayheadColor = new Color(1f, 0.4f, 0.4f, 0.9f);

        VMGTimelineSelection m_Selection;

        int m_DraggingTrack = -1;
        int m_DraggingKey = -1;
        bool m_Scrubbing;
        bool m_KeyDragMoved;

        // Undo group id stamped at MouseDown so MouseUp can collapse all the
        // per-delta Undo entries into a single Ctrl+Z step.
        int m_DragUndoGroup = -1;

        // Multi-drag state.
        struct DragSnapshot { public int track; public int key; public float originalTime; }
        readonly List<DragSnapshot> m_DragSnapshots = new List<DragSnapshot>();
        float m_DragAnchorTime;
        bool m_MultiDragActive;

        // Pending toggle from Shift+click: applied on MouseUp if no drag occurred.
        bool m_PendingShiftToggle;
        int m_PendingToggleTrack;
        int m_PendingToggleKey;

        // Rubber-band selection state.
        bool m_RubberArmed;            // MouseDown happened on empty track area; waiting to confirm a drag.
        bool m_RubberActive;           // Drag exceeded threshold; rubber-band is drawing.
        Vector2 m_RubberStart;
        Vector2 m_RubberCurrent;
        bool m_RubberShift;
        List<VMGTimelineSelection.Item> m_RubberInitialSelection;
        const float k_RubberStartThreshold = 3f;

        // --- Row drag state (R1: auto-subgroup / user-group reorder) ---
        // Armed by MouseDown on a header row, promoted to active once the
        // pointer leaves the start-threshold radius. If MouseUp arrives
        // while still armed-but-inactive, the click falls back to the
        // header's existing toggle-collapse behavior.
        bool m_RowDragArmed;
        bool m_RowDragActive;
        Vector2 m_RowDragStart;
        Vector2 m_RowDragCurrent;
        int m_RowDragSourceRow = -1;     // index into m_Rows captured at MouseDown
        RowKind m_RowDragKind;
        int m_RowDragSourceUserGroupId;  // for AutoGroup source: parent user group id
        string m_RowDragSourceGroupKey;  // for AutoGroup source: BuildGroupKey result
        int m_RowDragSourceUgId;         // for UserGroup source: the group's own id
        const float k_RowDragStartThreshold = 4f;

        float m_PixelsPerSecond;
        float m_ScrollX;

        // Hover state for key halo + row highlight. Refreshed each MouseMove /
        // Repaint pass; -1 means "no hit".
        int m_HoverTrack = -1;
        int m_HoverKey = -1;
        int m_HoverRow = -1;

        // Ruler display unit. Persists per-window via EditorPrefs.
        bool m_RulerShowFrames = true;
        const string k_RulerFramesPrefKey = "VMG.Timeline.RulerShowFrames";

        // Cached for the current Draw pass — DrawTrackKeys runs deep in the
        // call chain and shouldn't reach for the animator on every frame.
        bool m_RecordingThisFrame;

        // --- Group/flatten state ---
        // Auto-derived from clip.tracks each Draw pass. Three row kinds:
        //   UserGroup header (depth 0, user-defined composition)
        //   Auto subgroup header (depth 0 if no user group, depth 1 inside one)
        //   Track row (depth = parent's depth + 1)
        // Collapsed user groups hide their auto subgroups AND tracks; collapsed
        // auto subgroups hide their tracks only.
        enum RowKind { UserGroup, AutoGroup, Track }

        struct Row
        {
            public RowKind kind;
            public int depth;             // 0 = top-level header, 1 = nested header / top-level track, 2 = nested track
            public string groupKey;       // collapse-set key (see BuildUserGroupKey / BuildGroupKey)
            public string headerLabel;
            public int trackIdx;          // -1 when not a Track row
            public int userGroupId;       // 0 = no user group; for Track/AutoGroup this is the parent user group id
            public int autoGroupHeaderRow; // for Track: row index of its auto subgroup header (or -1 if none drawn — shouldn't happen)
            public int userGroupHeaderRow; // for Track/AutoGroup inside a user group: row index of the user-group header
        }

        readonly List<Row> m_Rows = new List<Row>();
        // Per-clip collapse state. Keyed by (clip instance id, groupKey). Lives
        // in memory only — re-opening the window starts everything expanded.
        readonly Dictionary<int, HashSet<string>> m_CollapsedByClip = new Dictionary<int, HashSet<string>>();

        const float k_GroupCaretWidth = 14f;

        const float k_MinPps = 4f;
        const float k_MaxPps = 1000f;
        const float k_ZoomStep = 1.15f;
        const float k_ScrollbarHeight = 12f;

        public VMGEditorPlayback Playback { get; set; }
        public bool DrawAddTrackBarEnabled { get; set; } = true;

        bool m_PrefsLoaded;

        // Vertical scroll for the track area only. Ruler / events row /
        // playhead / scrollbars stay sticky. Updated by mouse wheel and
        // the right-side scrollbar; clamped each frame against the track
        // area's overflow.
        float m_ScrollY;
        // Track area extents captured each Draw pass so input helpers
        // (TryFindRow, hit-tests) can reject clicks above the ruler or
        // below the horizontal scrollbar without piling extra args onto
        // every callsite.
        float m_TrackAreaTop;
        float m_TrackAreaBottom;

        public void Draw(VMGAnimator animator) => Draw(animator, -1f);

        public void Draw(VMGAnimator animator, float maxBodyHeight)
        {
            var clip = animator.clip;
            if (clip == null) return;

            if (!m_PrefsLoaded)
            {
                m_RulerShowFrames = EditorPrefs.GetBool(k_RulerFramesPrefKey, true);
                m_PrefsLoaded = true;
            }

            m_Selection = VMGTimelineSelection.For(animator);
            var rec = VMGEditorRecord.For(animator);
            m_RecordingThisFrame = rec != null && rec.IsRecording;

            RebuildRows(clip);

            int rowCount = m_Rows.Count;
            float contentBodyHeight = Mathf.Max(rowCount, 1) * k_RowHeight; // tracks only
            float naturalHeight = k_RulerHeight + k_EventRowHeight + contentBodyHeight + 2f + k_ScrollbarHeight;

            // If caller supplied a hard cap (window-driven), honor it so
            // the timeline fits inside the window and the track area
            // scrolls vertically. Otherwise expand to fit all rows.
            float totalHeight = maxBodyHeight > 0f ? Mathf.Max(maxBodyHeight, k_RulerHeight + k_EventRowHeight + k_RowHeight + k_ScrollbarHeight) : naturalHeight;

            var rect = GUILayoutUtility.GetRect(0f, totalHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, k_BgColor);
            DrawBorder(rect);

            float tlLeft = rect.x + k_LabelWidth;
            // Reserve room on the right for the vertical scrollbar.
            float vBarReserve = k_ScrollbarHeight; // same thickness as horizontal bar for symmetry
            float tlRight = rect.xMax - k_RightPad - vBarReserve;
            float viewWidth = Mathf.Max(1f, tlRight - tlLeft);
            float duration = Mathf.Max(0.0001f, clip.duration);

            // Fit zoom always targets the clip *duration*, not the visible
            // window — pressing Fit snaps to "no headroom".
            float fitPps = viewWidth / duration;
            float pps = m_PixelsPerSecond > 0f ? m_PixelsPerSecond : fitPps;
            pps = Mathf.Clamp(pps, Mathf.Max(k_MinPps, fitPps * 0.5f), k_MaxPps);

            var rulerRect = new Rect(tlLeft, rect.y, viewWidth, k_RulerHeight);
            var eventRowRect = new Rect(tlLeft, rulerRect.yMax, viewWidth, k_EventRowHeight);
            var scrubRect = new Rect(tlLeft, rect.y, viewWidth, k_RulerHeight); // ruler only — events handled separately
            var zoomHotRect = new Rect(tlLeft, rect.y, viewWidth, totalHeight - k_ScrollbarHeight - 1f);
            HandleZoom(rulerRect, zoomHotRect, pps, fitPps, duration);
            // Re-read pps after possible zoom change.
            pps = m_PixelsPerSecond > 0f ? m_PixelsPerSecond : fitPps;
            pps = Mathf.Clamp(pps, Mathf.Max(k_MinPps, fitPps * 0.5f), k_MaxPps);

            // The visible window. When zoomed out past Fit, viewWidth covers
            // more seconds than `duration` — the surplus is the headroom for
            // extending keys past the current end. When zoomed in, the
            // window collapses back to viewWidth's worth of content and the
            // scrollbar covers the (duration - viewSeconds) overflow.
            float viewSeconds = viewWidth / Mathf.Max(pps, 0.0001f);
            float windowEnd = Mathf.Max(duration, viewSeconds);
            float contentWidth = windowEnd * pps;
            float maxScroll = Mathf.Max(0f, contentWidth - viewWidth);
            m_ScrollX = Mathf.Clamp(m_ScrollX, 0f, maxScroll);

            var gridRect = new Rect(tlLeft, rect.y, viewWidth, totalHeight - k_ScrollbarHeight - 2f);
            DrawSnapGrid(gridRect, clip, windowEnd, pps, m_ScrollX);
            DrawRuler(rulerRect, windowEnd, duration, pps, m_ScrollX, clip);

            EditorGUI.DrawRect(new Rect(rect.x, eventRowRect.y, k_LabelWidth, eventRowRect.height), k_RulerColor);
            GUI.Label(new Rect(rect.x + 4f, eventRowRect.y, k_LabelWidth - 6f, eventRowRect.height), "Events", EditorStyles.miniLabel);
            DrawEventsRow(eventRowRect, clip, windowEnd, pps, m_ScrollX);

            // Track area sits below ruler + events, ends above the
            // horizontal scrollbar. m_ScrollY shifts row drawing up; the
            // visible height is `trackAreaHeight`.
            float trackAreaTop = eventRowRect.yMax;
            float trackAreaHeight = Mathf.Max(0f, (rect.y + totalHeight - k_ScrollbarHeight - 1f) - trackAreaTop);
            float maxScrollY = Mathf.Max(0f, contentBodyHeight - trackAreaHeight);
            m_ScrollY = Mathf.Clamp(m_ScrollY, 0f, maxScrollY);
            float trackTop = trackAreaTop - m_ScrollY;
            m_TrackAreaTop = trackAreaTop;
            m_TrackAreaBottom = trackAreaTop + trackAreaHeight;

            // Refresh hover state for Repaint. Mouse coordinates are in
            // window space (unaffected by scroll); we pass the scrolled
            // trackTop so row hit-tests stay consistent with what's drawn.
            UpdateHover(Event.current.mousePosition, rect, tlLeft, trackTop, clip, windowEnd, pps, m_ScrollX);

            // Clip the track area so rows scrolled above/below the visible
            // band don't paint over ruler / scrollbar.
            var trackClipRect = new Rect(rect.x, trackAreaTop, rect.width, trackAreaHeight);
            GUI.BeginClip(trackClipRect, Vector2.zero, Vector2.zero, false);
            // GUI.BeginClip shifts the coordinate origin: subtract the
            // clip rect's position from anything we draw inside.
            float clipOffsetY = trackAreaTop;
            var clippedOuter = new Rect(rect.x, rect.y - clipOffsetY, rect.width, totalHeight);
            DrawTracks(clippedOuter, trackTop - clipOffsetY, tlLeft, viewWidth, clip, windowEnd, pps, m_ScrollX);
            GUI.EndClip();
            // Drop indicator draws outside the clip but is clamped to the
            // track area below — keeps the line from being chopped off
            // when the source row lives at the very top/bottom edge.
            DrawRowDropIndicator(rect, trackTop);

            // Dim the headroom (duration .. windowEnd) so it reads as "out of
            // clip" — keys still drag freely there, and dropping a key in
            // the dim band extends `duration` on MouseUp.
            DrawHeadroomDim(rect, tlLeft, viewWidth, duration, pps, m_ScrollX, totalHeight - k_ScrollbarHeight - 2f);

            DrawPlayhead(new Rect(tlLeft, rulerRect.y, viewWidth, totalHeight - k_ScrollbarHeight - 2f), animator.progress, duration, pps, m_ScrollX);
            DrawRubberBand();

            float scrollbarY = rect.y + totalHeight - k_ScrollbarHeight - 1f;
            DrawHorizontalScrollbar(new Rect(tlLeft, scrollbarY, viewWidth, k_ScrollbarHeight), viewWidth, contentWidth);
            DrawVerticalScrollbar(new Rect(rect.xMax - vBarReserve - 1f, trackAreaTop, vBarReserve, trackAreaHeight), trackAreaHeight, contentBodyHeight);

            // Mouse wheel over the track area scrolls vertically.
            HandleVerticalWheel(trackClipRect, maxScrollY);

            HandleInput(rect, tlLeft, viewWidth, trackTop, clip, windowEnd, animator, scrubRect, eventRowRect, pps, m_ScrollX);

            if (DrawAddTrackBarEnabled) DrawAddTrackBar(animator);
        }

        // ---------- coordinate helpers ----------

        static float TimeToX(float t, float tlLeft, float pps, float scrollX) =>
            tlLeft + (t * pps) - scrollX;

        static float XToTime(float x, float tlLeft, float pps, float scrollX) =>
            (x - tlLeft + scrollX) / Mathf.Max(pps, 0.0001f);

        /// Snap a time value to the clip's snap divisor. Shift held =
        /// caller passes shiftHeld=true to bypass snap.
        static float SnapTime(float t, VMGAnimationClip clip, bool shiftHeld)
        {
            if (shiftHeld || clip == null || clip.snapDivisor <= 0) return t;
            float step = 1f / clip.snapDivisor;
            return Mathf.Round(t / step) * step;
        }

        // ---------- drawing ----------

        static void DrawBorder(Rect rect)
        {
            var c = k_BorderColor;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), c);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), c);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), c);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), c);
        }

        static void DrawSnapGrid(Rect rect, VMGAnimationClip clip, float duration, float pps, float scrollX)
        {
            if (clip == null || clip.snapDivisor <= 0) return;
            float step = 1f / clip.snapDivisor;
            float pixelStep = step * pps;
            // Hide the snap grid when it's too dense to read.
            if (pixelStep < k_MinSnapGridPixels) return;

            int first = Mathf.FloorToInt(scrollX / pps / step);
            int last = Mathf.CeilToInt((scrollX + rect.width) / pps / step);
            for (int i = first; i <= last; i++)
            {
                float t = i * step;
                if (t < 0f || t > duration) continue;
                float x = TimeToX(t, rect.x, pps, scrollX);
                if (x < rect.x || x > rect.xMax) continue;
                EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), k_SnapGridColor);
            }
        }

        void DrawRuler(Rect rect, float windowEnd, float duration, float pps, float scrollX, VMGAnimationClip clip)
        {
            EditorGUI.DrawRect(rect, k_RulerColor);

            // s/f toggle in the label gutter to the LEFT of the ruler. The
            // ruler rect itself only spans the timeline area; the gutter is
            // [rect.x - k_LabelWidth, rect.x]. snapDivisor == 0 means "no
            // frame rate is defined", in which case the toggle is disabled
            // and the unit is forced to seconds.
            int fps = clip != null ? Mathf.Max(0, clip.snapDivisor) : 0;
            bool canShowFrames = fps > 0;
            bool showFrames = m_RulerShowFrames && canShowFrames;
            var togglePos = new Rect(rect.x - 36f, rect.y + 1f, 32f, rect.height - 2f);
            using (new EditorGUI.DisabledScope(!canShowFrames))
            {
                string label = showFrames ? "f" : "s";
                if (GUI.Button(togglePos, new GUIContent(label, canShowFrames
                        ? $"Ruler unit: {(showFrames ? "frames" : "seconds")} (click to switch). Frame rate = clip.snapDivisor."
                        : "Set clip.snapDivisor > 0 to enable frame display."),
                    EditorStyles.miniButton))
                {
                    m_RulerShowFrames = !m_RulerShowFrames;
                    EditorPrefs.SetBool(k_RulerFramesPrefKey, m_RulerShowFrames);
                }
            }

            float pixelsPerUnit = showFrames ? pps / Mathf.Max(1, fps) : pps;  // px per (frame|second)
            float visibleUnits = rect.width / Mathf.Max(pixelsPerUnit, 0.0001f);
            float majorStep = ChooseGridStep(visibleUnits, showFrames);
            // Drop minor ticks when they would be denser than ~5px apart.
            float minorStep = (majorStep * 0.25f * pixelsPerUnit >= 5f) ? majorStep * 0.25f : majorStep;

            int firstMinor = Mathf.FloorToInt((scrollX / pps) * (showFrames ? fps : 1f) / minorStep);
            int lastMinor = Mathf.CeilToInt(((scrollX + rect.width) / pps) * (showFrames ? fps : 1f) / minorStep);
            // Ticks now extend all the way to windowEnd, so headroom shows
            // ruler marks too. duration is still highlighted with a 1px end
            // marker and the dim overlay (drawn later).
            float maxUnit = windowEnd * (showFrames ? fps : 1f);

            var labelStyle = EditorStyles.miniLabel;
            for (int i = firstMinor; i <= lastMinor; i++)
            {
                float u = i * minorStep;
                if (u < 0f || u > maxUnit + 1e-4f) continue;
                float tSec = showFrames ? u / Mathf.Max(1f, fps) : u;
                float x = TimeToX(tSec, rect.x, pps, scrollX);
                if (x < rect.x || x > rect.xMax) continue;
                bool isMajor = Mathf.Abs(u / majorStep - Mathf.Round(u / majorStep)) < 1e-3f;
                EditorGUI.DrawRect(new Rect(x, rect.y + (isMajor ? 0f : rect.height * 0.55f), 1f, isMajor ? rect.height : rect.height * 0.45f), isMajor ? k_GridColor : k_GridMinorColor);
                if (isMajor)
                {
                    string s = showFrames ? Mathf.RoundToInt(u).ToString() : $"{u:0.##}s";
                    GUI.Label(new Rect(x + 2f, rect.y, 60f, rect.height), s, labelStyle);
                }
            }

            float endX = TimeToX(duration, rect.x, pps, scrollX);
            if (endX >= rect.x && endX <= rect.xMax)
                EditorGUI.DrawRect(new Rect(endX - 1f, rect.y, 1f, rect.height), k_GridColor);
        }

        // Pick a "nice" tick step so ~8 majors fit the visible area. For
        // frames, prefer integer steps (1,2,5,10,...). For seconds, the
        // historical sub-second candidates remain.
        static float ChooseGridStep(float visibleUnits, bool framesMode)
        {
            float raw = visibleUnits / 8f;
            if (framesMode)
            {
                float[] frameCandidates = { 1f, 2f, 5f, 10f, 15f, 30f, 60f, 120f, 300f, 600f, 1800f, 3600f };
                foreach (var c in frameCandidates) if (raw <= c) return c;
                return 3600f;
            }
            float[] secCandidates = { 0.01f, 0.02f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 30f, 60f };
            foreach (var c in secCandidates) if (raw <= c) return c;
            return 60f;
        }

        void DrawEventsRow(Rect rect, VMGAnimationClip clip, float duration, float pps, float scrollX)
        {
            EditorGUI.DrawRect(rect, k_AltRowColor);
            if (clip.events == null) return;
            int selected = m_Selection.EventIndex;
            for (int i = 0; i < clip.events.Count; i++)
            {
                var ev = clip.events[i];
                if (ev == null) continue;
                float t = Mathf.Clamp(ev.time, 0f, duration);
                float x = TimeToX(t, rect.x, pps, scrollX);
                if (x < rect.x - 8f || x > rect.xMax + 8f) continue;
                var p0 = new Vector2(x, rect.y + 2f);
                var p1 = new Vector2(x - 5f, rect.yMax - 2f);
                var p2 = new Vector2(x + 5f, rect.yMax - 2f);
                bool isSel = i == selected;
                if (isSel)
                {
                    Handles.color = k_EventSelectedOutline;
                    var b0 = new Vector2(x, rect.y);
                    var b1 = new Vector2(x - 7f, rect.yMax);
                    var b2 = new Vector2(x + 7f, rect.yMax);
                    Handles.DrawAAConvexPolygon(b0, b1, b2);
                }
                Handles.color = isSel ? k_EventSelectedColor : k_EventColor;
                Handles.DrawAAConvexPolygon(p0, p1, p2);
            }
        }

        // Background tint for group header rows. Slightly darker than the
        // banded track row so the visual hierarchy reads at a glance.
        static readonly Color k_GroupHeaderColor = new Color(0f, 0f, 0f, 0.18f);
        // User-group header sits one level above the auto subgroup; slightly
        // darker + cooler tint so it reads as the outer container.
        static readonly Color k_UserGroupHeaderColor = new Color(0.18f, 0.22f, 0.30f, 0.55f);
        static readonly Color k_CaretColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        // Half-size diamonds drawn on collapsed group headers as a summary
        // of hidden child keys. Visual only — they don't take input.
        const float k_SummaryKeyHalfWidth = 3f;
        const float k_SummaryKeyHalfHeight = 4f;
        // Per-depth indent for nested headers / tracks. Caret+label area
        // for a row at depth d shifts right by d * k_GroupIndent.
        const float k_GroupIndent = 12f;
        static readonly Color k_SummaryKeyColor = new Color(0.86f, 0.86f, 0.86f, 0.55f);

        void DrawTracks(Rect outer, float top, float tlLeft, float tlWidth, VMGAnimationClip clip, float duration, float pps, float scrollX)
        {
            int selTrack = m_Selection.trackIndex;
            int rowCount = m_Rows.Count;
            for (int i = 0; i < Mathf.Max(rowCount, 1); i++)
            {
                var rowRect = new Rect(outer.x, top + i * k_RowHeight, outer.width - 2f, k_RowHeight);
                bool hovered = i == m_HoverRow;

                if (i >= rowCount)
                {
                    // Empty-clip placeholder row.
                    GUI.Label(new Rect(rowRect.x + 4f, rowRect.y, k_LabelWidth - 6f, rowRect.height), "<empty>", EditorStyles.miniLabel);
                    continue;
                }

                var row = m_Rows[i];
                if (row.kind == RowKind.UserGroup)
                {
                    DrawUserGroupHeaderRow(rowRect, tlLeft, tlWidth, clip, row, duration, pps, scrollX, hovered);
                }
                else if (row.kind == RowKind.AutoGroup)
                {
                    DrawGroupHeaderRow(rowRect, tlLeft, tlWidth, clip, row, duration, pps, scrollX, hovered);
                }
                else
                {
                    DrawChildTrackRow(rowRect, tlLeft, tlWidth, clip, row.trackIdx, i, selTrack, duration, pps, scrollX, hovered, row.depth);
                }
            }
        }

        void DrawGroupHeaderRow(Rect rowRect, float tlLeft, float tlWidth, VMGAnimationClip clip, Row row, float duration, float pps, float scrollX, bool hovered)
        {
            EditorGUI.DrawRect(rowRect, k_GroupHeaderColor);
            if (hovered) EditorGUI.DrawRect(rowRect, k_RowHoverColor);

            float indent = row.depth * k_GroupIndent;
            // Caret + bold label area.
            bool collapsed = IsGroupCollapsed(clip, row.groupKey);
            var caretRect = new Rect(rowRect.x + 2f + indent, rowRect.y, k_GroupCaretWidth, rowRect.height);
            var caret = new GUIContent(collapsed ? "▶" : "▼"); // ▶ / ▼
            var caretStyle = EditorStyles.miniLabel;
            var prevColor = GUI.color;
            GUI.color = k_CaretColor;
            GUI.Label(caretRect, caret, caretStyle);
            GUI.color = prevColor;

            var labelRect = new Rect(rowRect.x + k_GroupCaretWidth + 4f + indent, rowRect.y, k_LabelWidth - k_GroupCaretWidth - 6f - indent, rowRect.height);
            GUI.Label(labelRect, row.headerLabel, EditorStyles.boldLabel);

            // Collapsed: draw summary diamonds for hidden child keys.
            if (collapsed)
            {
                var keysRect = new Rect(tlLeft, rowRect.y, tlWidth, rowRect.height);
                DrawGroupSummaryKeys(keysRect, clip, row, duration, pps, scrollX);
            }
        }

        void DrawUserGroupHeaderRow(Rect rowRect, float tlLeft, float tlWidth, VMGAnimationClip clip, Row row, float duration, float pps, float scrollX, bool hovered)
        {
            EditorGUI.DrawRect(rowRect, k_UserGroupHeaderColor);
            if (hovered) EditorGUI.DrawRect(rowRect, k_RowHoverColor);

            bool collapsed = IsGroupCollapsed(clip, row.groupKey);
            var caretRect = new Rect(rowRect.x + 2f, rowRect.y, k_GroupCaretWidth, rowRect.height);
            var caret = new GUIContent(collapsed ? "▶" : "▼");
            var caretStyle = EditorStyles.miniLabel;
            var prevColor = GUI.color;
            GUI.color = k_CaretColor;
            GUI.Label(caretRect, caret, caretStyle);
            GUI.color = prevColor;

            var labelRect = new Rect(rowRect.x + k_GroupCaretWidth + 4f, rowRect.y, k_LabelWidth - k_GroupCaretWidth - 6f, rowRect.height);
            GUI.Label(labelRect, row.headerLabel, EditorStyles.boldLabel);

            if (collapsed)
            {
                var keysRect = new Rect(tlLeft, rowRect.y, tlWidth, rowRect.height);
                DrawUserGroupSummaryKeys(keysRect, clip, row.userGroupId, duration, pps, scrollX);
            }
        }

        void DrawChildTrackRow(Rect rowRect, float tlLeft, float tlWidth, VMGAnimationClip clip, int trackIdx, int rowIdx, int selTrack, float duration, float pps, float scrollX, bool hovered, int depth)
        {
            // Banded rows (Unity Animation pattern: alternating subtle tint).
            if (rowIdx % 2 == 1) EditorGUI.DrawRect(rowRect, k_AltRowColor);
            if (hovered && trackIdx != selTrack) EditorGUI.DrawRect(rowRect, k_RowHoverColor);
            if (trackIdx == selTrack)
            {
                EditorGUI.DrawRect(rowRect, k_TrackSelectedColor);
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 2f, rowRect.height), k_TrackSelectedBorder);
            }

            // Indent child rows under their auto-subgroup header (depth - 1
            // would land at the auto header; depth places the label one
            // step further in).
            float indent = depth * k_GroupIndent;
            var labelRect = new Rect(rowRect.x + k_GroupCaretWidth + 4f + indent, rowRect.y, k_LabelWidth - k_GroupCaretWidth - 6f - indent, rowRect.height);
            if (clip.tracks != null && trackIdx >= 0 && trackIdx < clip.tracks.Count && clip.tracks[trackIdx] != null)
            {
                var track = clip.tracks[trackIdx];
                GUI.Label(labelRect, BuildTrackTitle(track, trackIdx), EditorStyles.miniLabel);
                var keysRect = new Rect(tlLeft, rowRect.y, tlWidth, rowRect.height);
                DrawTrackKeys(keysRect, track, duration, trackIdx, pps, scrollX);
            }
            else
            {
                GUI.Label(labelRect, "<empty>", EditorStyles.miniLabel);
            }
        }

        void DrawGroupSummaryKeys(Rect rect, VMGAnimationClip clip, Row headerRow, float duration, float pps, float scrollX)
        {
            if (clip.tracks == null) return;
            float cy = rect.y + rect.height * 0.5f;
            for (int ti = 0; ti < clip.tracks.Count; ti++)
            {
                var track = clip.tracks[ti];
                if (track == null) continue;
                if (track.groupId != headerRow.userGroupId) continue;
                if (BuildGroupKey(track.binding, headerRow.userGroupId) != headerRow.groupKey) continue;
                if (track.keys == null) continue;
                for (int ki = 0; ki < track.keys.Count; ki++)
                {
                    float t = Mathf.Clamp(track.keys[ki].time, 0f, duration);
                    float cx = TimeToX(t, rect.x, pps, scrollX);
                    if (cx < rect.x - k_SummaryKeyHalfWidth - 2f || cx > rect.xMax + k_SummaryKeyHalfWidth + 2f) continue;
                    DrawDiamond(cx, cy, k_SummaryKeyHalfWidth, k_SummaryKeyHalfHeight, k_SummaryKeyColor);
                }
            }
        }

        void DrawUserGroupSummaryKeys(Rect rect, VMGAnimationClip clip, int userGroupId, float duration, float pps, float scrollX)
        {
            if (clip.tracks == null) return;
            float cy = rect.y + rect.height * 0.5f;
            for (int ti = 0; ti < clip.tracks.Count; ti++)
            {
                var track = clip.tracks[ti];
                if (track == null) continue;
                if (track.groupId != userGroupId) continue;
                if (track.keys == null) continue;
                for (int ki = 0; ki < track.keys.Count; ki++)
                {
                    float t = Mathf.Clamp(track.keys[ki].time, 0f, duration);
                    float cx = TimeToX(t, rect.x, pps, scrollX);
                    if (cx < rect.x - k_SummaryKeyHalfWidth - 2f || cx > rect.xMax + k_SummaryKeyHalfWidth + 2f) continue;
                    DrawDiamond(cx, cy, k_SummaryKeyHalfWidth, k_SummaryKeyHalfHeight, k_SummaryKeyColor);
                }
            }
        }

        static string BuildTrackTitle(VMGAnimationTrack track, int index)
        {
            var b = track.binding;
            string field = string.IsNullOrEmpty(b.fieldPath) ? "?" : b.fieldPath;
            // Group header already shows the GO path + component, so the
            // child row only needs the field path. Index keeps the
            // "{n}: …" affordance for selection feedback.
            return $"{index}: {field}";
        }

        // Auto subgroup key = "user{id}|gameObjectPath|componentTypeName".
        // Including the user group id makes the same (GO, component) under
        // two different user groups two distinct auto subgroups, so their
        // collapse states stay independent.
        static string BuildGroupKey(VMGChannelBinding b, int userGroupId) =>
            "u" + userGroupId + "|" + (b.gameObjectPath ?? string.Empty) + "|" + (b.componentTypeName ?? string.Empty);

        static string BuildUserGroupKey(int userGroupId) => "ug" + userGroupId;

        static string BuildGroupLabel(VMGChannelBinding b)
        {
            string path = string.IsNullOrEmpty(b.gameObjectPath) ? "<self>" : b.gameObjectPath;
            string comp = ShortComponentName(b.componentTypeName);
            return string.IsNullOrEmpty(comp) ? path : $"{path}  ·  {comp}";
        }

        static string ShortComponentName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return string.Empty;
            int comma = typeName.IndexOf(',');
            string baseName = comma >= 0 ? typeName.Substring(0, comma) : typeName;
            int dot = baseName.LastIndexOf('.');
            return dot >= 0 ? baseName.Substring(dot + 1) : baseName;
        }

        HashSet<string> GetCollapsedSet(VMGAnimationClip clip)
        {
            int id = clip.GetInstanceID();
            if (!m_CollapsedByClip.TryGetValue(id, out var set))
            {
                set = new HashSet<string>();
                m_CollapsedByClip[id] = set;
            }
            return set;
        }

        void RebuildRows(VMGAnimationClip clip)
        {
            m_LastClip = clip;
            m_Rows.Clear();
            if (clip == null || clip.tracks == null) return;
            var collapsed = GetCollapsedSet(clip);

            // Phase 1: ungrouped tracks (groupId == 0) keep the existing
            // top-level auto-grouping. Track order is preserved so trackIdx
            // semantics stay intact for selection, drag, undo.
            EmitTracksForUserGroup(clip, 0, /*userHeaderRow*/ -1, /*depthOffset*/ 0, collapsed);

            // Phase 2: each user group becomes a header row, followed by
            // (auto-subgroup header, child tracks) for its members. Empty
            // user groups still get a header row (user may have created the
            // group first, intending to assign tracks later).
            if (clip.userGroups != null)
            {
                for (int gi = 0; gi < clip.userGroups.Count; gi++)
                {
                    var g = clip.userGroups[gi];
                    if (g == null) continue;
                    int userHeaderRow = m_Rows.Count;
                    string ugKey = BuildUserGroupKey(g.id);
                    m_Rows.Add(new Row
                    {
                        kind = RowKind.UserGroup,
                        depth = 0,
                        groupKey = ugKey,
                        headerLabel = string.IsNullOrEmpty(g.name) ? "(unnamed group)" : g.name,
                        trackIdx = -1,
                        userGroupId = g.id,
                        autoGroupHeaderRow = -1,
                        userGroupHeaderRow = userHeaderRow,
                    });
                    if (collapsed.Contains(ugKey)) continue;
                    EmitTracksForUserGroup(clip, g.id, userHeaderRow, /*depthOffset*/ 1, collapsed);
                }
            }
        }

        // Emit auto-subgroup headers + track rows for every track whose
        // groupId matches `userGroupId`. Used both for ungrouped tracks
        // (userGroupId=0, depthOffset=0) and for tracks inside a user group
        // (depthOffset=1).
        void EmitTracksForUserGroup(VMGAnimationClip clip, int userGroupId, int userHeaderRow, int depthOffset, HashSet<string> collapsed)
        {
            string activeKey = null;
            int activeAutoHeaderRow = -1;
            for (int ti = 0; ti < clip.tracks.Count; ti++)
            {
                var track = clip.tracks[ti];
                if (track == null) continue;
                if (track.groupId != userGroupId) continue;

                string key = BuildGroupKey(track.binding, userGroupId);
                if (key != activeKey)
                {
                    activeKey = key;
                    activeAutoHeaderRow = m_Rows.Count;
                    m_Rows.Add(new Row
                    {
                        kind = RowKind.AutoGroup,
                        depth = depthOffset,
                        groupKey = key,
                        headerLabel = BuildGroupLabel(track.binding),
                        trackIdx = -1,
                        userGroupId = userGroupId,
                        autoGroupHeaderRow = activeAutoHeaderRow,
                        userGroupHeaderRow = userHeaderRow,
                    });
                }
                if (!collapsed.Contains(key))
                {
                    m_Rows.Add(new Row
                    {
                        kind = RowKind.Track,
                        depth = depthOffset + 1,
                        groupKey = key,
                        headerLabel = null,
                        trackIdx = ti,
                        userGroupId = userGroupId,
                        autoGroupHeaderRow = activeAutoHeaderRow,
                        userGroupHeaderRow = userHeaderRow,
                    });
                }
            }
        }

        bool IsGroupCollapsed(VMGAnimationClip clip, string groupKey) =>
            GetCollapsedSet(clip).Contains(groupKey);

        void ToggleGroupCollapsed(VMGAnimationClip clip, string groupKey)
        {
            var set = GetCollapsedSet(clip);
            if (!set.Add(groupKey)) set.Remove(groupKey);
            // Selection of a now-hidden child should stay valid — clearing
            // would be surprising. Hidden selection just becomes invisible
            // until the group is re-expanded.
        }

        void DrawTrackKeys(Rect rect, VMGAnimationTrack track, float duration, int trackIndex, float pps, float scrollX)
        {
            if (track.keys == null) return;
            bool recording = m_RecordingThisFrame;
            float cy = rect.y + rect.height * 0.5f;
            for (int i = 0; i < track.keys.Count; i++)
            {
                var k = track.keys[i];
                float t = Mathf.Clamp(k.time, 0f, duration);
                float cx = TimeToX(t, rect.x, pps, scrollX);
                if (cx < rect.x - k_KeyHalfWidth - 4f || cx > rect.xMax + k_KeyHalfWidth + 4f) continue;
                bool selected = m_Selection.Contains(trackIndex, i);
                bool hovered = trackIndex == m_HoverTrack && i == m_HoverKey;
                if (hovered)
                {
                    DrawDiamond(cx, cy, k_KeyHalfWidth + k_KeyHoverHaloPad, k_KeyHalfHeight + k_KeyHoverHaloPad, k_KeyHoverHalo);
                }
                Color fill = selected ? k_KeySelectedColor : (recording ? k_KeyRecordingColor : k_KeyColor);
                // 1px outline via a slightly larger diamond underneath.
                DrawDiamond(cx, cy, k_KeyHalfWidth + 1f, k_KeyHalfHeight + 1f, k_KeyOutline);
                DrawDiamond(cx, cy, k_KeyHalfWidth, k_KeyHalfHeight, fill);
            }
        }

        static void DrawDiamond(float cx, float cy, float hw, float hh, Color color)
        {
            var prev = Handles.color;
            Handles.color = color;
            Handles.DrawAAConvexPolygon(
                new Vector3(cx, cy - hh, 0f),
                new Vector3(cx + hw, cy, 0f),
                new Vector3(cx, cy + hh, 0f),
                new Vector3(cx - hw, cy, 0f));
            Handles.color = prev;
        }

        // Dim everything past `duration`. Drawn after tracks/grid/ruler so it
        // sits visually on top of those but below playhead + rubber-band.
        static readonly Color k_HeadroomDimColor = new Color(0f, 0f, 0f, 0.22f);

        void DrawHeadroomDim(Rect outer, float tlLeft, float viewWidth, float duration, float pps, float scrollX, float bodyHeight)
        {
            float endX = TimeToX(duration, tlLeft, pps, scrollX);
            // Clip the overlay to the visible timeline strip.
            float left = Mathf.Max(endX, tlLeft);
            float right = tlLeft + viewWidth;
            if (right <= left) return;
            EditorGUI.DrawRect(new Rect(left, outer.y, right - left, bodyHeight), k_HeadroomDimColor);
        }

        static void DrawPlayhead(Rect tlRect, float progress, float duration, float pps, float scrollX)
        {
            float t = Mathf.Clamp01(progress) * duration;
            float x = TimeToX(t, tlRect.x, pps, scrollX);
            if (x < tlRect.x || x > tlRect.xMax) return;
            EditorGUI.DrawRect(new Rect(x, tlRect.y, 1f, tlRect.height), k_PlayheadColor);
        }

        // ---------- rubber band ----------

        static readonly Color k_RubberFill = new Color(0.4f, 0.6f, 1f, 0.18f);
        static readonly Color k_RubberBorder = new Color(0.5f, 0.7f, 1f, 0.8f);

        Rect GetRubberRect()
        {
            float x = Mathf.Min(m_RubberStart.x, m_RubberCurrent.x);
            float y = Mathf.Min(m_RubberStart.y, m_RubberCurrent.y);
            float w = Mathf.Abs(m_RubberCurrent.x - m_RubberStart.x);
            float h = Mathf.Abs(m_RubberCurrent.y - m_RubberStart.y);
            return new Rect(x, y, w, h);
        }

        void DrawRubberBand()
        {
            if (!m_RubberActive) return;
            var r = GetRubberRect();
            EditorGUI.DrawRect(r, k_RubberFill);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), k_RubberBorder);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), k_RubberBorder);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), k_RubberBorder);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), k_RubberBorder);
        }

        // --- Row drag (auto-subgroup / user-group reorder) ---

        static readonly Color k_RowDropIndicatorColor = new Color(1f, 0.85f, 0.2f, 1f);

        // Computes which slot the pointer is over while a row drag is
        // active. Returns false when the drop would be a no-op. The
        // resulting slot is interpreted by ApplyRowDrag.
        struct RowDropTarget
        {
            // For AutoGroup source: index into clip.userGroups (-1 for
            // ungrouped) the moved subgroup will land under, plus the
            // row index (relative to m_Rows) just *before* the drop.
            // For UserGroup source: position in clip.userGroups list.
            public bool valid;
            public int destUserGroupId;       // 0 = ungrouped (top-level)
            public int insertAtRow;           // -1 = end of section
            public float indicatorY;
        }

        RowDropTarget ComputeRowDropTarget(VMGAnimationClip clip, Vector2 mousePos, float trackTop)
        {
            var result = new RowDropTarget { valid = false };
            if (m_RowDragSourceRow < 0 || m_Rows.Count == 0) return result;

            // Determine which row gap the pointer is closest to. Each row
            // contributes a top-half "before" slot and a bottom-half
            // "after" slot; we resolve to a single insertion index in
            // m_Rows (between rows).
            int rowCount = m_Rows.Count;
            float relY = mousePos.y - trackTop;
            int slotIdx = Mathf.Clamp(Mathf.RoundToInt(relY / k_RowHeight), 0, rowCount);
            float indicatorY = trackTop + slotIdx * k_RowHeight;

            if (m_RowDragKind == RowKind.UserGroup)
            {
                // User-group reorder: snap the indicator to gaps *between*
                // user-group headers (or at the very start / end of the
                // user-group section). Ungrouped section is row 0..N where
                // the first user-group header begins. Below the last user
                // group is the bottom.
                int firstUserHeader = -1;
                for (int i = 0; i < rowCount; i++)
                {
                    if (m_Rows[i].kind == RowKind.UserGroup) { firstUserHeader = i; break; }
                }
                if (firstUserHeader < 0)
                {
                    // No other user groups — only the dragged one. No-op.
                    return result;
                }
                // Pull the slot up if it's inside the ungrouped section.
                if (slotIdx < firstUserHeader) slotIdx = firstUserHeader;
                indicatorY = trackTop + slotIdx * k_RowHeight;

                // Find the target list position in clip.userGroups by
                // counting user-group headers at or after slotIdx.
                int destPos = 0;
                for (int i = 0; i < slotIdx && i < rowCount; i++)
                {
                    if (m_Rows[i].kind == RowKind.UserGroup) destPos++;
                }
                // No-op when dropping at the same position the source
                // already occupies. The source is at m_RowDragSourceUgId
                // in clip.userGroups.
                int srcPos = -1;
                for (int i = 0; i < clip.userGroups.Count; i++)
                {
                    if (clip.userGroups[i] != null && clip.userGroups[i].id == m_RowDragSourceUgId)
                    {
                        srcPos = i;
                        break;
                    }
                }
                if (srcPos < 0) return result;
                if (destPos == srcPos || destPos == srcPos + 1) return result;
                result.valid = true;
                result.destUserGroupId = -1;       // unused for UG reorder
                result.insertAtRow = slotIdx;
                result.indicatorY = indicatorY;
                return result;
            }

            // AutoGroup source: figure out which user group (or ungrouped)
            // the slot falls into.
            int targetUserGroupId = 0;
            for (int i = 0; i < slotIdx && i < rowCount; i++)
            {
                var r = m_Rows[i];
                if (r.kind == RowKind.UserGroup) targetUserGroupId = r.userGroupId;
            }
            // If the slot sits between/below user-group headers, we still
            // assign to the last-seen userGroupId. Dropping right *on* a
            // user-group header row's own line lands inside that group at
            // its top.
            // Reject the no-op: dropping the subgroup at a slot adjacent
            // to its own current position with the same parent group.
            int srcRowIdx = m_RowDragSourceRow;
            int srcMemberCount = 0;
            for (int i = srcRowIdx; i < rowCount; i++)
            {
                if (i == srcRowIdx) { srcMemberCount++; continue; }
                if (m_Rows[i].kind == RowKind.Track && m_Rows[i].autoGroupHeaderRow == srcRowIdx) srcMemberCount++;
                else break;
            }
            int srcEnd = srcRowIdx + srcMemberCount; // exclusive
            if (targetUserGroupId == m_RowDragSourceUserGroupId &&
                (slotIdx == srcRowIdx || slotIdx == srcEnd))
            {
                return result;
            }

            result.valid = true;
            result.destUserGroupId = targetUserGroupId;
            result.insertAtRow = slotIdx;
            result.indicatorY = indicatorY;
            return result;
        }

        void DrawRowDropIndicator(Rect outer, float trackTop)
        {
            if (!m_RowDragActive) return;
            var clip = ResolveActiveClip();
            if (clip == null) return;
            var target = ComputeRowDropTarget(clip, m_RowDragCurrent, trackTop);
            if (!target.valid) return;
            // Clamp Y to the visible track band so the line stays inside
            // the scrolled viewport.
            float y = Mathf.Clamp(target.indicatorY, m_TrackAreaTop, m_TrackAreaBottom - 2f);
            var lineRect = new Rect(outer.x + 2f, y - 1f, outer.width - 4f, 2f);
            EditorGUI.DrawRect(lineRect, k_RowDropIndicatorColor);
        }

        // The view doesn't store the clip directly, but every code path
        // here is called from Draw(animator) which already has it. We
        // stash the most recent clip when RebuildRows runs.
        VMGAnimationClip m_LastClip;
        VMGAnimationClip ResolveActiveClip() => m_LastClip;

        void CommitRowDrag(VMGAnimationClip clip, Vector2 mousePos, float trackTop)
        {
            var target = ComputeRowDropTarget(clip, mousePos, trackTop);
            if (!target.valid) return;
            if (m_RowDragKind == RowKind.UserGroup)
            {
                ApplyUserGroupReorder(clip, target.insertAtRow);
            }
            else if (m_RowDragKind == RowKind.AutoGroup)
            {
                ApplyAutoGroupMove(clip, target.destUserGroupId, target.insertAtRow);
            }
        }

        void ApplyUserGroupReorder(VMGAnimationClip clip, int slotIdx)
        {
            if (clip == null || clip.userGroups == null) return;
            int srcPos = -1;
            for (int i = 0; i < clip.userGroups.Count; i++)
            {
                if (clip.userGroups[i] != null && clip.userGroups[i].id == m_RowDragSourceUgId)
                {
                    srcPos = i;
                    break;
                }
            }
            if (srcPos < 0) return;
            int destPos = 0;
            for (int i = 0; i < slotIdx && i < m_Rows.Count; i++)
            {
                if (m_Rows[i].kind == RowKind.UserGroup) destPos++;
            }
            if (destPos == srcPos || destPos == srcPos + 1) return;
            Undo.RecordObject(clip, "Reorder VMG Group");
            var g = clip.userGroups[srcPos];
            clip.userGroups.RemoveAt(srcPos);
            int insertIdx = destPos > srcPos ? destPos - 1 : destPos;
            insertIdx = Mathf.Clamp(insertIdx, 0, clip.userGroups.Count);
            clip.userGroups.Insert(insertIdx, g);
            VMGTimelineSelection.MarkDirty(clip);
        }

        // Move all tracks belonging to the source auto subgroup to the
        // destination user group's section, contiguous block, at the
        // computed slot. Source tracks are identified by groupId + the
        // GroupKey captured at MouseDown so a single (GO, comp) bundle
        // moves as one.
        void ApplyAutoGroupMove(VMGAnimationClip clip, int destUserGroupId, int slotIdx)
        {
            if (clip == null || clip.tracks == null) return;

            // 1. Collect source track indices (in current list order).
            var srcIndices = new List<int>();
            for (int ti = 0; ti < clip.tracks.Count; ti++)
            {
                var t = clip.tracks[ti];
                if (t == null) continue;
                if (t.groupId != m_RowDragSourceUserGroupId) continue;
                if (BuildGroupKey(t.binding, m_RowDragSourceUserGroupId) != m_RowDragSourceGroupKey) continue;
                srcIndices.Add(ti);
            }
            if (srcIndices.Count == 0) return;

            // 2. Compute the *target track index* the source block should
            // land at, before removal. Walk the destination section's
            // existing tracks in order and stop at the slot the user
            // pointed at.
            //
            // For the destination user group, list every track with that
            // groupId. The slot index relative to m_Rows tells us which
            // visible track-or-header row the drop sits after; we
            // translate that back to a clip.tracks index.
            int destInsertTrackIdx;
            if (slotIdx >= m_Rows.Count)
            {
                destInsertTrackIdx = clip.tracks.Count;
            }
            else
            {
                // Find the nearest track row at or after slotIdx that's
                // in the destination user group; insert before it.
                int idx = -1;
                for (int i = slotIdx; i < m_Rows.Count; i++)
                {
                    var r = m_Rows[i];
                    if (r.kind == RowKind.Track && clip.tracks[r.trackIdx] != null &&
                        clip.tracks[r.trackIdx].groupId == destUserGroupId)
                    {
                        idx = r.trackIdx;
                        break;
                    }
                }
                if (idx < 0)
                {
                    // No track row after the slot in the destination
                    // section — append at the end of that section.
                    int lastInDest = -1;
                    for (int ti = 0; ti < clip.tracks.Count; ti++)
                    {
                        if (clip.tracks[ti] != null && clip.tracks[ti].groupId == destUserGroupId)
                            lastInDest = ti;
                    }
                    destInsertTrackIdx = lastInDest + 1;
                }
                else
                {
                    destInsertTrackIdx = idx;
                }
            }

            // 3. Snapshot source tracks, re-stamp their groupId, splice.
            Undo.RecordObject(clip, "Reorder VMG Tracks");
            var moved = new List<VMGAnimationTrack>(srcIndices.Count);
            foreach (var i in srcIndices) moved.Add(clip.tracks[i]);
            foreach (var t in moved) t.groupId = destUserGroupId;

            // Remove from highest to lowest so earlier indices stay valid.
            for (int i = srcIndices.Count - 1; i >= 0; i--)
            {
                int rem = srcIndices[i];
                clip.tracks.RemoveAt(rem);
                if (rem < destInsertTrackIdx) destInsertTrackIdx--;
            }
            destInsertTrackIdx = Mathf.Clamp(destInsertTrackIdx, 0, clip.tracks.Count);
            for (int i = 0; i < moved.Count; i++)
            {
                clip.tracks.Insert(destInsertTrackIdx + i, moved[i]);
            }
            VMGTimelineSelection.MarkDirty(clip);
        }

        void UpdateRubberSelection(VMGAnimationClip clip, float tlLeft, float pps, float scrollX, float duration, float trackTop)
        {
            var box = GetRubberRect();
            var picked = new List<VMGTimelineSelection.Item>();
            if (m_RubberShift && m_RubberInitialSelection != null) picked.AddRange(m_RubberInitialSelection);

            // Walk flattened rows so collapsed groups (no visible rows) are
            // naturally excluded from lasso selection. Group header rows are
            // skipped — only real tracks contribute keys.
            for (int ri = 0; ri < m_Rows.Count; ri++)
            {
                var row = m_Rows[ri];
                if (row.kind != RowKind.Track) continue;
                if (clip.tracks == null || row.trackIdx < 0 || row.trackIdx >= clip.tracks.Count) continue;
                var track = clip.tracks[row.trackIdx];
                if (track == null || track.keys == null) continue;
                float rowTop = trackTop + ri * k_RowHeight;
                float cy = rowTop + k_RowHeight * 0.5f;
                if (cy < box.y || cy > box.yMax) continue;
                for (int ki = 0; ki < track.keys.Count; ki++)
                {
                    float t = Mathf.Clamp(track.keys[ki].time, 0f, duration);
                    float cx = TimeToX(t, tlLeft, pps, scrollX);
                    if (cx < box.x || cx > box.xMax) continue;
                    var item = new VMGTimelineSelection.Item { track = row.trackIdx, key = ki };
                    if (!picked.Contains(item)) picked.Add(item);
                }
            }
            m_Selection.ReplaceWith(picked);
        }

        // ---------- zoom + scrollbar ----------

        void HandleZoom(Rect rulerRect, Rect hotRect, float currentPps, float fitPps, float duration)
        {
            var e = Event.current;
            if (e.type != EventType.ScrollWheel) return;
            if (!hotRect.Contains(e.mousePosition)) return;
            // Plain wheel scrolls tracks; modifier wheel zooms. Matches
            // Unity Animation conventions and lets users navigate long
            // track lists without accidentally changing zoom.
            bool overRuler = rulerRect.Contains(e.mousePosition);
            bool zoomModifier = e.alt || e.control || e.command;
            if (!overRuler && !zoomModifier) return;

            float mouseTime = XToTime(e.mousePosition.x, rulerRect.x, currentPps, m_ScrollX);
            float zoom = e.delta.y < 0f ? k_ZoomStep : 1f / k_ZoomStep;
            float newPps = Mathf.Clamp(currentPps * zoom, Mathf.Max(k_MinPps, fitPps * 0.5f), k_MaxPps);
            if (Mathf.Approximately(newPps, currentPps)) { e.Use(); return; }
            m_PixelsPerSecond = newPps;
            // Keep the time under the cursor pinned to the cursor pixel.
            m_ScrollX = mouseTime * newPps - (e.mousePosition.x - rulerRect.x);
            float contentWidth = duration * newPps;
            float maxScroll = Mathf.Max(0f, contentWidth - rulerRect.width);
            m_ScrollX = Mathf.Clamp(m_ScrollX, 0f, maxScroll);
            e.Use();
            GUI.changed = true;
        }

        void DrawHorizontalScrollbar(Rect rect, float viewWidth, float contentWidth)
        {
            if (contentWidth <= viewWidth + 0.5f)
            {
                // Still draw a "Fit" affordance even when no scrollbar is needed.
                if (m_PixelsPerSecond > 0f)
                {
                    if (GUI.Button(new Rect(rect.x, rect.y, 50f, rect.height), "Fit", EditorStyles.miniButton))
                    {
                        m_PixelsPerSecond = 0f;
                        m_ScrollX = 0f;
                    }
                }
                return;
            }
            float fitW = 50f;
            var fitRect = new Rect(rect.x, rect.y, fitW, rect.height);
            if (GUI.Button(fitRect, "Fit", EditorStyles.miniButton))
            {
                m_PixelsPerSecond = 0f;
                m_ScrollX = 0f;
                return;
            }
            var barRect = new Rect(rect.x + fitW + 4f, rect.y, rect.width - fitW - 4f, rect.height);
            m_ScrollX = GUI.HorizontalScrollbar(barRect, m_ScrollX, viewWidth, 0f, contentWidth);
        }

        void DrawVerticalScrollbar(Rect rect, float viewHeight, float contentHeight)
        {
            if (contentHeight <= viewHeight + 0.5f) return;
            m_ScrollY = GUI.VerticalScrollbar(rect, m_ScrollY, viewHeight, 0f, contentHeight);
        }

        // Mouse-wheel scroll over the track area. ScrollWheel events carry
        // a delta.y in IMGUI's "lines" convention (~3 px per notch), so we
        // scale to a comfortable per-tick step.
        void HandleVerticalWheel(Rect hotRect, float maxScrollY)
        {
            var e = Event.current;
            if (e.type != EventType.ScrollWheel) return;
            if (!hotRect.Contains(e.mousePosition)) return;
            if (maxScrollY <= 0f) return;
            m_ScrollY = Mathf.Clamp(m_ScrollY + e.delta.y * 10f, 0f, maxScrollY);
            e.Use();
            GUI.changed = true;
        }

        // ---------- input ----------

        int m_DraggingEvent = -1;

        // `windowEnd` is the visible-window upper bound, NOT the clip's
        // duration — it may exceed duration when zoomed-out headroom is
        // present. All drag/scrub/add-key clamps run against windowEnd so
        // keys can be extended into the headroom; duration is then
        // recalculated on MouseUp via RecalculateDuration().
        void HandleInput(Rect outer, float tlLeft, float tlWidth, float trackTop, VMGAnimationClip clip, float windowEnd, VMGAnimator animator, Rect scrubRect, Rect eventRowRect, float pps, float scrollX)
        {
            // Inside this method `duration` historically meant "drag/scrub
            // clamp ceiling" — that's now the windowEnd. ScrubTo and Add Key
            // need the real clip.duration; they reach for it via the clip
            // reference directly.
            float duration = windowEnd;
            var e = Event.current;

            switch (e.type)
            {
                case EventType.KeyDown:
                {
                    if (EditorGUIUtility.editingTextField) break;
                    if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
                    {
                        if (m_Selection.HasSelection)
                        {
                            DeleteSelectedKeys(clip);
                            e.Use();
                            GUI.changed = true;
                        }
                        else if (m_Selection.HasEventSelection)
                        {
                            DeleteSelectedEvent(clip);
                            e.Use();
                            GUI.changed = true;
                        }
                    }
                    break;
                }
                case EventType.ValidateCommand:
                {
                    if (EditorGUIUtility.editingTextField) break;
                    if (e.commandName == "Copy" && m_Selection.HasSelection) { e.Use(); break; }
                    if (e.commandName == "Paste" && VMGKeyClipboard.HasContent) { e.Use(); break; }
                    if (e.commandName == "Delete" && m_Selection.HasSelection) { e.Use(); break; }
                    break;
                }
                case EventType.ExecuteCommand:
                {
                    if (EditorGUIUtility.editingTextField) break;
                    if (e.commandName == "Copy" && m_Selection.HasSelection)
                    {
                        VMGKeyClipboard.Copy(clip, m_Selection.Items);
                        e.Use();
                    }
                    else if (e.commandName == "Paste" && VMGKeyClipboard.HasContent)
                    {
                        PasteAtPlayhead(clip, animator);
                        e.Use();
                        GUI.changed = true;
                    }
                    else if (e.commandName == "Delete" && m_Selection.HasSelection)
                    {
                        DeleteSelectedKeys(clip);
                        e.Use();
                        GUI.changed = true;
                    }
                    break;
                }
                case EventType.MouseDown:
                {
                    if (!outer.Contains(e.mousePosition)) return;
                    if (e.button == 0 && scrubRect.Contains(e.mousePosition))
                    {
                        if (Playback != null && Playback.IsPlaying) Playback.Pause();
                        Playback?.EnsureBaselineCaptured();
                        ScrubTo(animator, e.mousePosition.x, tlLeft, pps, scrollX, duration, clip, e.shift);
                        m_Scrubbing = true;
                        e.Use();
                        GUI.changed = true;
                        return;
                    }
                    if (eventRowRect.Contains(e.mousePosition))
                    {
                        if (e.button == 0)
                        {
                            if (TryHitEvent(clip, e.mousePosition, tlLeft, pps, scrollX, duration, out int evIdx))
                            {
                                m_Selection.SelectEvent(evIdx);
                                m_DraggingEvent = evIdx;
                                Undo.IncrementCurrentGroup();
                                m_DragUndoGroup = Undo.GetCurrentGroup();
                                if (UnityEditor.Selection.activeGameObject != animator.gameObject)
                                    UnityEditor.Selection.activeGameObject = animator.gameObject;
                                e.Use();
                                GUI.changed = true;
                                return;
                            }
                            else
                            {
                                m_Selection.ClearEvent();
                                e.Use();
                                GUI.changed = true;
                                return;
                            }
                        }
                        else if (e.button == 1)
                        {
                            ShowEventRowContextMenu(clip, e.mousePosition, tlLeft, pps, scrollX, duration);
                            e.Use();
                            return;
                        }
                    }
                    if (TryFindRow(e.mousePosition, trackTop, out int rowIdx))
                    {
                        var row = m_Rows[rowIdx];
                        if (row.kind != RowKind.Track)
                        {
                            // LMB on header: arm a potential row drag.
                            // MouseUp without drag movement falls back to
                            // toggle-collapse. RMB opens the group context
                            // menu (Phase 3 actions).
                            if (e.button == 0)
                            {
                                m_RowDragArmed = true;
                                m_RowDragActive = false;
                                m_RowDragStart = e.mousePosition;
                                m_RowDragCurrent = e.mousePosition;
                                m_RowDragSourceRow = rowIdx;
                                m_RowDragKind = row.kind;
                                m_RowDragSourceUserGroupId = row.userGroupId;
                                m_RowDragSourceGroupKey = row.groupKey;
                                m_RowDragSourceUgId = row.kind == RowKind.UserGroup ? row.userGroupId : 0;
                                e.Use();
                            }
                            else if (e.button == 1)
                            {
                                ShowGroupHeaderContextMenu(clip, row);
                                e.Use();
                            }
                            break;
                        }
                        int trackIdx = row.trackIdx;
                        if (e.button == 0 && e.mousePosition.x < tlLeft)
                        {
                            m_Selection.Select(trackIdx, -1);
                            e.Use();
                            GUI.changed = true;
                        }
                        else if (e.button == 0 && e.mousePosition.x >= tlLeft)
                        {
                            if (TryHitKey(clip, trackIdx, e.mousePosition, tlLeft, pps, scrollX, duration, out int keyIdx))
                            {
                                if (e.shift)
                                {
                                    // Defer the add/remove toggle to MouseUp.
                                    // If a drag happens before then, treat
                                    // Shift as a snap modifier instead, and
                                    // include this key in the drag set if it
                                    // isn't already selected.
                                    m_PendingShiftToggle = true;
                                    m_PendingToggleTrack = trackIdx;
                                    m_PendingToggleKey = keyIdx;
                                    if (!m_Selection.Contains(trackIdx, keyIdx))
                                        m_Selection.AddOrRemove(trackIdx, keyIdx);
                                }
                                else if (!m_Selection.Contains(trackIdx, keyIdx))
                                {
                                    m_Selection.Select(trackIdx, keyIdx);
                                }
                                m_DraggingTrack = trackIdx;
                                m_DraggingKey = keyIdx;
                                m_KeyDragMoved = false;
                                Undo.IncrementCurrentGroup();
                                m_DragUndoGroup = Undo.GetCurrentGroup();
                                // If the clicked key is part of a multi-selection,
                                // snapshot all selected keys for delta drag.
                                if (m_Selection.IsMulti && m_Selection.Contains(trackIdx, keyIdx))
                                {
                                    m_DragSnapshots.Clear();
                                    foreach (var it in m_Selection.Items)
                                    {
                                        if (it.track < 0 || it.track >= clip.tracks.Count) continue;
                                        var tr = clip.tracks[it.track];
                                        if (tr == null || it.key < 0 || it.key >= tr.keys.Count) continue;
                                        m_DragSnapshots.Add(new DragSnapshot
                                        {
                                            track = it.track,
                                            key = it.key,
                                            originalTime = tr.keys[it.key].time,
                                        });
                                    }
                                    m_DragAnchorTime = clip.tracks[trackIdx].keys[keyIdx].time;
                                    m_MultiDragActive = true;
                                }
                                // Focus the inspector on this animator so the
                                // KeyEditor panel lands in a predictable spot.
                                // Locked inspectors are unaffected — Unity
                                // ignores Selection changes for those.
                                if (UnityEditor.Selection.activeGameObject != animator.gameObject)
                                    UnityEditor.Selection.activeGameObject = animator.gameObject;
                                e.Use();
                                GUI.changed = true;
                            }
                            else
                            {
                                // Empty track area: arm rubber-band. If user
                                // drags, we'll start a selection box; if not,
                                // MouseUp will treat it as a track click.
                                m_RubberArmed = true;
                                m_RubberStart = e.mousePosition;
                                m_RubberCurrent = e.mousePosition;
                                m_RubberShift = e.shift;
                                m_RubberInitialSelection = new List<VMGTimelineSelection.Item>(m_Selection.Items);
                                e.Use();
                            }
                        }
                        else if (e.button == 1)
                        {
                            ShowTrackContextMenu(clip, trackIdx, e.mousePosition, tlLeft, pps, scrollX, duration);
                            e.Use();
                        }
                    }
                    break;
                }
                case EventType.MouseDrag:
                {
                    if (m_RowDragArmed || m_RowDragActive)
                    {
                        if (!m_RowDragActive && Vector2.Distance(e.mousePosition, m_RowDragStart) >= k_RowDragStartThreshold)
                        {
                            m_RowDragActive = true;
                        }
                        if (m_RowDragActive)
                        {
                            m_RowDragCurrent = e.mousePosition;
                            e.Use();
                            GUI.changed = true;
                        }
                        return;
                    }
                    if (m_Scrubbing)
                    {
                        Playback?.EnsureBaselineCaptured();
                        ScrubTo(animator, e.mousePosition.x, tlLeft, pps, scrollX, duration, clip, e.shift);
                        e.Use();
                        return;
                    }
                    if (m_DraggingEvent >= 0 && m_DraggingEvent < clip.events.Count)
                    {
                        float et = XToTime(e.mousePosition.x, tlLeft, pps, scrollX);
                        et = SnapTime(et, clip, e.shift);
                        et = Mathf.Max(0f, et);
                        var ev = clip.events[m_DraggingEvent];
                        if (ev.time != et)
                        {
                            Undo.RecordObject(clip, "Move VMG Event");
                            ev.time = et;
                            clip.RecalculateDuration();
                            VMGTimelineSelection.MarkDirty(clip);
                        }
                        e.Use();
                        return;
                    }
                    if (m_RubberArmed || m_RubberActive)
                    {
                        if (!m_RubberActive && Vector2.Distance(e.mousePosition, m_RubberStart) >= k_RubberStartThreshold)
                            m_RubberActive = true;
                        if (m_RubberActive)
                        {
                            m_RubberCurrent = e.mousePosition;
                            UpdateRubberSelection(clip, tlLeft, pps, scrollX, duration, trackTop);
                            e.Use();
                            GUI.changed = true;
                        }
                        return;
                    }
                    if (m_DraggingTrack < 0 || m_DraggingKey < 0) return;
                    if (m_DraggingTrack >= clip.tracks.Count) return;
                    float mouseT = XToTime(e.mousePosition.x, tlLeft, pps, scrollX);
                    if (m_MultiDragActive)
                    {
                        float delta = mouseT - m_DragAnchorTime;
                        if (clip.snapDivisor > 0 && !e.shift)
                        {
                            float step = 1f / clip.snapDivisor;
                            delta = Mathf.Round(delta / step) * step;
                        }
                        if (delta == 0f) { e.Use(); break; }
                        Undo.RecordObject(clip, "Move VMG Keys");
                        foreach (var snap in m_DragSnapshots)
                        {
                            if (snap.track < 0 || snap.track >= clip.tracks.Count) continue;
                            var tr = clip.tracks[snap.track];
                            if (tr == null || snap.key < 0 || snap.key >= tr.keys.Count) continue;
                            var kk = tr.keys[snap.key];
                            kk.time = Mathf.Max(0f, snap.originalTime + delta);
                            tr.keys[snap.key] = kk;
                        }
                        clip.RecalculateDuration();
                        VMGTimelineSelection.MarkDirty(clip);
                        m_KeyDragMoved = true;
                        e.Use();
                        break;
                    }
                    var track = clip.tracks[m_DraggingTrack];
                    if (m_DraggingKey >= track.keys.Count) return;
                    float t = SnapTime(mouseT, clip, e.shift);
                    t = Mathf.Clamp(t, 0f, duration);
                    var k = track.keys[m_DraggingKey];
                    if (k.time != t)
                    {
                        Undo.RecordObject(clip, "Move VMG Key");
                        k.time = t;
                        track.keys[m_DraggingKey] = k;
                        clip.RecalculateDuration();
                        VMGTimelineSelection.MarkDirty(clip);
                        m_KeyDragMoved = true;
                    }
                    e.Use();
                    break;
                }
                case EventType.MouseUp:
                {
                    if (m_RowDragArmed || m_RowDragActive)
                    {
                        if (!m_RowDragActive)
                        {
                            // No movement past threshold: original click
                            // semantics — toggle collapse on the header row.
                            if (m_RowDragSourceRow >= 0 && m_RowDragSourceRow < m_Rows.Count)
                            {
                                var srcRow = m_Rows[m_RowDragSourceRow];
                                ToggleGroupCollapsed(clip, srcRow.groupKey);
                            }
                        }
                        else
                        {
                            CommitRowDrag(clip, e.mousePosition, trackTop);
                        }
                        m_RowDragArmed = false;
                        m_RowDragActive = false;
                        m_RowDragSourceRow = -1;
                        e.Use();
                        GUI.changed = true;
                    }
                    if (m_Scrubbing)
                    {
                        m_Scrubbing = false;
                        e.Use();
                    }
                    if (m_RubberArmed || m_RubberActive)
                    {
                        if (!m_RubberActive)
                        {
                            // Click without drag on empty area: fall back to
                            // track selection.
                            if (TryFindTrackRow(m_RubberStart, trackTop, out int tIdx))
                            {
                                if (m_Selection.trackIndex != tIdx) m_Selection.Select(tIdx, -1);
                            }
                        }
                        m_RubberArmed = false;
                        m_RubberActive = false;
                        m_RubberInitialSelection = null;
                        e.Use();
                        GUI.changed = true;
                    }
                    if (m_KeyDragMoved && m_DraggingTrack >= 0 && m_DraggingKey >= 0 && m_DraggingTrack < clip.tracks.Count)
                    {
                        if (m_MultiDragActive) SortAfterMultiDrag(clip);
                        else
                        {
                            var track = clip.tracks[m_DraggingTrack];
                            SortKeysAndUpdateSelection(track);
                        }
                        if (m_DragUndoGroup >= 0)
                        {
                            Undo.CollapseUndoOperations(m_DragUndoGroup);
                            Undo.SetCurrentGroupName(m_MultiDragActive ? "Move VMG Keys" : "Move VMG Key");
                        }
                    }
                    else if (m_DraggingEvent >= 0 && m_DragUndoGroup >= 0)
                    {
                        Undo.CollapseUndoOperations(m_DragUndoGroup);
                        Undo.SetCurrentGroupName("Move VMG Event");
                    }
                    else if (m_PendingShiftToggle)
                    {
                        // No drag — apply the pending Shift+click toggle.
                        m_Selection.AddOrRemove(m_PendingToggleTrack, m_PendingToggleKey);
                    }
                    m_DraggingTrack = -1;
                    m_DraggingKey = -1;
                    m_KeyDragMoved = false;
                    m_MultiDragActive = false;
                    m_DragSnapshots.Clear();
                    m_PendingShiftToggle = false;
                    m_DraggingEvent = -1;
                    m_DragUndoGroup = -1;
                    break;
                }
            }
        }

        void UpdateHover(Vector2 mousePos, Rect outer, float tlLeft, float trackTop, VMGAnimationClip clip, float duration, float pps, float scrollX)
        {
            int prevTrack = m_HoverTrack;
            int prevKey = m_HoverKey;
            int prevRow = m_HoverRow;

            // m_HoverRow is the *flattened* row index (group headers count).
            // m_HoverTrack is the real clip.tracks index (or -1 for headers
            // / no hit). This split lets banded/hover row tinting follow the
            // visual layout while key hit-test still keys off real trackIdx.
            m_HoverTrack = -1;
            m_HoverKey = -1;
            m_HoverRow = -1;

            if (outer.Contains(mousePos) && TryFindRow(mousePos, trackTop, out int rowIdx))
            {
                m_HoverRow = rowIdx;
                var row = m_Rows[rowIdx];
                if (row.kind == RowKind.Track && mousePos.x >= tlLeft
                    && TryHitKey(clip, row.trackIdx, mousePos, tlLeft, pps, scrollX, duration, out int keyIdx))
                {
                    m_HoverTrack = row.trackIdx;
                    m_HoverKey = keyIdx;
                }
            }

            if (prevTrack != m_HoverTrack || prevKey != m_HoverKey || prevRow != m_HoverRow)
                GUI.changed = true;
        }

        // Map mouse Y to the flattened row index. Caller decides what to do
        // with header vs. track rows via the Row struct.
        bool TryFindRow(Vector2 pos, float trackTop, out int rowIdx)
        {
            rowIdx = -1;
            // Reject clicks outside the track area (above the ruler or
            // below the horizontal scrollbar). m_TrackAreaTop/Bottom are
            // captured each Draw pass.
            if (pos.y < m_TrackAreaTop || pos.y >= m_TrackAreaBottom) return false;
            if (pos.y < trackTop) return false;
            int idx = Mathf.FloorToInt((pos.y - trackTop) / k_RowHeight);
            if (idx < 0 || idx >= m_Rows.Count) return false;
            rowIdx = idx;
            return true;
        }

        // For callers that only care about real tracks. Returns false when
        // the pointer is over a group header or below all rows.
        bool TryFindTrackRow(Vector2 pos, float trackTop, out int trackIdx)
        {
            trackIdx = -1;
            if (!TryFindRow(pos, trackTop, out int rowIdx)) return false;
            var row = m_Rows[rowIdx];
            if (row.kind != RowKind.Track) return false;
            trackIdx = row.trackIdx;
            return true;
        }

        static bool TryHitKey(VMGAnimationClip clip, int trackIdx, Vector2 pos, float tlLeft, float pps, float scrollX, float duration, out int keyIdx)
        {
            keyIdx = -1;
            if (trackIdx < 0 || trackIdx >= clip.tracks.Count) return false;
            var track = clip.tracks[trackIdx];
            if (track == null || track.keys == null) return false;
            for (int i = 0; i < track.keys.Count; i++)
            {
                float t = Mathf.Clamp(track.keys[i].time, 0f, duration);
                float cx = TimeToX(t, tlLeft, pps, scrollX);
                if (Mathf.Abs(pos.x - cx) <= k_KeyHitRadius)
                {
                    keyIdx = i;
                    return true;
                }
            }
            return false;
        }

        // `windowEnd` clamps the cursor so you can scrub into headroom (and
        // the playhead settles at the real duration). progress is normalized
        // against clip.duration so animator playback stays consistent.
        static void ScrubTo(VMGAnimator animator, float px, float tlLeft, float pps, float scrollX, float windowEnd, VMGAnimationClip clip, bool shiftHeld)
        {
            float t = XToTime(px, tlLeft, pps, scrollX);
            t = SnapTime(t, clip, shiftHeld);
            // Clamp the scrub cursor to clip.duration — there's nothing
            // animatable past it, so the playhead snaps to the end.
            float dur = Mathf.Max(0.0001f, clip != null ? clip.duration : windowEnd);
            t = Mathf.Clamp(t, 0f, dur);
            float u = t / dur;
            animator.progress = u;
            try { animator.Sample(u); }
            catch { }
            SceneView.RepaintAll();
        }

        void ShowTrackContextMenu(VMGAnimationClip clip, int trackIdx, Vector2 mousePos, float tlLeft, float pps, float scrollX, float duration)
        {
            float t = SnapTime(XToTime(mousePos.x, tlLeft, pps, scrollX), clip, shiftHeld: false);
            t = Mathf.Clamp(t, 0f, duration);
            var menu = new GenericMenu();

            // Section: Add
            menu.AddItem(new GUIContent($"Add Key at {t:0.###}s"), false, () =>
            {
                Undo.RecordObject(clip, "Add VMG Key");
                var track = clip.tracks[trackIdx];
                var k = new VMGAnimationKey { time = t };
                VMGEasingPresets.GetTangents(VMGEasingPreset.Linear, out var outT, out var inT);
                k.outTangent = outT;
                k.inTangent = inT;
                if (track.keys.Count > 0)
                {
                    var lastKey = FindNearestKey(track, t);
                    CopyValueFrom(ref k, lastKey, track.type);
                }
                track.keys.Add(k);
                track.keys.Sort((a, b) => a.time.CompareTo(b.time));
                clip.RecalculateDuration();
                VMGTimelineSelection.MarkDirty(clip);
                m_Selection.Clear();
            });

            menu.AddSeparator(string.Empty);

            // Section: Clipboard
            if (m_Selection.HasSelection)
                menu.AddItem(new GUIContent($"Copy {m_Selection.Count} Key(s)"), false, () => VMGKeyClipboard.Copy(clip, m_Selection.Items));
            else
                menu.AddDisabledItem(new GUIContent("Copy Keys (none selected)"));
            if (VMGKeyClipboard.HasContent)
            {
                int targetTrack = trackIdx;
                menu.AddItem(new GUIContent($"Paste {VMGKeyClipboard.Count} Key(s) at {t:0.###}s"), false, () =>
                {
                    Undo.RecordObject(clip, "Paste VMG Keys");
                    var sel = VMGKeyClipboard.Paste(clip, t, targetTrack, out var warnings);
                    foreach (var w in warnings) Debug.LogWarning($"[VMG.Animation] {w}");
                    VMGTimelineSelection.MarkDirty(clip);
                    m_Selection.ReplaceWith(sel);
                });
            }
            else
                menu.AddDisabledItem(new GUIContent("Paste Keys (clipboard empty)"));
            if (m_Selection.HasSelection)
                menu.AddItem(new GUIContent($"Delete {m_Selection.Count} Key(s)"), false, () => DeleteSelectedKeys(clip));
            else
                menu.AddDisabledItem(new GUIContent("Delete Key(s) (none selected)"));

            menu.AddSeparator(string.Empty);

            // Section: Group assignment
            BuildAssignToGroupSubmenu(menu, clip, trackIdx);

            menu.AddSeparator(string.Empty);

            // Section: Track
            menu.AddItem(new GUIContent("Delete Track"), false, () =>
            {
                Undo.RecordObject(clip, "Delete VMG Track");
                clip.tracks.RemoveAt(trackIdx);
                clip.RecalculateDuration();
                m_Selection.Clear();
                VMGTimelineSelection.MarkDirty(clip);
            });

            menu.ShowAsContext();
        }

        // "Assign to group >" submenu on the per-track right-click. Lists
        // every existing user group + a "(No group)" option + "New group...".
        // Check mark shows the track's current groupId.
        void BuildAssignToGroupSubmenu(GenericMenu menu, VMGAnimationClip clip, int trackIdx)
        {
            if (trackIdx < 0 || trackIdx >= clip.tracks.Count) return;
            var track = clip.tracks[trackIdx];
            if (track == null) return;
            int currentGid = track.groupId;

            menu.AddItem(new GUIContent("Assign to group/(No group)"), currentGid == 0, () =>
            {
                if (track.groupId == 0) return;
                Undo.RecordObject(clip, "Assign Track Group");
                track.groupId = 0;
                VMGTimelineSelection.MarkDirty(clip);
            });

            if (clip.userGroups != null)
            {
                foreach (var g in clip.userGroups)
                {
                    if (g == null) continue;
                    int gid = g.id;
                    string label = string.IsNullOrEmpty(g.name) ? "(unnamed)" : g.name;
                    menu.AddItem(new GUIContent($"Assign to group/{label}"), currentGid == gid, () =>
                    {
                        if (track.groupId == gid) return;
                        Undo.RecordObject(clip, "Assign Track Group");
                        track.groupId = gid;
                        VMGTimelineSelection.MarkDirty(clip);
                    });
                }
            }

            menu.AddSeparator("Assign to group/");
            menu.AddItem(new GUIContent("Assign to group/New group..."), false, () =>
            {
                PromptAndCreateGroup(clip, name =>
                {
                    Undo.RecordObject(clip, "Assign Track to New Group");
                    var g = new VMGTrackGroup { id = clip.NextGroupId(), name = name };
                    clip.userGroups.Add(g);
                    track.groupId = g.id;
                    VMGTimelineSelection.MarkDirty(clip);
                });
            });
        }

        // Right-click on any group header — both user groups and auto
        // subgroups. The available actions differ by row kind.
        void ShowGroupHeaderContextMenu(VMGAnimationClip clip, Row row)
        {
            var menu = new GenericMenu();
            if (row.kind == RowKind.UserGroup)
            {
                int gid = row.userGroupId;
                menu.AddItem(new GUIContent("Rename group..."), false, () =>
                {
                    PromptAndRenameGroup(clip, gid);
                });
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Delete group (keep tracks)"), false, () =>
                {
                    DeleteUserGroup(clip, gid, deleteTracks: false);
                });
                menu.AddItem(new GUIContent("Delete group and tracks"), false, () =>
                {
                    DeleteUserGroup(clip, gid, deleteTracks: true);
                });
            }
            else if (row.kind == RowKind.AutoGroup)
            {
                // Auto subgroups are derived; no rename. Offer to assign
                // every member track to a user group at once.
                menu.AddItem(new GUIContent("New group from these tracks..."), false, () =>
                {
                    PromptAndCreateGroup(clip, name =>
                    {
                        Undo.RecordObject(clip, "New Group From Auto Subgroup");
                        var g = new VMGTrackGroup { id = clip.NextGroupId(), name = name };
                        clip.userGroups.Add(g);
                        for (int ti = 0; ti < clip.tracks.Count; ti++)
                        {
                            var t = clip.tracks[ti];
                            if (t == null) continue;
                            if (t.groupId != row.userGroupId) continue;
                            if (BuildGroupKey(t.binding, row.userGroupId) != row.groupKey) continue;
                            t.groupId = g.id;
                        }
                        VMGTimelineSelection.MarkDirty(clip);
                    });
                });
            }

            // Empty menu is a no-op, but adding a Cancel keeps the click
            // from feeling broken when no actions exist.
            menu.ShowAsContext();
        }

        void DeleteUserGroup(VMGAnimationClip clip, int gid, bool deleteTracks)
        {
            Undo.RecordObject(clip, deleteTracks ? "Delete Group and Tracks" : "Delete Group");
            if (deleteTracks)
            {
                for (int i = clip.tracks.Count - 1; i >= 0; i--)
                {
                    if (clip.tracks[i] != null && clip.tracks[i].groupId == gid)
                        clip.tracks.RemoveAt(i);
                }
            }
            else
            {
                foreach (var t in clip.tracks)
                {
                    if (t != null && t.groupId == gid) t.groupId = 0;
                }
            }
            if (clip.userGroups != null)
            {
                for (int i = clip.userGroups.Count - 1; i >= 0; i--)
                {
                    if (clip.userGroups[i] != null && clip.userGroups[i].id == gid)
                        clip.userGroups.RemoveAt(i);
                }
            }
            clip.RecalculateDuration();
            m_Selection.Clear();
            VMGTimelineSelection.MarkDirty(clip);
        }

        void PromptAndCreateGroup(VMGAnimationClip clip, Action<string> onConfirmed)
        {
            VMGNameInputPopup.Show("Create Group", "Group name:", "Group", name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                onConfirmed(name.Trim());
            });
        }

        void PromptAndRenameGroup(VMGAnimationClip clip, int gid)
        {
            VMGTrackGroup g = null;
            foreach (var ug in clip.userGroups) { if (ug != null && ug.id == gid) { g = ug; break; } }
            if (g == null) return;
            string seed = g.name ?? string.Empty;
            VMGNameInputPopup.Show("Rename Group", "Group name:", seed, name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                Undo.RecordObject(clip, "Rename Group");
                g.name = name.Trim();
                VMGTimelineSelection.MarkDirty(clip);
            });
        }

        static VMGAnimationKey FindNearestKey(VMGAnimationTrack track, float t)
        {
            VMGAnimationKey best = track.keys[0];
            float bestDist = Mathf.Abs(best.time - t);
            for (int i = 1; i < track.keys.Count; i++)
            {
                float d = Mathf.Abs(track.keys[i].time - t);
                if (d < bestDist) { bestDist = d; best = track.keys[i]; }
            }
            return best;
        }

        static void CopyValueFrom(ref VMGAnimationKey target, VMGAnimationKey src, VMGChannelType type)
        {
            switch (type)
            {
                case VMGChannelType.Float: target.floatValue = src.floatValue; break;
                case VMGChannelType.Int: target.intValue = src.intValue; break;
                case VMGChannelType.Bool: target.boolValue = src.boolValue; break;
                case VMGChannelType.Color: target.colorValue = src.colorValue; break;
                case VMGChannelType.Vector2:
                case VMGChannelType.Vector3:
                case VMGChannelType.Vector4: target.vectorValue = src.vectorValue; break;
            }
        }

        void PasteAtPlayhead(VMGAnimationClip clip, VMGAnimator animator)
        {
            Undo.RecordObject(clip, "Paste VMG Keys");
            float start = animator.progress * Mathf.Max(0.0001f, clip.duration);
            int preferred = m_Selection.trackIndex; // -1 if no selection
            var sel = VMGKeyClipboard.Paste(clip, start, preferred, out var warnings);
            foreach (var w in warnings) Debug.LogWarning($"[VMG.Animation] {w}");
            VMGTimelineSelection.MarkDirty(clip);
            m_Selection.ReplaceWith(sel);
        }

        bool TryHitEvent(VMGAnimationClip clip, Vector2 pos, float tlLeft, float pps, float scrollX, float duration, out int evIdx)
        {
            evIdx = -1;
            if (clip.events == null) return false;
            const float hit = 7f;
            for (int i = 0; i < clip.events.Count; i++)
            {
                var ev = clip.events[i];
                if (ev == null) continue;
                float t = Mathf.Clamp(ev.time, 0f, duration);
                float cx = TimeToX(t, tlLeft, pps, scrollX);
                if (Mathf.Abs(pos.x - cx) <= hit) { evIdx = i; return true; }
            }
            return false;
        }

        void ShowEventRowContextMenu(VMGAnimationClip clip, Vector2 mousePos, float tlLeft, float pps, float scrollX, float duration)
        {
            float t = SnapTime(XToTime(mousePos.x, tlLeft, pps, scrollX), clip, shiftHeld: false);
            t = Mathf.Clamp(t, 0f, Mathf.Max(0f, duration));
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent($"Add Event at {t:0.###}s"), false, () =>
            {
                Undo.RecordObject(clip, "Add VMG Event");
                clip.events.Add(new VMGAnimationEvent { time = t, label = string.Empty });
                clip.events.Sort((a, b) => a.time.CompareTo(b.time));
                int newIdx = -1;
                for (int i = 0; i < clip.events.Count; i++)
                    if (Mathf.Approximately(clip.events[i].time, t)) { newIdx = i; break; }
                clip.RecalculateDuration();
                VMGTimelineSelection.MarkDirty(clip);
                if (newIdx >= 0) m_Selection.SelectEvent(newIdx);
            });
            if (TryHitEvent(clip, mousePos, tlLeft, pps, scrollX, duration, out int hitIdx))
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Delete Event"), false, () =>
                {
                    Undo.RecordObject(clip, "Delete VMG Event");
                    clip.events.RemoveAt(hitIdx);
                    m_Selection.ClearEvent();
                    clip.RecalculateDuration();
                    VMGTimelineSelection.MarkDirty(clip);
                });
            }
            menu.ShowAsContext();
        }

        void DeleteSelectedEvent(VMGAnimationClip clip)
        {
            int idx = m_Selection.EventIndex;
            if (idx < 0 || idx >= clip.events.Count) return;
            Undo.RecordObject(clip, "Delete VMG Event");
            clip.events.RemoveAt(idx);
            m_Selection.ClearEvent();
            clip.RecalculateDuration();
            VMGTimelineSelection.MarkDirty(clip);
        }

        void DeleteSelectedKeys(VMGAnimationClip clip)
        {
            Undo.RecordObject(clip, "Delete VMG Keys");
            var byTrack = new Dictionary<int, List<int>>();
            foreach (var it in m_Selection.Items)
            {
                if (it.track < 0 || it.track >= clip.tracks.Count) continue;
                var tr = clip.tracks[it.track];
                if (tr == null || it.key < 0 || it.key >= tr.keys.Count) continue;
                if (!byTrack.TryGetValue(it.track, out var list)) { list = new List<int>(); byTrack[it.track] = list; }
                list.Add(it.key);
            }
            foreach (var pair in byTrack)
            {
                pair.Value.Sort((a, b) => b.CompareTo(a));
                var tr = clip.tracks[pair.Key];
                foreach (var ki in pair.Value)
                    if (ki >= 0 && ki < tr.keys.Count) tr.keys.RemoveAt(ki);
            }
            m_Selection.Clear();
            clip.RecalculateDuration();
            VMGTimelineSelection.MarkDirty(clip);
        }

        void SortAfterMultiDrag(VMGAnimationClip clip)
        {
            // Snapshot pivot keys per drag entry to find them again after sort.
            var pivots = new List<(int track, VMGAnimationKey key)>();
            foreach (var snap in m_DragSnapshots)
            {
                if (snap.track < 0 || snap.track >= clip.tracks.Count) continue;
                var tr = clip.tracks[snap.track];
                if (tr == null || snap.key < 0 || snap.key >= tr.keys.Count) continue;
                pivots.Add((snap.track, tr.keys[snap.key]));
            }
            // Sort each unique track once.
            var touchedTracks = new HashSet<int>();
            foreach (var snap in m_DragSnapshots) touchedTracks.Add(snap.track);
            foreach (var ti in touchedTracks)
            {
                if (ti < 0 || ti >= clip.tracks.Count) continue;
                clip.tracks[ti]?.keys.Sort((a, b) => a.time.CompareTo(b.time));
            }
            // Rebuild selection by re-locating each pivot.
            var newSel = new List<VMGTimelineSelection.Item>();
            foreach (var (ti, pivot) in pivots)
            {
                var tr = clip.tracks[ti];
                if (tr == null) continue;
                for (int i = 0; i < tr.keys.Count; i++)
                {
                    var k = tr.keys[i];
                    if (k.time == pivot.time
                        && k.floatValue == pivot.floatValue
                        && k.intValue == pivot.intValue
                        && k.boolValue == pivot.boolValue
                        && k.colorValue == pivot.colorValue
                        && k.vectorValue == pivot.vectorValue)
                    {
                        var item = new VMGTimelineSelection.Item { track = ti, key = i };
                        if (!newSel.Contains(item)) newSel.Add(item);
                        break;
                    }
                }
            }
            m_Selection.ReplaceWith(newSel);
        }

        void SortKeysAndUpdateSelection(VMGAnimationTrack track)
        {
            if (m_DraggingKey < 0 || m_DraggingKey >= track.keys.Count) return;
            var pivot = track.keys[m_DraggingKey];
            int trackIdx = m_DraggingTrack;
            track.keys.Sort((a, b) => a.time.CompareTo(b.time));
            for (int i = 0; i < track.keys.Count; i++)
            {
                var k = track.keys[i];
                if (k.time == pivot.time
                    && k.floatValue == pivot.floatValue
                    && k.intValue == pivot.intValue
                    && k.boolValue == pivot.boolValue
                    && k.colorValue == pivot.colorValue
                    && k.vectorValue == pivot.vectorValue)
                {
                    m_Selection.Select(trackIdx, i);
                    return;
                }
            }
        }

        // ---------- add track ----------

        void DrawAddTrackBar(VMGAnimator animator)
        {
            var clip = animator.clip;
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add Group", GUILayout.Width(120f)))
            {
                PromptAndCreateGroup(clip, name =>
                {
                    Undo.RecordObject(clip, "Add VMG Group");
                    clip.userGroups.Add(new VMGTrackGroup
                    {
                        id = clip.NextGroupId(),
                        name = name,
                    });
                    VMGTimelineSelection.MarkDirty(clip);
                });
            }
            if (GUILayout.Button("+ Add Track", GUILayout.Width(120f)))
            {
                VMGChannelPickerWindow.Show(animator.transform, picked =>
                {
                    Undo.RecordObject(clip, "Add VMG Track");
                    clip.tracks.Add(new VMGAnimationTrack
                    {
                        type = picked.channelType,
                        binding = new VMGChannelBinding
                        {
                            gameObjectPath = picked.gameObjectPath,
                            componentTypeName = picked.componentTypeName,
                            fieldPath = picked.fieldPath,
                        },
                    });
                    VMGTimelineSelection.MarkDirty(clip);
                });
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
