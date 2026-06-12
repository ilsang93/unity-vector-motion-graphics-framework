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
        const float k_KeyRadius = 4f;
        const float k_KeyHitRadius = 8f;
        const float k_RightPad = 10f;

        static readonly Color k_BgColor = new Color(0.18f, 0.18f, 0.18f, 1f);
        static readonly Color k_AltRowColor = new Color(1f, 1f, 1f, 0.03f);
        static readonly Color k_RulerColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        static readonly Color k_GridColor = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color k_SnapGridColor = new Color(1f, 1f, 1f, 0.04f);
        const float k_MinSnapGridPixels = 6f;
        static readonly Color k_BorderColor = new Color(0f, 0f, 0f, 0.6f);
        static readonly Color k_KeyColor = new Color(1f, 0.85f, 0.3f, 1f);
        static readonly Color k_KeySelectedColor = new Color(1f, 1f, 1f, 1f);
        static readonly Color k_KeyOutline = new Color(0f, 0f, 0f, 0.7f);
        static readonly Color k_KeySelectedOutline = new Color(1f, 0.45f, 0.05f, 1f);
        const float k_KeySelectedOutlineRadius = 3f;
        static readonly Color k_TrackSelectedColor = new Color(1f, 0.45f, 0.05f, 0.18f);
        static readonly Color k_TrackSelectedBorder = new Color(1f, 0.45f, 0.05f, 0.7f);
        static readonly Color k_EventColor = new Color(0.4f, 0.85f, 1f, 1f);
        static readonly Color k_EventSelectedColor = new Color(1f, 1f, 1f, 1f);
        static readonly Color k_EventSelectedOutline = new Color(1f, 0.45f, 0.05f, 1f);
        static readonly Color k_PlayheadColor = new Color(1f, 0.4f, 0.4f, 0.85f);

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

        float m_PixelsPerSecond;
        float m_ScrollX;

        const float k_MinPps = 4f;
        const float k_MaxPps = 1000f;
        const float k_ZoomStep = 1.15f;
        const float k_ScrollbarHeight = 12f;

        public VMGEditorPlayback Playback { get; set; }
        public bool DrawAddTrackBarEnabled { get; set; } = true;

        public void Draw(VMGAnimator animator)
        {
            var clip = animator.clip;
            if (clip == null) return;

            m_Selection = VMGTimelineSelection.For(animator);
            int trackCount = clip.tracks != null ? clip.tracks.Count : 0;
            float totalHeight = k_RulerHeight + k_EventRowHeight + Mathf.Max(trackCount, 1) * k_RowHeight + 2f + k_ScrollbarHeight;

            var rect = GUILayoutUtility.GetRect(0f, totalHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, k_BgColor);
            DrawBorder(rect);

            float tlLeft = rect.x + k_LabelWidth;
            float tlRight = rect.xMax - k_RightPad;
            float viewWidth = Mathf.Max(1f, tlRight - tlLeft);
            float duration = Mathf.Max(0.0001f, clip.duration);

            float fitPps = viewWidth / duration;
            float pps = m_PixelsPerSecond > 0f ? m_PixelsPerSecond : fitPps;
            pps = Mathf.Clamp(pps, Mathf.Max(k_MinPps, fitPps * 0.5f), k_MaxPps);
            float contentWidth = duration * pps;
            float maxScroll = Mathf.Max(0f, contentWidth - viewWidth);
            m_ScrollX = Mathf.Clamp(m_ScrollX, 0f, maxScroll);

            var rulerRect = new Rect(tlLeft, rect.y, viewWidth, k_RulerHeight);
            var eventRowRect = new Rect(tlLeft, rulerRect.yMax, viewWidth, k_EventRowHeight);
            var scrubRect = new Rect(tlLeft, rect.y, viewWidth, k_RulerHeight); // ruler only — events handled separately
            var zoomHotRect = new Rect(tlLeft, rect.y, viewWidth, totalHeight - k_ScrollbarHeight - 1f);
            HandleZoom(rulerRect, zoomHotRect, pps, fitPps, duration);
            // Re-read pps after possible zoom change.
            pps = m_PixelsPerSecond > 0f ? m_PixelsPerSecond : fitPps;
            pps = Mathf.Clamp(pps, Mathf.Max(k_MinPps, fitPps * 0.5f), k_MaxPps);
            contentWidth = duration * pps;
            maxScroll = Mathf.Max(0f, contentWidth - viewWidth);
            m_ScrollX = Mathf.Clamp(m_ScrollX, 0f, maxScroll);

            var gridRect = new Rect(tlLeft, rect.y, viewWidth, totalHeight - k_ScrollbarHeight - 2f);
            DrawSnapGrid(gridRect, clip, duration, pps, m_ScrollX);
            DrawRuler(rulerRect, duration, pps, m_ScrollX);

            EditorGUI.DrawRect(new Rect(rect.x, eventRowRect.y, k_LabelWidth, eventRowRect.height), k_RulerColor);
            GUI.Label(new Rect(rect.x + 4f, eventRowRect.y, k_LabelWidth - 6f, eventRowRect.height), "Events", EditorStyles.miniLabel);
            DrawEventsRow(eventRowRect, clip, duration, pps, m_ScrollX);

            float trackTop = eventRowRect.yMax;
            DrawTracks(rect, trackTop, tlLeft, viewWidth, clip, duration, pps, m_ScrollX);

            DrawPlayhead(new Rect(tlLeft, rulerRect.y, viewWidth, totalHeight - k_ScrollbarHeight - 2f), animator.progress, duration, pps, m_ScrollX);
            DrawRubberBand();

            float scrollbarY = rect.y + totalHeight - k_ScrollbarHeight - 1f;
            DrawHorizontalScrollbar(new Rect(tlLeft, scrollbarY, viewWidth, k_ScrollbarHeight), viewWidth, contentWidth);

            HandleInput(rect, tlLeft, viewWidth, trackTop, clip, duration, animator, scrubRect, eventRowRect, pps, m_ScrollX);

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

        static void DrawRuler(Rect rect, float duration, float pps, float scrollX)
        {
            EditorGUI.DrawRect(rect, k_RulerColor);

            float visibleSeconds = rect.width / Mathf.Max(pps, 0.0001f);
            float step = ChooseGridStep(visibleSeconds);
            int first = Mathf.FloorToInt(scrollX / Mathf.Max(pps, 0.0001f) / step);
            int last = Mathf.CeilToInt((scrollX + rect.width) / Mathf.Max(pps, 0.0001f) / step);
            var labelStyle = EditorStyles.miniLabel;
            for (int i = first; i <= last; i++)
            {
                float t = i * step;
                if (t < 0f || t > duration) continue;
                float x = TimeToX(t, rect.x, pps, scrollX);
                if (x < rect.x || x > rect.xMax) continue;
                EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), k_GridColor);
                GUI.Label(new Rect(x + 2f, rect.y, 60f, rect.height), $"{t:0.##}s", labelStyle);
            }
            float endX = TimeToX(duration, rect.x, pps, scrollX);
            if (endX >= rect.x && endX <= rect.xMax)
                EditorGUI.DrawRect(new Rect(endX - 1f, rect.y, 1f, rect.height), k_GridColor);
        }

        static float ChooseGridStep(float visibleSeconds)
        {
            float raw = visibleSeconds / 8f;
            float[] candidates = { 0.01f, 0.02f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 30f, 60f };
            foreach (var c in candidates)
            {
                if (raw <= c) return c;
            }
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

        void DrawTracks(Rect outer, float top, float tlLeft, float tlWidth, VMGAnimationClip clip, float duration, float pps, float scrollX)
        {
            int trackCount = clip.tracks != null ? clip.tracks.Count : 0;
            int selTrack = m_Selection.trackIndex;
            for (int i = 0; i < Mathf.Max(trackCount, 1); i++)
            {
                var rowRect = new Rect(outer.x, top + i * k_RowHeight, outer.width - 2f, k_RowHeight);
                if (i % 2 == 1) EditorGUI.DrawRect(rowRect, k_AltRowColor);
                if (i == selTrack)
                {
                    EditorGUI.DrawRect(rowRect, k_TrackSelectedColor);
                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 2f, rowRect.height), k_TrackSelectedBorder);
                }

                var labelRect = new Rect(rowRect.x + 4f, rowRect.y, k_LabelWidth - 6f, rowRect.height);
                if (i < trackCount && clip.tracks[i] != null)
                {
                    var track = clip.tracks[i];
                    string title = BuildTrackTitle(track, i);
                    GUI.Label(labelRect, title, EditorStyles.miniLabel);

                    var keysRect = new Rect(tlLeft, rowRect.y, tlWidth, rowRect.height);
                    DrawTrackKeys(keysRect, track, duration, i, pps, scrollX);
                }
                else
                {
                    GUI.Label(labelRect, "<empty>", EditorStyles.miniLabel);
                }
            }
        }

        static string BuildTrackTitle(VMGAnimationTrack track, int index)
        {
            var b = track.binding;
            string field = string.IsNullOrEmpty(b.fieldPath) ? "?" : b.fieldPath;
            string path = string.IsNullOrEmpty(b.gameObjectPath) ? "self" : b.gameObjectPath;
            return $"{index}: {path} · {field}";
        }

        void DrawTrackKeys(Rect rect, VMGAnimationTrack track, float duration, int trackIndex, float pps, float scrollX)
        {
            if (track.keys == null) return;
            float cy = rect.y + rect.height * 0.5f;
            for (int i = 0; i < track.keys.Count; i++)
            {
                var k = track.keys[i];
                float t = Mathf.Clamp(k.time, 0f, duration);
                float cx = TimeToX(t, rect.x, pps, scrollX);
                if (cx < rect.x - k_KeyRadius - 4f || cx > rect.xMax + k_KeyRadius + 4f) continue;
                bool selected = m_Selection.Contains(trackIndex, i);
                if (selected)
                {
                    Handles.color = k_KeySelectedOutline;
                    Handles.DrawSolidDisc(new Vector3(cx, cy, 0f), Vector3.forward, k_KeyRadius + k_KeySelectedOutlineRadius);
                }
                Handles.color = k_KeyOutline;
                Handles.DrawSolidDisc(new Vector3(cx, cy, 0f), Vector3.forward, k_KeyRadius + 1f);
                Handles.color = selected ? k_KeySelectedColor : k_KeyColor;
                Handles.DrawSolidDisc(new Vector3(cx, cy, 0f), Vector3.forward, k_KeyRadius);
            }
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

        void UpdateRubberSelection(VMGAnimationClip clip, float tlLeft, float pps, float scrollX, float duration, float trackTop)
        {
            var box = GetRubberRect();
            var picked = new List<VMGTimelineSelection.Item>();
            if (m_RubberShift && m_RubberInitialSelection != null) picked.AddRange(m_RubberInitialSelection);

            int trackCount = clip.tracks != null ? clip.tracks.Count : 0;
            for (int ti = 0; ti < trackCount; ti++)
            {
                var track = clip.tracks[ti];
                if (track == null || track.keys == null) continue;
                float rowTop = trackTop + ti * k_RowHeight;
                float cy = rowTop + k_RowHeight * 0.5f;
                if (cy < box.y || cy > box.yMax) continue;
                for (int ki = 0; ki < track.keys.Count; ki++)
                {
                    float t = Mathf.Clamp(track.keys[ki].time, 0f, duration);
                    float cx = TimeToX(t, tlLeft, pps, scrollX);
                    if (cx < box.x || cx > box.xMax) continue;
                    var item = new VMGTimelineSelection.Item { track = ti, key = ki };
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

        // ---------- input ----------

        int m_DraggingEvent = -1;

        void HandleInput(Rect outer, float tlLeft, float tlWidth, float trackTop, VMGAnimationClip clip, float duration, VMGAnimator animator, Rect scrubRect, Rect eventRowRect, float pps, float scrollX)
        {
            var e = Event.current;
            int trackCount = clip.tracks != null ? clip.tracks.Count : 0;

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
                    if (TryFindTrackRow(e.mousePosition, trackTop, trackCount, out int trackIdx))
                    {
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
                            clip.RecalculateDurationIfAuto();
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
                        clip.RecalculateDurationIfAuto();
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
                        clip.RecalculateDurationIfAuto();
                        VMGTimelineSelection.MarkDirty(clip);
                        m_KeyDragMoved = true;
                    }
                    e.Use();
                    break;
                }
                case EventType.MouseUp:
                {
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
                            if (TryFindTrackRow(m_RubberStart, trackTop, trackCount, out int tIdx))
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

        static bool TryFindTrackRow(Vector2 pos, float trackTop, int trackCount, out int trackIdx)
        {
            trackIdx = -1;
            if (pos.y < trackTop) return false;
            int idx = Mathf.FloorToInt((pos.y - trackTop) / k_RowHeight);
            if (idx < 0 || idx >= trackCount) return false;
            trackIdx = idx;
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

        static void ScrubTo(VMGAnimator animator, float px, float tlLeft, float pps, float scrollX, float duration, VMGAnimationClip clip, bool shiftHeld)
        {
            float t = XToTime(px, tlLeft, pps, scrollX);
            t = SnapTime(t, clip, shiftHeld);
            t = Mathf.Clamp(t, 0f, duration);
            float u = duration > 0f ? t / duration : 0f;
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
                clip.RecalculateDurationIfAuto();
                VMGTimelineSelection.MarkDirty(clip);
                m_Selection.Clear();
            });
            menu.AddSeparator(string.Empty);
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
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Delete Track"), false, () =>
            {
                Undo.RecordObject(clip, "Delete VMG Track");
                clip.tracks.RemoveAt(trackIdx);
                clip.RecalculateDurationIfAuto();
                m_Selection.Clear();
                VMGTimelineSelection.MarkDirty(clip);
            });
            menu.ShowAsContext();
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
                clip.RecalculateDurationIfAuto();
                VMGTimelineSelection.MarkDirty(clip);
                if (newIdx >= 0) m_Selection.SelectEvent(newIdx);
            });
            if (TryHitEvent(clip, mousePos, tlLeft, pps, scrollX, duration, out int hitIdx))
            {
                menu.AddItem(new GUIContent("Delete Event"), false, () =>
                {
                    Undo.RecordObject(clip, "Delete VMG Event");
                    clip.events.RemoveAt(hitIdx);
                    m_Selection.ClearEvent();
                    clip.RecalculateDurationIfAuto();
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
            clip.RecalculateDurationIfAuto();
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
            clip.RecalculateDurationIfAuto();
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
