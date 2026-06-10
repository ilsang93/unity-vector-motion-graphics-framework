using System;
using System.Reflection;
using UnityEngine;

namespace VMG.Animation
{
    internal enum VMGPathSegmentKind
    {
        Field,
        StructField,
        Property,
        StructProperty,
    }

    internal struct VMGPathSegment
    {
        public FieldInfo field;
        public PropertyInfo property;
        public VMGPathSegmentKind kind;

        public Type MemberType => field != null ? field.FieldType : property.PropertyType;

        public object Get(object owner) => field != null ? field.GetValue(owner) : property.GetValue(owner);

        public void Set(object owner, object value)
        {
            if (field != null) field.SetValue(owner, value);
            else property.SetValue(owner, value);
        }

        public bool IsStructLike =>
            kind == VMGPathSegmentKind.StructField || kind == VMGPathSegmentKind.StructProperty;
    }

    internal class VMGCompiledPath
    {
        public VMGPathSegment[] segments;
        public Type leafType;
    }

    internal static class VMGFieldPathCompiler
    {
        public const int MaxDepth = 16;
        const BindingFlags k_Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static bool TryCompile(Type rootType, string fieldPath, out VMGCompiledPath compiled, out string error)
        {
            compiled = null;
            error = null;

            if (rootType == null) { error = "root type is null"; return false; }
            if (string.IsNullOrEmpty(fieldPath)) { error = "fieldPath is empty"; return false; }

            var parts = fieldPath.Split('.');
            if (parts.Length > MaxDepth)
            {
                error = $"fieldPath depth {parts.Length} exceeds cap {MaxDepth}";
                return false;
            }

            var segments = new VMGPathSegment[parts.Length];
            var current = rootType;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!TryResolveMember(current, parts[i], out var seg, out error)) return false;
                segments[i] = seg;
                current = seg.MemberType;
            }

            compiled = new VMGCompiledPath
            {
                segments = segments,
                leafType = current,
            };
            return true;
        }

        static bool TryResolveMember(Type type, string name, out VMGPathSegment seg, out string error)
        {
            seg = default;
            error = null;

            var fi = FindField(type, name);
            if (fi != null)
            {
                seg = new VMGPathSegment
                {
                    field = fi,
                    kind = IsStructLike(fi.FieldType)
                        ? VMGPathSegmentKind.StructField
                        : VMGPathSegmentKind.Field,
                };
                return true;
            }

            var pi = FindProperty(type, name);
            if (pi != null)
            {
                if (!pi.CanRead || !pi.CanWrite)
                {
                    error = $"property '{name}' on {type.FullName} is not read/write";
                    return false;
                }
                seg = new VMGPathSegment
                {
                    property = pi,
                    kind = IsStructLike(pi.PropertyType)
                        ? VMGPathSegmentKind.StructProperty
                        : VMGPathSegmentKind.Property,
                };
                return true;
            }

            error = $"member '{name}' not found on type {type.FullName}";
            return false;
        }

        static bool IsStructLike(Type t)
        {
            return t.IsValueType && !t.IsPrimitive && !t.IsEnum;
        }

        static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var fi = t.GetField(name, k_Flags);
                if (fi != null) return fi;
            }
            return null;
        }

        static PropertyInfo FindProperty(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var pi = t.GetProperty(name, k_Flags);
                if (pi != null) return pi;
            }
            return null;
        }
    }
}
