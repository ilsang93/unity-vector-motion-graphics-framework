namespace VMG.Core
{
    /// In-place path modifier. Each modifier mutates the path buffer
    /// passed in, producing the input to the next stage.
    public interface IPathModifier
    {
        bool Enabled { get; }
        void Apply(VectorPath path);
    }
}
