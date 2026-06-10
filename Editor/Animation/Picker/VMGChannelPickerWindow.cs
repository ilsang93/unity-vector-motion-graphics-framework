using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VMG.EditorTools.Animation
{
    public class VMGChannelPickerWindow : EditorWindow
    {
        Transform m_Root;
        Action<VMGChannelCandidate> m_OnPicked;
        List<VMGChannelCandidate> m_All = new List<VMGChannelCandidate>();
        List<VMGChannelCandidate> m_Filtered = new List<VMGChannelCandidate>();
        TreeNode m_Tree;
        string m_Search = string.Empty;
        Vector2 m_Scroll;
        HashSet<string> m_Expanded = new HashSet<string>();

        internal static void Show(Transform root, Action<VMGChannelCandidate> onPicked)
        {
            var w = CreateInstance<VMGChannelPickerWindow>();
            w.titleContent = new GUIContent("Pick Channel");
            w.m_Root = root;
            w.m_OnPicked = onPicked;
            w.Rebuild();
            w.minSize = new Vector2(420f, 320f);
            w.ShowUtility();
        }

        void Rebuild()
        {
            m_All = VMGChannelTreeBuilder.Build(m_Root);
            ApplyFilter();
        }

        void ApplyFilter()
        {
            m_Filtered.Clear();
            string s = (m_Search ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(s))
            {
                m_Filtered.AddRange(m_All);
            }
            else
            {
                foreach (var c in m_All)
                {
                    if (c.searchKey.Contains(s)) m_Filtered.Add(c);
                }
            }
            m_Tree = BuildTree(m_Filtered);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField($"Root: {(m_Root != null ? m_Root.name : "<none>")}   ({m_All.Count} channels, {m_Filtered.Count} shown)", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            m_Search = EditorGUILayout.TextField("Search", m_Search);
            if (EditorGUI.EndChangeCheck()) ApplyFilter();

            EditorGUILayout.Space();

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            if (m_Tree != null)
            {
                bool forceExpand = !string.IsNullOrEmpty(m_Search);
                DrawNode(m_Tree, depth: 0, forceExpand);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(80f))) Close();
            EditorGUILayout.EndHorizontal();
        }

        void DrawNode(TreeNode node, int depth, bool forceExpand)
        {
            foreach (var child in node.children)
            {
                if (child.candidate != null)
                {
                    DrawLeaf(child, depth);
                }
                else
                {
                    DrawGroup(child, depth, forceExpand);
                }
            }
        }

        void DrawGroup(TreeNode node, int depth, bool forceExpand)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 12f);
            bool expanded = forceExpand || m_Expanded.Contains(node.key);
            bool next = EditorGUILayout.Foldout(expanded, node.label, true);
            if (!forceExpand)
            {
                if (next && !expanded) m_Expanded.Add(node.key);
                else if (!next && expanded) m_Expanded.Remove(node.key);
            }
            EditorGUILayout.EndHorizontal();
            if (next) DrawNode(node, depth + 1, forceExpand);
        }

        void DrawLeaf(TreeNode node, int depth)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 12f + 14f);
            var label = $"{node.label}   ({node.candidate.channelType})";
            if (GUILayout.Button(label, EditorStyles.linkLabel, GUILayout.ExpandWidth(true)))
            {
                m_OnPicked?.Invoke(node.candidate);
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ---------- tree ----------

        class TreeNode
        {
            public string key;
            public string label;
            public List<TreeNode> children = new List<TreeNode>();
            public VMGChannelCandidate candidate;
        }

        static TreeNode BuildTree(List<VMGChannelCandidate> items)
        {
            var root = new TreeNode { key = string.Empty, label = string.Empty };
            foreach (var c in items)
            {
                var parts = c.displayPath.Split(new[] { " / " }, StringSplitOptions.None);
                var current = root;
                string acc = string.Empty;
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i];
                    acc = i == 0 ? p : acc + " / " + p;
                    bool isLeaf = i == parts.Length - 1;
                    TreeNode found = null;
                    foreach (var ch in current.children)
                    {
                        if (ch.label == p && (ch.candidate == null) == !isLeaf) { found = ch; break; }
                    }
                    if (found == null)
                    {
                        found = new TreeNode { key = acc, label = p };
                        if (isLeaf) found.candidate = c;
                        current.children.Add(found);
                    }
                    current = found;
                }
            }
            return root;
        }
    }
}
