using System.Collections.Generic;
using VMG.Core;

namespace VMG.Fonts
{
    /// Caches glyph outlines so each (font, glyph) is parsed from the TTF
    /// bytes at most once. One provider wraps one font's bytes; build it
    /// from raw font bytes (Editor: TMP_FontAsset.sourceFontFile bytes;
    /// runtime: a baked/packaged byte[]).
    ///
    /// Returned GlyphContours are SHARED, cached instances — callers must
    /// treat them as read-only and copy nodes before transforming.
    public sealed class GlyphOutlineProvider
    {
        private readonly TtfOutlineParser m_Parser;
        private readonly Dictionary<int, GlyphContour> m_ByGid = new Dictionary<int, GlyphContour>(128);

        public bool IsUsable => m_Parser != null && m_Parser.HasOutlines;
        public int UnitsPerEm => m_Parser != null ? m_Parser.UnitsPerEm : 1000;

        /// Builds a provider from font bytes. Returns null only if the bytes
        /// have no usable outlines at all — both TrueType (`glyf`) and
        /// OpenType-CFF (`CFF `) are supported.
        public static GlyphOutlineProvider FromBytes(byte[] fontBytes)
        {
            if (fontBytes == null || fontBytes.Length < 12) return null;
            TtfOutlineParser parser;
            try { parser = new TtfOutlineParser(fontBytes); }
            catch { return null; }
            if (!parser.HasOutlines) return null;
            return new GlyphOutlineProvider(parser);
        }

        private GlyphOutlineProvider(TtfOutlineParser parser) { m_Parser = parser; }

        /// Outline for a unicode code point, cached. Empty contour for
        /// whitespace; null if the font is unusable.
        public GlyphContour GetGlyph(int unicode)
        {
            if (m_Parser == null) return null;
            int gid = m_Parser.GetGlyphIndex(unicode);
            if (m_ByGid.TryGetValue(gid, out var cached)) return cached;
            var g = m_Parser.GetGlyphByIndex(gid);
            m_ByGid[gid] = g;
            return g;
        }
    }
}
