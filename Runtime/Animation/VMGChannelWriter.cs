using System;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using VMGObjectId = UnityEngine.EntityId;
#else
using VMGObjectId = System.Int32;
#endif

namespace VMG.Animation
{
    internal class VMGChannelWriter
    {
        readonly UnityEngine.Object m_Target;
        readonly VMGCompiledPath m_Path;
        readonly VMGChannelType m_Type;
        readonly string m_FieldPath;
        readonly object[] m_BoxStack;

        public VMGChannelWriter(UnityEngine.Object target, VMGCompiledPath path, VMGChannelType type, string fieldPath = null)
        {
            m_Target = target;
            m_Path = path;
            m_Type = type;
            m_FieldPath = fieldPath;
            m_BoxStack = new object[path.segments.Length];
        }

        // Used by VMGTimeline.Remove(target) to find tweens bound to a
        // particular component. Object reference equality is enough — writers
        // are constructed with a concrete Component instance.
        public bool TargetEquals(UnityEngine.Object other) => ReferenceEquals(m_Target, other);

        // Revert baseline keying: (target instance ID, field path) identifies
        // a single (target, channel) slot regardless of how many writers point
        // to it. The TargetInstanceID dies with the UnityEngine.Object so the
        // key naturally collapses when the host is destroyed.
#if UNITY_6000_5_OR_NEWER
        public VMGObjectId TargetInstanceID => m_Target != null ? m_Target.GetEntityId() : EntityId.None;
#else
        public VMGObjectId TargetInstanceID => m_Target != null ? m_Target.GetInstanceID() : 0;
#endif
        public string FieldPath => m_FieldPath;
        public VMGChannelType ChannelType => m_Type;
        public UnityEngine.Object TargetObject => m_Target;

        public bool IsTypeCompatible(out string error)
        {
            var leaf = m_Path.leafType;
            error = null;
            switch (m_Type)
            {
                case VMGChannelType.Float:
                    if (leaf == typeof(float)) return true;
                    break;
                case VMGChannelType.Int:
                    if (leaf == typeof(int)) return true;
                    break;
                case VMGChannelType.Bool:
                    if (leaf == typeof(bool)) return true;
                    break;
                case VMGChannelType.Color:
                    if (leaf == typeof(Color)) return true;
                    break;
                case VMGChannelType.Vector2:
                    if (leaf == typeof(Vector2)) return true;
                    break;
                case VMGChannelType.Vector3:
                    if (leaf == typeof(Vector3)) return true;
                    break;
                case VMGChannelType.Vector4:
                    if (leaf == typeof(Vector4)) return true;
                    break;
            }
            error = $"channel type {m_Type} incompatible with leaf {leaf}";
            return false;
        }

        public void Write(in VMGAnimationKey value)
        {
            object leafValue = Box(value);
            WriteLeaf(leafValue);
        }

        public void Write(float v) => WriteLeaf(v);
        public void Write(int v) => WriteLeaf(v);
        public void Write(bool v) => WriteLeaf(v);
        public void Write(Color v) => WriteLeaf(v);
        public void Write(Vector2 v) => WriteLeaf(v);
        public void Write(Vector3 v) => WriteLeaf(v);
        public void Write(Vector4 v) => WriteLeaf(v);

        object Box(in VMGAnimationKey key)
        {
            switch (m_Type)
            {
                case VMGChannelType.Float: return key.floatValue;
                case VMGChannelType.Int: return key.intValue;
                case VMGChannelType.Bool: return key.boolValue;
                case VMGChannelType.Color: return key.colorValue;
                case VMGChannelType.Vector2: return (Vector2)key.vectorValue;
                case VMGChannelType.Vector3: return (Vector3)key.vectorValue;
                case VMGChannelType.Vector4: return key.vectorValue;
                default: return null;
            }
        }

        void WriteLeaf(object leafValue)
        {
            var segments = m_Path.segments;
            int n = segments.Length;

            // Walk down: box each intermediate so we can write back through
            // struct chains. (Field or Property — both go through Set on
            // the way back up.)
            object owner = m_Target;
            for (int i = 0; i < n - 1; i++)
            {
                m_BoxStack[i] = owner;
                owner = segments[i].Get(owner);
            }

            // Write leaf
            segments[n - 1].Set(owner, leafValue);

            // Walk back up: propagate change through struct-like segments.
            for (int i = n - 2; i >= 0; i--)
            {
                if (segments[i].IsStructLike)
                {
                    segments[i].Set(m_BoxStack[i], owner);
                    owner = m_BoxStack[i];
                }
                else
                {
                    break;
                }
            }
        }
    }
}
