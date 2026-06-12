using System;
using System.Collections.Generic;
using UnityEngine;

namespace VMG.Animation
{
    internal class VMGResolvedTrack
    {
        public VMGAnimationTrack track;
        public VMGChannelWriter writer;
        public bool hasLastValue;
        public float lastFloat;
        public int lastInt;
        public bool lastBool;
        public Color lastColor;
        public Vector4 lastVector;
    }

    internal class VMGBindingResolver
    {
        readonly Transform m_Root;
        readonly List<VMGResolvedTrack> m_Resolved = new List<VMGResolvedTrack>();

        // Shared across all resolvers — type name lookup is domain-stable,
        // and the per-resolver instance cache used to blow startup time
        // when the resolver got rebuilt frequently (e.g. inspector refresh
        // on key click).
        static readonly Dictionary<string, Type> s_TypeCache = new Dictionary<string, Type>();

        public IReadOnlyList<VMGResolvedTrack> Tracks => m_Resolved;

        public VMGBindingResolver(Transform root)
        {
            m_Root = root;
        }

        public int Resolve(VMGAnimationClip clip)
        {
            m_Resolved.Clear();
            if (clip == null) return 0;

            int failed = 0;
            foreach (var track in clip.tracks)
            {
                if (TryResolveTrack(track, out var resolved)) m_Resolved.Add(resolved);
                else failed++;
            }
            return failed;
        }

        bool TryResolveTrack(VMGAnimationTrack track, out VMGResolvedTrack resolved)
        {
            resolved = null;
            if (track == null) return false;

            // Silently skip tracks whose binding hasn't been authored yet —
            // empty fields are an expected intermediate state (e.g. just added
            // a track and haven't picked the channel). Treat as not-yet-bound
            // rather than an error.
            if (string.IsNullOrEmpty(track.binding.componentTypeName)
                || string.IsNullOrEmpty(track.binding.fieldPath))
            {
                return false;
            }

            var go = FindGameObject(track.binding.gameObjectPath);
            if (go == null)
            {
                string children = ListChildNames(m_Root);
                Debug.LogError($"[VMG.Animation] gameObject path '{track.binding.gameObjectPath}' not found under '{m_Root.name}'. Existing children: [{children}]");
                return false;
            }

            var compType = ResolveType(track.binding.componentTypeName);
            if (compType == null)
            {
                Debug.LogError($"[VMG.Animation] component type '{track.binding.componentTypeName}' not resolvable");
                return false;
            }

            var component = go.GetComponent(compType);
            if (component == null)
            {
                Debug.LogError($"[VMG.Animation] component '{compType.Name}' not found on '{go.name}'");
                return false;
            }

            if (!VMGFieldPathCompiler.TryCompile(compType, track.binding.fieldPath, out var path, out var error))
            {
                Debug.LogError($"[VMG.Animation] fieldPath '{track.binding.fieldPath}' compile failed: {error}");
                return false;
            }

            var writer = new VMGChannelWriter(component, path, track.type, track.binding.fieldPath);
            if (!writer.IsTypeCompatible(out var typeError))
            {
                Debug.LogError($"[VMG.Animation] {typeError} (path '{track.binding.fieldPath}')");
                return false;
            }

            resolved = new VMGResolvedTrack
            {
                track = track,
                writer = writer,
            };
            return true;
        }

        static string ListChildNames(Transform root)
        {
            if (root == null || root.childCount == 0) return "<none>";
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < root.childCount; i++)
            {
                if (i > 0) names.Append(", ");
                names.Append('\'');
                names.Append(root.GetChild(i).name);
                names.Append('\'');
            }
            return names.ToString();
        }

        GameObject FindGameObject(string path)
        {
            if (string.IsNullOrEmpty(path)) return m_Root.gameObject;
            var t = m_Root.Find(path);
            return t != null ? t.gameObject : null;
        }

        Type ResolveType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (s_TypeCache.TryGetValue(name, out var cached)) return cached;

            var t = Type.GetType(name, throwOnError: false);
            if (t == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(name, throwOnError: false);
                    if (t != null) break;
                }
            }
            s_TypeCache[name] = t;
            return t;
        }
    }
}
