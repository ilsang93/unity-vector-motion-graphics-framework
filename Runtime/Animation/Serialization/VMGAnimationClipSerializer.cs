using System.Collections.Generic;
using UnityEngine;

namespace VMG.Animation.Serialization
{
    public static partial class VMGAnimationClipSerializer
    {
        public static string Export(VMGAnimator animator, bool prettyPrint = true)
        {
            if (animator == null) return null;
            var dto = BuildDto(animator.clip, animator.transform);
            return JsonUtility.ToJson(dto, prettyPrint);
        }

        public static string Export(VMGAnimationClip clip, bool prettyPrint = true)
        {
            if (clip == null) return null;
            var dto = BuildDto(clip, root: null);
            return JsonUtility.ToJson(dto, prettyPrint);
        }

        static VMGClipDto BuildDto(VMGAnimationClip clip, Transform root)
        {
            var dto = new VMGClipDto();
            if (clip == null) return dto;

            dto.duration = clip.duration;
            dto.loop = clip.loop;
            dto.autoFitDuration = clip.autoFitDuration;
            dto.snapDivisor = clip.snapDivisor;

            if (clip.tracks != null)
            {
                foreach (var track in clip.tracks)
                {
                    if (track == null) continue;
                    dto.tracks.Add(BuildTrackDto(track));
                }
            }

            if (clip.events != null)
            {
                foreach (var ev in clip.events)
                {
                    if (ev == null) continue;
                    dto.events.Add(new VMGEventDto { time = ev.time, label = ev.label });
                }
            }

            if (root != null)
            {
                BuildHierarchyDto(root, dto);
            }

            return dto;
        }

        static VMGTrackDto BuildTrackDto(VMGAnimationTrack track)
        {
            var t = new VMGTrackDto
            {
                gameObjectPath = track.binding.gameObjectPath,
                componentTypeName = track.binding.componentTypeName,
                fieldPath = track.binding.fieldPath,
                channelType = (int)track.type,
            };
            if (track.keys != null)
            {
                foreach (var k in track.keys)
                {
                    t.keys.Add(new VMGKeyDto
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
            return t;
        }

        static void BuildHierarchyDto(Transform root, VMGClipDto dto)
        {
            var seenAssets = new HashSet<string>();
            WalkAndSnapshot(root, root, dto, seenAssets);
        }

        static void WalkAndSnapshot(Transform root, Transform current, VMGClipDto dto, HashSet<string> seenAssets)
        {
            if (HasRenderingComponent(current, out var snapshot))
            {
                snapshot.path = ComputeRelativePath(root, current);
                dto.hierarchy.nodes.Add(snapshot);
                CollectShapeAssetReferences(current, dto.requiredShapeAssets, seenAssets);
            }
            for (int i = 0; i < current.childCount; i++)
            {
                WalkAndSnapshot(root, current.GetChild(i), dto, seenAssets);
            }
        }

        static bool HasRenderingComponent(Transform t, out VMGHierarchyNodeDto node)
        {
            node = null;
            var comps = t.GetComponents<MonoBehaviour>();
            bool any = false;
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (!IsVmgRenderingComponent(c)) continue;
                if (!any)
                {
                    node = new VMGHierarchyNodeDto();
                    any = true;
                }
                node.components.Add(new VMGComponentSnapshotDto
                {
                    componentTypeName = c.GetType().AssemblyQualifiedName,
                    jsonState = JsonUtility.ToJson(c),
                });
            }
            return any;
        }

        static bool IsVmgRenderingComponent(MonoBehaviour c)
        {
            var ns = c.GetType().Namespace;
            return ns == "VMG.UI" || ns == "VMG.World";
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

        static void CollectShapeAssetReferences(Transform t, List<VMGShapeAssetRefDto> manifest, HashSet<string> seen)
        {
            var comps = t.GetComponents<MonoBehaviour>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (!IsVmgRenderingComponent(c)) continue;

                var fields = c.GetType().GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                foreach (var f in fields)
                {
                    if (!typeof(ScriptableObject).IsAssignableFrom(f.FieldType)) continue;
                    if (f.FieldType.Name != "VMGShapeAsset") continue;
                    var asset = f.GetValue(c) as ScriptableObject;
                    if (asset == null) continue;
                    string assetName = asset.name;
                    if (string.IsNullOrEmpty(assetName)) continue;
                    if (!seen.Add(assetName)) continue;
                    manifest.Add(new VMGShapeAssetRefDto
                    {
                        guid = string.Empty,
                        assetPath = string.Empty,
                        name = assetName,
                    });
                }
            }
        }
    }
}
