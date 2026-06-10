using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VMG.Animation;

namespace VMG.EditorTools.Animation
{
    internal static class VMGChannelTreeBuilder
    {
        const int k_MaxDepth = 16;
        const BindingFlags k_Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static List<VMGChannelCandidate> Build(Transform root)
        {
            var result = new List<VMGChannelCandidate>();
            if (root == null) return result;
            WalkTransform(root, root, result);
            return result;
        }

        static void WalkTransform(Transform root, Transform current, List<VMGChannelCandidate> sink)
        {
            string goPath = ComputeRelativePath(root, current);
            string goLabel = string.IsNullOrEmpty(goPath) ? "self" : goPath;

            // Transform itself is widely keyframed — expose its serializable
            // position/rotation/scale via Transform's public surface.
            AddTransformChannels(goPath, goLabel, sink);

            foreach (var c in current.GetComponents<MonoBehaviour>())
            {
                if (c == null) continue;
                var type = c.GetType();
                string compLabel = type.Name;
                string compTypeName = type.AssemblyQualifiedName;

                foreach (var f in type.GetFields(k_Flags))
                {
                    if (!IsSerializableField(f)) continue;
                    WalkField(goPath, goLabel, compTypeName, compLabel, f, prefix: f.Name, displayPrefix: f.Name, depth: 1, sink);
                }
            }

            for (int i = 0; i < current.childCount; i++)
            {
                WalkTransform(root, current.GetChild(i), sink);
            }
        }

        static void AddTransformChannels(string goPath, string goLabel, List<VMGChannelCandidate> sink)
        {
            string typeName = typeof(Transform).AssemblyQualifiedName;
            AddVectorChannels(sink, goPath, goLabel, typeName, "Transform", "localPosition", VMGChannelType.Vector3);
            AddVectorChannels(sink, goPath, goLabel, typeName, "Transform", "localScale", VMGChannelType.Vector3);
            // localRotation as Euler not directly serializable from Transform's
            // public surface in a key-friendly way without quaternion conversion,
            // so we expose localEulerAngles instead.
            AddVectorChannels(sink, goPath, goLabel, typeName, "Transform", "localEulerAngles", VMGChannelType.Vector3);
        }

        static void AddVectorChannels(List<VMGChannelCandidate> sink, string goPath, string goLabel, string compTypeName, string compLabel, string fieldName, VMGChannelType type)
        {
            string display = $"{goLabel} / {compLabel} / {fieldName}";
            sink.Add(MakeCandidate(display, goPath, compTypeName, fieldName, type));
            string[] subs = type switch
            {
                VMGChannelType.Vector2 => new[] { "x", "y" },
                VMGChannelType.Vector3 => new[] { "x", "y", "z" },
                VMGChannelType.Vector4 => new[] { "x", "y", "z", "w" },
                VMGChannelType.Color => new[] { "r", "g", "b", "a" },
                _ => null,
            };
            if (subs == null) return;
            foreach (var s in subs)
            {
                sink.Add(MakeCandidate($"{display} / {s}", goPath, compTypeName, $"{fieldName}.{s}", VMGChannelType.Float));
            }
        }

        static void WalkField(string goPath, string goLabel, string compTypeName, string compLabel, FieldInfo field, string prefix, string displayPrefix, int depth, List<VMGChannelCandidate> sink)
        {
            if (depth > k_MaxDepth) return;
            var ft = field.FieldType;

            if (TryGetLeafChannel(ft, out var leafType))
            {
                string display = $"{goLabel} / {compLabel} / {displayPrefix}";
                sink.Add(MakeCandidate(display, goPath, compTypeName, prefix, leafType));

                // Also expose subcomponents for vector/color leaves so the user
                // can keyframe a single component (e.g., color.r).
                AddLeafSubcomponents(sink, goPath, goLabel, compTypeName, compLabel, prefix, displayPrefix, leafType);
                return;
            }

            if (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum)
            {
                foreach (var sub in ft.GetFields(k_Flags))
                {
                    if (!IsSerializableField(sub)) continue;
                    string nextPath = prefix + "." + sub.Name;
                    string nextDisplay = displayPrefix + " / " + sub.Name;
                    WalkField(goPath, goLabel, compTypeName, compLabel, sub, nextPath, nextDisplay, depth + 1, sink);
                }
            }
            // Class-typed serialized fields are opaque (Animation window
            // limitation) — skip walking into them.
        }

        static void AddLeafSubcomponents(List<VMGChannelCandidate> sink, string goPath, string goLabel, string compTypeName, string compLabel, string prefix, string displayPrefix, VMGChannelType leafType)
        {
            string[] subs = leafType switch
            {
                VMGChannelType.Vector2 => new[] { "x", "y" },
                VMGChannelType.Vector3 => new[] { "x", "y", "z" },
                VMGChannelType.Vector4 => new[] { "x", "y", "z", "w" },
                VMGChannelType.Color => new[] { "r", "g", "b", "a" },
                _ => null,
            };
            if (subs == null) return;
            foreach (var s in subs)
            {
                string display = $"{goLabel} / {compLabel} / {displayPrefix} / {s}";
                string path = prefix + "." + s;
                sink.Add(MakeCandidate(display, goPath, compTypeName, path, VMGChannelType.Float));
            }
        }

        static bool TryGetLeafChannel(Type t, out VMGChannelType channel)
        {
            if (t == typeof(float)) { channel = VMGChannelType.Float; return true; }
            if (t == typeof(int)) { channel = VMGChannelType.Int; return true; }
            if (t == typeof(bool)) { channel = VMGChannelType.Bool; return true; }
            if (t == typeof(Color)) { channel = VMGChannelType.Color; return true; }
            if (t == typeof(Vector2)) { channel = VMGChannelType.Vector2; return true; }
            if (t == typeof(Vector3)) { channel = VMGChannelType.Vector3; return true; }
            if (t == typeof(Vector4)) { channel = VMGChannelType.Vector4; return true; }
            channel = default;
            return false;
        }

        static bool IsSerializableField(FieldInfo f)
        {
            if (f.IsStatic) return false;
            if (f.IsDefined(typeof(NonSerializedAttribute), inherit: false)) return false;
            if (f.IsDefined(typeof(HideInInspector), inherit: false)) return false;
            if (f.IsPublic) return true;
            return f.IsDefined(typeof(SerializeField), inherit: false);
        }

        static VMGChannelCandidate MakeCandidate(string display, string goPath, string compTypeName, string fieldPath, VMGChannelType type)
        {
            return new VMGChannelCandidate
            {
                displayPath = display,
                gameObjectPath = goPath,
                componentTypeName = compTypeName,
                fieldPath = fieldPath,
                channelType = type,
                searchKey = display.ToLowerInvariant(),
            };
        }

        static string ComputeRelativePath(Transform root, Transform target)
        {
            if (target == root) return string.Empty;
            var stack = new Stack<string>();
            var t = target;
            while (t != null && t != root)
            {
                stack.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", stack);
        }
    }
}
