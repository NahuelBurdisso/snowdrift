namespace Snowdrift.Core
{
    public class DistanceTracker
    {
        public float TrueDistance { get; private set; }

        /// <summary>
        /// Advance the tracked distance by <paramref name="meters"/>.
        /// Callers are responsible for passing non-negative values (TerrainStream
        /// already clamps its delta), but a guard is applied here as a defensive
        /// contract: negative or zero input is silently ignored.
        /// </summary>
        public void Advance(float meters)
        {
            if (meters <= 0f) return;
            TrueDistance += meters;
        }

        public void OnOriginShift() { /* true distance unaffected by design; hook kept for symmetry/logging */ }
        public void Reset() => TrueDistance = 0f;
    }
}
