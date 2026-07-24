using UnityEngine;

namespace Snowdrift.Core
{
    public static class CarveMath
    {
        // Damps sideways velocity; redirects a fraction of the removed magnitude into forward.
        // Never increases total speed (spec: only slope accelerates you).
        public static Vector3 ApplyGrip(Vector3 vel, Vector3 boardForward, float grip, float redirect, float dt)
        {
            var fwd = boardForward.normalized;
            float fwdMag = Vector3.Dot(vel, fwd);
            Vector3 side = vel - fwd * fwdMag;
            float damp = 1f - Mathf.Exp(-grip * dt);
            Vector3 removed = side * damp;
            Vector3 newSide = side - removed;
            float sign = fwdMag >= 0f ? 1f : -1f;
            float newFwd = fwdMag + sign * removed.magnitude * Mathf.Clamp01(redirect);
            Vector3 result = fwd * newFwd + newSide;
            // Hard guarantee: clamp to original speed.
            float mag = result.magnitude;
            if (mag > vel.magnitude && mag > 1e-6f) result *= vel.magnitude / mag;
            return result;
        }

        // Quadratic drag past soft cap.
        public static Vector3 ApplyDrag(Vector3 vel, float softCap, float dragK, float dt)
        {
            float speed = vel.magnitude;
            if (speed <= softCap || speed < 1e-6f) return vel;
            float over = speed - softCap;
            float newSpeed = Mathf.Max(softCap, speed - dragK * over * over * dt);
            return vel * (newSpeed / speed);
        }

        // Lean toward target with asymmetric rates. Attack applies when leaning harder
        // OR reversing edge (sign change) — edge-to-edge transitions must stay snappy.
        public static float StepLean(float current, float target, float attack, float release, float dt)
        {
            bool attacking = Mathf.Abs(target) > Mathf.Abs(current)
                          || (target != 0f && current != 0f && Mathf.Sign(target) != Mathf.Sign(current));
            return Mathf.MoveTowards(current, target, (attacking ? attack : release) * dt);
        }
    }
}
