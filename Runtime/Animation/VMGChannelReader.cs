using UnityEngine;

namespace VMG.Animation
{
    internal class VMGChannelReader
    {
        readonly UnityEngine.Object m_Target;
        readonly VMGCompiledPath m_Path;
        readonly VMGChannelType m_Type;

        public VMGChannelType Type => m_Type;

        public VMGChannelReader(UnityEngine.Object target, VMGCompiledPath path, VMGChannelType type)
        {
            m_Target = target;
            m_Path = path;
            m_Type = type;
        }

        public object Read()
        {
            object owner = m_Target;
            var segs = m_Path.segments;
            for (int i = 0; i < segs.Length; i++) owner = segs[i].Get(owner);
            return owner;
        }
    }
}
