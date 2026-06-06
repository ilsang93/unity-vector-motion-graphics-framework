namespace VMG.Core
{
    /// Per-renderer scratch buffers: a reusable VectorPath that
    /// modifiers chew on, and a MeshBuffer that builders fill. Held by
    /// the renderer so allocations don't repeat each frame.
    public sealed class ShapePipeline
    {
        public readonly VectorPath workingPath = new VectorPath();
        public readonly MeshBuffer mesh = new MeshBuffer();
    }
}
