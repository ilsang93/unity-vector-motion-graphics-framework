using UnityEngine;

namespace VMG.Animation.Core
{
    // Analytic spring solver. The spring equation
    //   m * x'' + c * x' + k * (x - 1) = 0,   x(0) = 0, x'(0) = v
    // has a closed-form solution for each damping regime, so Evaluate is a
    // few transcendental calls — no LUT, no ODE step. anime.js parity for
    // createSpring.
    //
    // Parameters mirror anime.js:
    //   stiffness (k)  : spring constant
    //   damping   (c)  : viscous damping coefficient
    //   mass      (m)
    //   velocity  (v)  : initial dx/dt at t=0
    //
    // Inputs are sanitized by VMGEase.Spring (k>0, m>0, c>=0). The solver
    // assumes those guarantees and does not re-clamp.
    internal static class VMGSpringSolver
    {
        // Target is "settled to within this fraction of 1.0". 0.005 = 0.5%,
        // visually indistinguishable from rest.
        const float k_SettleThreshold = 0.005f;
        // Absolute time cap to keep RecommendedDuration finite even when the
        // user picks pathological parameters (k tiny, c=0 → never settles).
        const float k_SettleMax = 10f;

        // x(τ) where τ ∈ [0,1] is the normalized progress through the
        // spring's settle time. Return value is x in [0, ~1+overshoot]; the
        // caller (Lerp) extrapolates beyond 1 for overshoot.
        public static float Evaluate(float stiffness, float damping, float mass, float velocity, float normT)
        {
            if (normT <= 0f) return 0f;
            float settle = SettleTime(stiffness, damping, mass, velocity);
            float t = normT * settle;
            return Position(stiffness, damping, mass, velocity, t);
        }

        // x(t) at absolute time t. 1 - error(t), where error has the closed
        // form for each ζ regime.
        static float Position(float stiffness, float damping, float mass, float velocity, float t)
        {
            float omega0 = Mathf.Sqrt(stiffness / mass);
            float zeta = damping / (2f * Mathf.Sqrt(stiffness * mass));

            float e;
            if (zeta < 1f)
            {
                float omegaD = omega0 * Mathf.Sqrt(1f - zeta * zeta);
                float decay = Mathf.Exp(-zeta * omega0 * t);
                float a = 1f;
                float b = (zeta * omega0 - velocity) / omegaD;
                e = decay * (a * Mathf.Cos(omegaD * t) + b * Mathf.Sin(omegaD * t));
            }
            else if (Mathf.Approximately(zeta, 1f))
            {
                float decay = Mathf.Exp(-omega0 * t);
                e = decay * (1f + (omega0 - velocity) * t);
            }
            else
            {
                float disc = omega0 * Mathf.Sqrt(zeta * zeta - 1f);
                float r1 = -zeta * omega0 + disc;
                float r2 = -zeta * omega0 - disc;
                // From e(0)=1, e'(0)=-v: A + B = 1, A*r1 + B*r2 = -v
                float A = (-velocity - r2) / (r1 - r2);
                float B = 1f - A;
                e = A * Mathf.Exp(r1 * t) + B * Mathf.Exp(r2 * t);
            }
            return 1f - e;
        }

        // Time at which |error(t)| drops below k_SettleThreshold and stays
        // there. The exponential envelope dominates the trig terms, so we
        // approximate by inverting the envelope. Capped to k_SettleMax.
        public static float SettleTime(float stiffness, float damping, float mass, float velocity)
        {
            float omega0 = Mathf.Sqrt(stiffness / mass);
            float zeta = damping / (2f * Mathf.Sqrt(stiffness * mass));

            float decayRate;
            if (zeta < 1f)
            {
                decayRate = zeta * omega0;
            }
            else if (Mathf.Approximately(zeta, 1f))
            {
                decayRate = omega0;
            }
            else
            {
                // Slowest mode dominates: r = -ζ*ω0 + ω0*sqrt(ζ²-1), which
                // for ζ>1 is the smaller-magnitude (slower) root.
                decayRate = zeta * omega0 - omega0 * Mathf.Sqrt(zeta * zeta - 1f);
            }

            if (decayRate <= 0f) return k_SettleMax;
            float t = -Mathf.Log(k_SettleThreshold) / decayRate;
            if (t > k_SettleMax) t = k_SettleMax;
            if (t < 0.01f) t = 0.01f;
            return t;
        }
    }
}
