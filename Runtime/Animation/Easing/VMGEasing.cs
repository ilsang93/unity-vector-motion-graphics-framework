using UnityEngine;

namespace VMG.Animation
{
    internal static class VMGEasing
    {
        const int k_NewtonIterations = 4;
        const float k_NewtonMinSlope = 1e-4f;

        public static float Evaluate(Vector2 outP, Vector2 inP, float normT)
        {
            if (normT <= 0f) return 0f;
            if (normT >= 1f) return 1f;

            float p1x = Mathf.Clamp01(outP.x);
            float p2x = Mathf.Clamp01(inP.x);

            float t = SolveT(normT, p1x, p2x);
            return BezierY(t, outP.y, inP.y);
        }

        static float SolveT(float x, float p1x, float p2x)
        {
            float t = x;
            for (int i = 0; i < k_NewtonIterations; i++)
            {
                float currentX = BezierX(t, p1x, p2x) - x;
                float currentSlope = BezierDX(t, p1x, p2x);
                if (Mathf.Abs(currentSlope) < k_NewtonMinSlope) break;
                t -= currentX / currentSlope;
            }
            return Mathf.Clamp01(t);
        }

        // Cubic Bezier with P0=(0,0), P3=(1,1).
        static float BezierX(float t, float p1x, float p2x)
        {
            float omt = 1f - t;
            return 3f * omt * omt * t * p1x + 3f * omt * t * t * p2x + t * t * t;
        }

        static float BezierY(float t, float p1y, float p2y)
        {
            float omt = 1f - t;
            return 3f * omt * omt * t * p1y + 3f * omt * t * t * p2y + t * t * t;
        }

        static float BezierDX(float t, float p1x, float p2x)
        {
            float omt = 1f - t;
            return 3f * omt * omt * p1x + 6f * omt * t * (p2x - p1x) + 3f * t * t * (1f - p2x);
        }
    }
}
