using System.Globalization;
using UnityEngine;

namespace VMG.Svg
{
    internal static class SvgColorParser
    {
        public static bool TryParse(string s, out Color color)
        {
            color = Color.black;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s == "none" || s == "transparent") { color = new Color(0, 0, 0, 0); return true; }
            if (s.StartsWith("#")) return TryHex(s, out color);
            if (s.StartsWith("rgb")) return TryRgb(s, out color);
            return TryNamed(s, out color);
        }

        private static bool TryHex(string s, out Color color)
        {
            color = Color.black;
            string hex = s.Substring(1);
            if (hex.Length == 3 || hex.Length == 4)
            {
                // Short hex: each digit doubled.
                var sb = new System.Text.StringBuilder(hex.Length * 2);
                for (int i = 0; i < hex.Length; i++) { sb.Append(hex[i]); sb.Append(hex[i]); }
                hex = sb.ToString();
            }
            if (hex.Length != 6 && hex.Length != 8) return false;
            if (!byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)) return false;
            if (!byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)) return false;
            if (!byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b)) return false;
            byte aByte = 255;
            if (hex.Length == 8 && !byte.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out aByte)) return false;
            color = new Color32(r, g, b, aByte);
            return true;
        }

        private static bool TryRgb(string s, out Color color)
        {
            color = Color.black;
            int lp = s.IndexOf('(');
            int rp = s.IndexOf(')');
            if (lp < 0 || rp < lp) return false;
            bool hasAlpha = s.StartsWith("rgba");
            string inside = s.Substring(lp + 1, rp - lp - 1);
            var parts = inside.Split(',');
            if (parts.Length < (hasAlpha ? 4 : 3)) return false;
            if (!TryComponent(parts[0], out float r)) return false;
            if (!TryComponent(parts[1], out float g)) return false;
            if (!TryComponent(parts[2], out float b)) return false;
            float a = 1f;
            if (hasAlpha && !float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a)) return false;
            color = new Color(r, g, b, a);
            return true;
        }

        private static bool TryComponent(string s, out float v)
        {
            s = s.Trim();
            if (s.EndsWith("%"))
            {
                if (float.TryParse(s.Substring(0, s.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float p))
                {
                    v = Mathf.Clamp01(p / 100f); return true;
                }
                v = 0f; return false;
            }
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
            {
                v = Mathf.Clamp01(x / 255f); return true;
            }
            v = 0f; return false;
        }

        private static bool TryNamed(string s, out Color color)
        {
            switch (s.ToLowerInvariant())
            {
                case "black": color = Color.black; return true;
                case "white": color = Color.white; return true;
                case "red": color = Color.red; return true;
                case "green": color = new Color(0f, 0.5f, 0f); return true;
                case "blue": color = Color.blue; return true;
                case "yellow": color = Color.yellow; return true;
                case "cyan": color = Color.cyan; return true;
                case "magenta": color = Color.magenta; return true;
                case "gray":
                case "grey": color = Color.gray; return true;
                case "silver": color = new Color(0.75f, 0.75f, 0.75f); return true;
                case "lime": color = new Color(0f, 1f, 0f); return true;
                case "maroon": color = new Color(0.5f, 0f, 0f); return true;
                case "navy": color = new Color(0f, 0f, 0.5f); return true;
                case "olive": color = new Color(0.5f, 0.5f, 0f); return true;
                case "orange": color = new Color(1f, 0.647f, 0f); return true;
                case "purple": color = new Color(0.5f, 0f, 0.5f); return true;
                case "teal": color = new Color(0f, 0.5f, 0.5f); return true;
            }
            color = Color.black;
            return false;
        }
    }
}
