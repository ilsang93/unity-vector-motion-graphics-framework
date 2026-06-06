using System.Globalization;

namespace VMG.Svg
{
    internal static class SvgTransformParser
    {
        public static Matrix2D Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return Matrix2D.Identity;
            Matrix2D acc = Matrix2D.Identity;
            int i = 0;
            int len = s.Length;
            while (i < len)
            {
                while (i < len && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
                int nameStart = i;
                while (i < len && (char.IsLetter(s[i]))) i++;
                if (nameStart == i) break;
                string name = s.Substring(nameStart, i - nameStart);
                while (i < len && s[i] != '(') i++;
                if (i >= len) break;
                i++; // skip '('
                int argStart = i;
                while (i < len && s[i] != ')') i++;
                string argsStr = s.Substring(argStart, i - argStart);
                if (i < len) i++; // skip ')'
                var args = ParseArgs(argsStr);
                Matrix2D m = BuildOp(name, args);
                acc = Matrix2D.Multiply(acc, m);
            }
            return acc;
        }

        private static float[] ParseArgs(string s)
        {
            var parts = s.Split(new[] { ' ', ',', '\t', '\n', '\r' },
                                System.StringSplitOptions.RemoveEmptyEntries);
            var result = new float[parts.Length];
            for (int j = 0; j < parts.Length; j++)
            {
                float.TryParse(parts[j], NumberStyles.Float, CultureInfo.InvariantCulture, out result[j]);
            }
            return result;
        }

        private static Matrix2D BuildOp(string name, float[] a)
        {
            switch (name)
            {
                case "matrix":
                    if (a.Length >= 6)
                        return new Matrix2D { a = a[0], b = a[1], c = a[2], d = a[3], e = a[4], f = a[5] };
                    break;
                case "translate":
                    return Matrix2D.Translate(a.Length > 0 ? a[0] : 0f, a.Length > 1 ? a[1] : 0f);
                case "scale":
                    if (a.Length == 1) return Matrix2D.Scale(a[0], a[0]);
                    if (a.Length >= 2) return Matrix2D.Scale(a[0], a[1]);
                    break;
                case "rotate":
                    if (a.Length == 1) return Matrix2D.Rotate(a[0]);
                    if (a.Length >= 3)
                    {
                        // rotate(angle, cx, cy) = T(cx,cy) * R(angle) * T(-cx,-cy)
                        var t1 = Matrix2D.Translate(a[1], a[2]);
                        var r = Matrix2D.Rotate(a[0]);
                        var t2 = Matrix2D.Translate(-a[1], -a[2]);
                        return Matrix2D.Multiply(Matrix2D.Multiply(t1, r), t2);
                    }
                    break;
                case "skewX": return Matrix2D.Skew(a.Length > 0 ? a[0] : 0f, 0f);
                case "skewY": return Matrix2D.Skew(0f, a.Length > 0 ? a[0] : 0f);
            }
            return Matrix2D.Identity;
        }
    }
}
