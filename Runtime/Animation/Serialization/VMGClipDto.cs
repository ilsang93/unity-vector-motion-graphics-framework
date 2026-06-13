using System;
using System.Collections.Generic;
using UnityEngine;

namespace VMG.Animation.Serialization
{
    [Serializable]
    internal class VMGClipDto
    {
        // "1" → no userGroups / no track.groupId
        // "2" → userGroups + track.groupId present
        public string formatVersion = "2";
        public float duration;
        public bool loop;
        public int snapDivisor = 60;
        public List<VMGTrackDto> tracks = new List<VMGTrackDto>();
        public List<VMGEventDto> events = new List<VMGEventDto>();
        public List<VMGTrackGroupDto> userGroups = new List<VMGTrackGroupDto>();
        public VMGHierarchyDto hierarchy = new VMGHierarchyDto();
        public List<VMGShapeAssetRefDto> requiredShapeAssets = new List<VMGShapeAssetRefDto>();
    }

    [Serializable]
    internal class VMGTrackDto
    {
        public string gameObjectPath;
        public string componentTypeName;
        public string fieldPath;
        public int channelType;
        public int groupId;
        public List<VMGKeyDto> keys = new List<VMGKeyDto>();
    }

    [Serializable]
    internal class VMGTrackGroupDto
    {
        public int id;
        public string name;
    }

    [Serializable]
    internal class VMGKeyDto
    {
        public float time;
        public float floatValue;
        public int intValue;
        public bool boolValue;
        public Color colorValue;
        public Vector4 vectorValue;
        public Vector2 inTangent;
        public Vector2 outTangent;
    }

    [Serializable]
    internal class VMGEventDto
    {
        public float time;
        public string label;
    }

    [Serializable]
    internal class VMGHierarchyDto
    {
        public List<VMGHierarchyNodeDto> nodes = new List<VMGHierarchyNodeDto>();
    }

    [Serializable]
    internal class VMGHierarchyNodeDto
    {
        public string path;
        public List<VMGComponentSnapshotDto> components = new List<VMGComponentSnapshotDto>();
    }

    [Serializable]
    internal class VMGComponentSnapshotDto
    {
        public string componentTypeName;
        public string jsonState;
    }

    [Serializable]
    internal class VMGShapeAssetRefDto
    {
        public string guid;
        public string assetPath;
        public string name;
    }
}
