namespace VMG.Core
{
    /// Authoring side. Produces a VectorPath each evaluation.
    public interface IShapeSource
    {
        void Build(VectorPath outPath);
    }
}
