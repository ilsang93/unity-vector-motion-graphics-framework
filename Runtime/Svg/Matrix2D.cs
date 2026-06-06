using UnityEngine;

namespace VMG.Svg
{
    /// 2x3 affine transform: [ a c e ; b d f ]. Matches SVG transform order.
    internal struct Matrix2D
    {
        public float a, b, c, d, e, f;

        public static Matrix2D Identity => new Matrix2D { a = 1f, d = 1f };

        public bool IsIdentity => a == 1f && b == 0f && c == 0f && d == 1f && e == 0f && f == 0f;

        public Vector2 MultiplyPoint(Vector2 p)
        {
            return new Vector2(a * p.x + c * p.y + e, b * p.x + d * p.y + f);
        }

        public Vector2 MultiplyVector(Vector2 v)
        {
            return new Vector2(a * v.x + c * v.y, b * v.x + d * v.y);
        }

        public static Matrix2D Multiply(Matrix2D L, Matrix2D R)
        {
            return new Matrix2D
            {
                a = L.a * R.a + L.c * R.b,
                b = L.b * R.a + L.d * R.b,
                c = L.a * R.c + L.c * R.d,
                d = L.b * R.c + L.d * R.d,
                e = L.a * R.e + L.c * R.f + L.e,
                f = L.b * R.e + L.d * R.f + L.f,
            };
        }

        public static Matrix2D Translate(float tx, float ty) =>
            new Matrix2D { a = 1f, d = 1f, e = tx, f = ty };

        public static Matrix2D Scale(float sx, float sy) =>
            new Matrix2D { a = sx, d = sy };

        public static Matrix2D Rotate(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            return new Matrix2D { a = cs, b = sn, c = -sn, d = cs };
        }

        public static Matrix2D Skew(float xDeg, float yDeg)
        {
            return new Matrix2D
            {
                a = 1f, d = 1f,
                c = Mathf.Tan(xDeg * Mathf.Deg2Rad),
                b = Mathf.Tan(yDeg * Mathf.Deg2Rad),
            };
        }
    }
}
