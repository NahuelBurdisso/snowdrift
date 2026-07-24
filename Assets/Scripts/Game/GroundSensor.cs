using UnityEngine;

namespace Snowdrift.Game
{
    public class GroundSensor : MonoBehaviour
    {
        public LayerMask groundMask;
        public float rayLength = 1.2f;
        public Vector2 footprint = new Vector2(0.3f, 0.8f); // half extents (x, z)
        public bool Grounded { get; private set; }
        public Vector3 SmoothedNormal { get; private set; } = Vector3.up;
        public float GroundDistance { get; private set; }

        public void Sense(float smoothHz)
        {
            int hits = 0; Vector3 nSum = Vector3.zero; float dMin = float.MaxValue;
            var t = transform;
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
            {
                var origin = t.position + t.right * (ix * footprint.x) + t.forward * (iz * footprint.y) + Vector3.up * 0.3f;
                if (Physics.Raycast(origin, Vector3.down, out var hit, rayLength + 0.3f, groundMask))
                { hits++; nSum += hit.normal; dMin = Mathf.Min(dMin, hit.distance - 0.3f); }
            }
            Grounded = hits >= 2; // tolerate seam/edge single-ray misses
            GroundDistance = hits > 0 ? dMin : float.MaxValue;
            if (hits > 0)
            {
                var target = (nSum / hits).normalized;
                float k = 1f - Mathf.Exp(-smoothHz * Time.fixedDeltaTime);
                SmoothedNormal = Vector3.Slerp(SmoothedNormal, target, k).normalized;
            }
        }

        // Predicted touchdown for auto-land assist: raycast down from board along velocity.
        public bool PredictLanding(Vector3 velocity, float window, out Vector3 normal)
        {
            normal = Vector3.up;
            var future = transform.position + velocity * window;
            if (Physics.Raycast(transform.position, (future - transform.position).normalized,
                out var hit, velocity.magnitude * window + 1f, groundMask))
            { normal = hit.normal; return true; }
            return false;
        }
    }
}
