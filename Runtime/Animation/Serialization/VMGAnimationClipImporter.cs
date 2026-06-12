using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VMG.Animation.Serialization
{
    public static partial class VMGAnimationClipSerializer
    {
        public static VMGImportResult Import(string json, VMGAnimator target, VMGImportOptions options = default)
        {
            var result = new VMGImportResult();
            if (string.IsNullOrEmpty(json))
            {
                result.warnings.Add("Import: input JSON is empty.");
                return result;
            }
            if (target == null)
            {
                result.warnings.Add("Import: target VMGAnimator is null.");
                return result;
            }

            VMGClipDto dto;
            try
            {
                dto = JsonUtility.FromJson<VMGClipDto>(json);
            }
            catch (Exception ex)
            {
                result.warnings.Add($"Import: JSON parse failed: {ex.Message}");
                return result;
            }
            if (dto == null)
            {
                result.warnings.Add("Import: JSON parse returned null.");
                return result;
            }

            var clip = ScriptableObject.CreateInstance<VMGAnimationClip>();
            clip.name = "Imported VMGAnimationClip";
            BuildRuntimeClip(dto, clip);
            result.clip = clip;

            HandleHierarchy(dto, target.transform, options, result);

            VerifyShapeAssetManifest(dto, target.transform, result);

            target.clip = clip;

            return result;
        }

        static void BuildRuntimeClip(VMGClipDto dto, VMGAnimationClip clip)
        {
            clip.duration = dto.duration;
            clip.loop = dto.loop;
            clip.autoFitDuration = dto.autoFitDuration;
            clip.snapDivisor = dto.snapDivisor;
            clip.tracks.Clear();
            clip.events.Clear();
            clip.hierarchy = new VMGHierarchySnapshot();

            if (dto.tracks != null)
            {
                foreach (var td in dto.tracks)
                {
                    if (td == null) continue;
                    var track = new VMGAnimationTrack
                    {
                        binding = new VMGChannelBinding
                        {
                            gameObjectPath = td.gameObjectPath,
                            componentTypeName = td.componentTypeName,
                            fieldPath = td.fieldPath,
                        },
                        type = (VMGChannelType)td.channelType,
                    };
                    if (td.keys != null)
                    {
                        foreach (var k in td.keys)
                        {
                            track.keys.Add(new VMGAnimationKey
                            {
                                time = k.time,
                                floatValue = k.floatValue,
                                intValue = k.intValue,
                                boolValue = k.boolValue,
                                colorValue = k.colorValue,
                                vectorValue = k.vectorValue,
                                inTangent = k.inTangent,
                                outTangent = k.outTangent,
                            });
                        }
                    }
                    clip.tracks.Add(track);
                }
            }

            if (dto.events != null)
            {
                foreach (var ed in dto.events)
                {
                    if (ed == null) continue;
                    clip.events.Add(new VMGAnimationEvent
                    {
                        time = ed.time,
                        label = ed.label,
                    });
                }
            }
        }

        static void HandleHierarchy(VMGClipDto dto, Transform root, VMGImportOptions options, VMGImportResult result)
        {
            bool hasChildren = root.childCount > 0;
            var mode = options.hierarchyMode;

            bool recreate;
            switch (mode)
            {
                case VMGHierarchyMode.ForceRecreate: recreate = true; break;
                case VMGHierarchyMode.SkipRecreate: recreate = false; break;
                default: recreate = !hasChildren; break;
            }

            if (!recreate)
            {
                if (mode == VMGHierarchyMode.Auto && hasChildren)
                {
                    result.warnings.Add("Hierarchy snapshot not applied: target has existing children. Use ForceRecreate to override.");
                }
                return;
            }

            if (hasChildren && mode == VMGHierarchyMode.ForceRecreate)
            {
                int destroyed = DestroyExistingChildren(root);
                result.warnings.Add($"ForceRecreate: destroyed {destroyed} existing child object(s).");
            }

            if (dto.hierarchy == null || dto.hierarchy.nodes == null || dto.hierarchy.nodes.Count == 0)
            {
                return;
            }

            foreach (var node in dto.hierarchy.nodes)
            {
                if (node == null) continue;
                var go = EnsurePath(root, node.path);
                if (go == null)
                {
                    result.warnings.Add($"Failed to create path '{node.path}'.");
                    continue;
                }
                if (node.components != null)
                {
                    foreach (var compDto in node.components)
                    {
                        ApplyComponent(go, compDto, result);
                    }
                }
            }

            result.hierarchyRecreated = true;
        }

        static int DestroyExistingChildren(Transform root)
        {
            int count = 0;
            var children = new List<Transform>();
            for (int i = 0; i < root.childCount; i++) children.Add(root.GetChild(i));
            foreach (var c in children)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(c.gameObject);
                else UnityEngine.Object.DestroyImmediate(c.gameObject);
                count++;
            }
            return count;
        }

        static GameObject EnsurePath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root.gameObject;
            var parts = path.Split('/');
            var current = root;
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                var child = current.Find(part);
                if (child == null)
                {
                    var go = new GameObject(part);
                    go.transform.SetParent(current, false);
                    child = go.transform;
                }
                current = child;
            }
            return current.gameObject;
        }

        static void ApplyComponent(GameObject go, VMGComponentSnapshotDto dto, VMGImportResult result)
        {
            if (dto == null || string.IsNullOrEmpty(dto.componentTypeName)) return;
            var type = ResolveType(dto.componentTypeName);
            if (type == null)
            {
                result.warnings.Add($"Component type '{dto.componentTypeName}' not resolvable; skipped on '{go.name}'.");
                return;
            }
            var component = go.GetComponent(type);
            if (component == null) component = go.AddComponent(type);
            if (component == null)
            {
                result.warnings.Add($"Failed to add component '{type.Name}' to '{go.name}'.");
                return;
            }
            if (!string.IsNullOrEmpty(dto.jsonState))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(dto.jsonState, component);
                }
                catch (Exception ex)
                {
                    result.warnings.Add($"FromJsonOverwrite failed on '{type.Name}' at '{go.name}': {ex.Message}");
                }
            }
        }

        static void VerifyShapeAssetManifest(VMGClipDto dto, Transform root, VMGImportResult result)
        {
            if (dto.requiredShapeAssets == null || dto.requiredShapeAssets.Count == 0) return;

            var found = new HashSet<string>();
            CollectShapeAssetNames(root, found);

            foreach (var entry in dto.requiredShapeAssets)
            {
                if (entry == null || string.IsNullOrEmpty(entry.name)) continue;
                if (!found.Contains(entry.name))
                {
                    result.missingShapeAssets.Add(entry.name);
                }
            }
            if (result.missingShapeAssets.Count > 0)
            {
                result.warnings.Add($"Import: {result.missingShapeAssets.Count} SVG shape asset(s) missing — re-import the SVG and assign manually.");
            }
        }

        static void CollectShapeAssetNames(Transform t, HashSet<string> sink)
        {
            var comps = t.GetComponents<MonoBehaviour>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                var ns = c.GetType().Namespace;
                if (ns != "VMG.UI" && ns != "VMG.World") continue;
                var fields = c.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var f in fields)
                {
                    if (!typeof(ScriptableObject).IsAssignableFrom(f.FieldType)) continue;
                    if (f.FieldType.Name != "VMGShapeAsset") continue;
                    var asset = f.GetValue(c) as ScriptableObject;
                    if (asset == null) continue;
                    if (!string.IsNullOrEmpty(asset.name)) sink.Add(asset.name);
                }
            }
            for (int i = 0; i < t.childCount; i++) CollectShapeAssetNames(t.GetChild(i), sink);
        }

        static Type ResolveType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var t = Type.GetType(name, throwOnError: false);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(name, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
