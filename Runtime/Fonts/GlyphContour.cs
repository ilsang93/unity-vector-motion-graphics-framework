using System.Collections.Generic;
using VMG.Core;

namespace VMG.Fonts
{
    /// One parsed glyph: its closed contours plus the horizontal metrics
    /// needed to place it on a baseline. Coordinates are in FONT EM UNITS
    /// (i.e. raw glyf coordinates), NOT normalized — divide by
    /// UnitsPerEm to get the [0..~1] em-square space, then scale to the
    /// target point size. Keeping raw units here lets the caller decide
    /// the normalization so placement matches TMP's layout exactly.
    ///
    /// Contour winding follows the TrueType convention as authored in the
    /// font: outer contours and inner (hole) contours have opposite
    /// orientation. The downstream even-odd FillTessellator renders holes
    /// correctly regardless of absolute winding direction.
    public sealed class GlyphContour
    {
        /// Closed contours. Each is a list of VectorNodes using the VMG
        /// cubic-bezier convention: outTangent/inTangent are RELATIVE
        /// offsets from the node position, type = Bezier on curved nodes,
        /// Corner on straight ones. Always treated as closed.
        public readonly List<List<VectorNode>> contours = new List<List<VectorNode>>();

        /// Glyph advance width in em units (how far the pen moves). Useful
        /// for validation against TMP's xAdvance; layout itself comes from
        /// TMP, so this is informational.
        public float advanceWidth;

        /// Left side bearing in em units.
        public float leftSideBearing;

        /// The font's units-per-em (e.g. 1000 for CFF-style, 2048 typical
        /// for TrueType). Divide contour coords by this to normalize.
        public int unitsPerEm = 1000;

        /// True when the glyph has no outline (space, control chars).
        public bool isEmpty => contours.Count == 0;
    }
}
