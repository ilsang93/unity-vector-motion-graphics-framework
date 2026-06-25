using UnityEngine;

namespace VMG.Text
{
    public enum WarpMode
    {
        None = 0,
        Arc,        // bend the baseline into an arc (PowerPoint "Arch")
        Circle,     // wrap the whole line around a center as a ring
        Trapezoid,  // taper width top vs bottom (perspective)
        Wave,       // sinusoidal vertical ripple along the line
        Grid,       // free-form envelope: drag a grid of control points
    }

    /// PowerPoint-WordArt-style text distortion. Applied per glyph vertex
    /// AFTER all glyphs are placed, in the text's local space, using each
    /// vertex's NORMALIZED position within the text bounds:
    ///   u = (x - minX) / width   in [0,1]  (left→right along the line)
    ///   v = (y - minY) / height  in [0,1]  (bottom→top)
    /// so a warp reads "where in the text block is this point" independent of
    /// font size / string length.
    ///
    /// Each mode is a closed-form map (u,v)->world plus its analytic Jacobian,
    /// so bezier in/out tangents transform correctly (they're directions, not
    /// points) and curves stay smooth instead of faceting.
    [System.Serializable]
    public struct VMGTextWarp
    {
        public WarpMode mode;

        [Tooltip("Primary distortion strength. Arc/Wave: bend height as a fraction of text width. Trapezoid: top-width shrink (−) / bottom-width shrink (+).")]
        [Range(-1f, 1f)] public float amount;

        [Tooltip("Secondary control. Circle: sweep angle in degrees (360 = full ring). Wave: number of crests across the line. Trapezoid: unused.")]
        public float secondary;

        // ---- Grid mode (free-form envelope) ----
        // Max grid: 5x5 CELLS => 6x6 = 36 control points. Stored as 36 NAMED
        // Vector2 fields (NOT a Vector2[]) because Unity's Animation window
        // only keyframes struct members, never array/list elements — array
        // control points can't be animated. Flat-N is the only path to
        // per-point keyframing (see the package's freeNodes/ShapeStack
        // precedent). Each is a NORMALIZED position in [0,1]x[0,1] over the
        // text bounds; field index = r*MaxCols + c on the FIXED 6-wide slot
        // grid, so changing active cols/rows keeps channel bindings stable.
        public const int MaxCellsPerSide = 5;
        public const int MaxCols = MaxCellsPerSide + 1; // 6 control cols
        public const int MaxRows = MaxCellsPerSide + 1; // 6 control rows
        public const int MaxPts = MaxCols * MaxRows;    // 36

        [Tooltip("Grid mode: number of cells across (1..5). Active control cols = this+1.")]
        public int gridCols;
        [Tooltip("Grid mode: number of cells up (1..5). Active control rows = this+1.")]
        public int gridRows;

        // 36 control points. Keyframable. Indexed r*MaxCols + c.
        public Vector2 p00, p01, p02, p03, p04, p05;
        public Vector2 p06, p07, p08, p09, p10, p11;
        public Vector2 p12, p13, p14, p15, p16, p17;
        public Vector2 p18, p19, p20, p21, p22, p23;
        public Vector2 p24, p25, p26, p27, p28, p29;
        public Vector2 p30, p31, p32, p33, p34, p35;

        // Has the grid been initialized to a uniform layout? (Default(struct)
        // leaves all points at (0,0); we lazily fill on first use.)
        public bool gridInitialized;

        public bool Enabled => mode != WarpMode.None;

        public static VMGTextWarp Default => new VMGTextWarp
        {
            mode = WarpMode.None,
            amount = 0.3f,
            secondary = 360f,
            gridCols = 3,
            gridRows = 2,
            gridInitialized = false, // filled lazily to a uniform grid
        };

        // Active control-point grid dimensions (cols+1 by rows+1).
        public int CtrlCols => Mathf.Clamp(gridCols, 1, MaxCellsPerSide) + 1;
        public int CtrlRows => Mathf.Clamp(gridRows, 1, MaxCellsPerSide) + 1;
        public int CtrlCount => CtrlCols * CtrlRows;

        // Flat-field accessors. Index is on the FIXED MaxCols-wide slot grid
        // (r*MaxCols + c), NOT the active-cols-wide one, so a point keeps the
        // same field/channel when cols/rows change.
        public Vector2 GetPt(int slot)
        {
            switch (slot)
            {
                case 0: return p00; case 1: return p01; case 2: return p02; case 3: return p03; case 4: return p04; case 5: return p05;
                case 6: return p06; case 7: return p07; case 8: return p08; case 9: return p09; case 10: return p10; case 11: return p11;
                case 12: return p12; case 13: return p13; case 14: return p14; case 15: return p15; case 16: return p16; case 17: return p17;
                case 18: return p18; case 19: return p19; case 20: return p20; case 21: return p21; case 22: return p22; case 23: return p23;
                case 24: return p24; case 25: return p25; case 26: return p26; case 27: return p27; case 28: return p28; case 29: return p29;
                case 30: return p30; case 31: return p31; case 32: return p32; case 33: return p33; case 34: return p34; case 35: return p35;
                default: return Vector2.zero;
            }
        }

        public void SetPt(int slot, Vector2 v)
        {
            switch (slot)
            {
                case 0: p00 = v; break; case 1: p01 = v; break; case 2: p02 = v; break; case 3: p03 = v; break; case 4: p04 = v; break; case 5: p05 = v; break;
                case 6: p06 = v; break; case 7: p07 = v; break; case 8: p08 = v; break; case 9: p09 = v; break; case 10: p10 = v; break; case 11: p11 = v; break;
                case 12: p12 = v; break; case 13: p13 = v; break; case 14: p14 = v; break; case 15: p15 = v; break; case 16: p16 = v; break; case 17: p17 = v; break;
                case 18: p18 = v; break; case 19: p19 = v; break; case 20: p20 = v; break; case 21: p21 = v; break; case 22: p22 = v; break; case 23: p23 = v; break;
                case 24: p24 = v; break; case 25: p25 = v; break; case 26: p26 = v; break; case 27: p27 = v; break; case 28: p28 = v; break; case 29: p29 = v; break;
                case 30: p30 = v; break; case 31: p31 = v; break; case 32: p32 = v; break; case 33: p33 = v; break; case 34: p34 = v; break; case 35: p35 = v; break;
            }
        }

        // Slot index (fixed grid) for an active-grid coordinate (c,r).
        public static int Slot(int c, int r) => r * MaxCols + c;

        /// Fill all active control points with the uniform-grid (identity)
        /// position if not yet initialized. Returns true if it wrote anything.
        public bool EnsureGrid()
        {
            if (gridInitialized) return false;
            int cols = CtrlCols, rows = CtrlRows;
            // Initialize the FULL slot grid to a uniform layout so unused slots
            // also hold sane values (harmless, keeps Animation channels tidy).
            for (int r = 0; r < MaxRows; r++)
                for (int c = 0; c < MaxCols; c++)
                {
                    float nx = MaxCols > 1 ? (float)c / (MaxCols - 1) : 0f;
                    float ny = MaxRows > 1 ? (float)r / (MaxRows - 1) : 0f;
                    // But place ACTIVE points on the active-grid spacing so the
                    // identity warp matches the active cols/rows exactly.
                    if (c < cols && r < rows)
                    {
                        nx = cols > 1 ? (float)c / (cols - 1) : 0f;
                        ny = rows > 1 ? (float)r / (rows - 1) : 0f;
                    }
                    SetPt(Slot(c, r), new Vector2(nx, ny));
                }
            gridInitialized = true;
            return true;
        }

        /// Reset all control points to the uniform identity grid.
        public void ResetGrid()
        {
            gridInitialized = false;
            EnsureGrid();
        }

        // Result of warping one point: the new position plus the 2x2 Jacobian
        // (∂out/∂in) used to rotate/scale tangents.
        public struct Mapped
        {
            public Vector2 pos;
            public Vector2 dXdu; // ∂pos/∂(local x)
            public Vector2 dYdv; // ∂pos/∂(local y)
        }

        /// Map a local-space point given the text bounds. Returns the warped
        /// position and a Jacobian so tangents can be transformed by the same
        /// local linearization. `b` carries minX/minY/width/height.
        public Mapped Map(Vector2 p, in Bounds2D b)
        {
            float w = Mathf.Max(b.width, 1e-4f);
            float h = Mathf.Max(b.height, 1e-4f);
            float u = (p.x - b.minX) / w;          // 0..1 along line
            float v = (p.y - b.minY) / h;          // 0..1 up
            float cx = b.minX + w * 0.5f;          // horizontal center (world)

            switch (mode)
            {
                case WarpMode.Arc: return MapArc(p, u, v, w, h, cx, b);
                case WarpMode.Circle: return MapCircle(p, u, v, w, h, b);
                case WarpMode.Trapezoid: return MapTrapezoid(p, u, v, w, h, cx, b);
                case WarpMode.Wave: return MapWave(p, u, v, w, h, b);
                case WarpMode.Grid: return MapGrid(u, v, w, h, b);
                default: return Identity(p);
            }
        }

        // ---- Grid: bilinear free-form envelope. Find the cell (u,v) lands in,
        // then bilerp the cell's four control points (mapped from normalized to
        // world). Jacobian unused (warp is applied to a pre-flattened polyline),
        // so return identity columns.
        private Mapped MapGrid(float u, float v, float w, float h, in Bounds2D b)
        {
            int cols = CtrlCols, rows = CtrlRows;
            int cellsX = cols - 1, cellsY = rows - 1;
            if (cellsX < 1 || cellsY < 1)
                return Identity(new Vector2(b.minX + u * w, b.minY + v * h));

            float fu = Mathf.Clamp01(u) * cellsX;
            float fv = Mathf.Clamp01(v) * cellsY;
            int ci = Mathf.Min((int)fu, cellsX - 1);
            int cj = Mathf.Min((int)fv, cellsY - 1);
            float su = fu - ci; // 0..1 within cell
            float sv = fv - cj;

            Vector2 c00 = CtrlWorld(ci, cj, b, w, h);
            Vector2 c10 = CtrlWorld(ci + 1, cj, b, w, h);
            Vector2 c01 = CtrlWorld(ci, cj + 1, b, w, h);
            Vector2 c11 = CtrlWorld(ci + 1, cj + 1, b, w, h);

            Vector2 bottom = Vector2.LerpUnclamped(c00, c10, su);
            Vector2 top = Vector2.LerpUnclamped(c01, c11, su);
            Vector2 pos = Vector2.LerpUnclamped(bottom, top, sv);
            return new Mapped { pos = pos, dXdu = new Vector2(1f, 0f), dYdv = new Vector2(0f, 1f) };
        }

        // Control point (c,r) mapped from its normalized position to world space
        // within the text bounds. Reads the flat field at the fixed slot index.
        private Vector2 CtrlWorld(int c, int r, in Bounds2D b, float w, float h)
        {
            Vector2 g = GetPt(Slot(c, r));
            return new Vector2(b.minX + g.x * w, b.minY + g.y * h);
        }

        private static Mapped Identity(Vector2 p) =>
            new Mapped { pos = p, dXdu = new Vector2(1f, 0f), dYdv = new Vector2(0f, 1f) };

        // ---- Arc: lift the baseline by a parabola peaking at the center.
        // y' = y + bend * 4 u(1-u) ; bend = amount * width. The fill body
        // shifts with its baseline so letters ride the curve. Also rotate
        // each column slightly to follow the tangent of the arc.
        private Mapped MapArc(Vector2 p, float u, float v, float w, float h, float cx, in Bounds2D b)
        {
            float bend = amount * w;
            // Baseline height profile f(u) = 4 u (1-u); f'(u) = 4 - 8u.
            float lift = bend * (4f * u * (1f - u));
            float slope = bend * (4f - 8f * u) / w; // d(lift)/dx

            // Rotate the column so verticals lean into the slope (tangent of arc).
            float ang = Mathf.Atan(slope);
            float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);

            float x = p.x;
            float y = p.y + lift;
            // Jacobian: x' = x ; y' = y + lift(x). ∂x'/∂x=1, ∂y'/∂x=slope,
            // ∂x'/∂y=0, ∂y'/∂y=1. Plus column rotation folded in for the
            // vertical direction so strokes stay perpendicular to the arc.
            var m = new Mapped { pos = new Vector2(x, y) };
            m.dXdu = new Vector2(1f, slope);
            m.dYdv = new Vector2(-sin, cos);
            return m;
        }

        // ---- Circle: wrap the line around a center. u maps to angle over the
        // sweep; v maps to radius (top of text = outer). Classic ring text.
        private Mapped MapCircle(Vector2 p, float u, float v, float w, float h, in Bounds2D b)
        {
            float sweep = secondary * Mathf.Deg2Rad;
            if (Mathf.Abs(sweep) < 1e-3f) sweep = Mathf.PI * 2f;
            float radius = w / sweep;                 // line length = arc length
            // v in [0,1] becomes radial offset around the baseline radius.
            float rBase = radius + amount * w * 0.5f; // amount nudges ring size
            float r = rBase + (v - 0.0f) * h;         // taller glyph parts reach out
            // Angle: start at top (−90°) and sweep clockwise across the line.
            float a = -Mathf.PI * 0.5f + (u - 0.5f) * sweep;
            float cos = Mathf.Cos(a), sin = Mathf.Sin(a);

            float cx = b.minX + w * 0.5f;
            float cy = b.minY - rBase;                 // center below the line
            var m = new Mapped();
            m.pos = new Vector2(cx + r * cos, cy + r * sin);
            // ∂pos/∂x = ∂pos/∂u * (1/w): tangential direction (−sin,cos)*r*(sweep/w).
            float dadx = sweep / w;
            m.dXdu = new Vector2(-sin, cos) * (r * dadx);
            // ∂pos/∂y = ∂pos/∂v * (1/h) * h = radial direction (cos,sin).
            m.dYdv = new Vector2(cos, sin);
            return m;
        }

        // ---- Trapezoid: scale each row's width by a factor that ramps from
        // bottom to top, giving a perspective taper. amount>0 narrows the top.
        private Mapped MapTrapezoid(Vector2 p, float u, float v, float w, float h, float cx, in Bounds2D b)
        {
            // Width scale at height v: 1 at bottom, (1-amount) at top.
            float scale = Mathf.Lerp(1f, 1f - amount, v);
            float x = cx + (p.x - cx) * scale;
            float y = p.y;
            var m = new Mapped { pos = new Vector2(x, y) };
            // ∂x'/∂x = scale ; ∂x'/∂y = (p.x-cx) * d(scale)/dy.
            float dScaleDy = (-amount) / h;
            m.dXdu = new Vector2(scale, 0f);
            m.dYdv = new Vector2((p.x - cx) * dScaleDy, 1f);
            return m;
        }

        // ---- Wave: sinusoidal vertical displacement along the line.
        private Mapped MapWave(Vector2 p, float u, float v, float w, float h, in Bounds2D b)
        {
            float crests = Mathf.Max(0.25f, secondary);
            float amp = amount * w * 0.25f;
            float phase = u * crests * Mathf.PI * 2f;
            float dy = amp * Mathf.Sin(phase);
            float ddyDx = amp * Mathf.Cos(phase) * (crests * Mathf.PI * 2f / w);
            var m = new Mapped { pos = new Vector2(p.x, p.y + dy) };
            m.dXdu = new Vector2(1f, ddyDx);
            m.dYdv = new Vector2(0f, 1f);
            return m;
        }

        /// Bounds of the placed (un-warped) text in local space.
        public struct Bounds2D
        {
            public float minX, minY, width, height;
            public Bounds2D(float minX, float minY, float maxX, float maxY)
            {
                this.minX = minX; this.minY = minY;
                width = maxX - minX; height = maxY - minY;
            }
        }
    }
}
