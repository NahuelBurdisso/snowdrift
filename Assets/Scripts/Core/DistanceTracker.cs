namespace Snowdrift.Core
{
    public class DistanceTracker
    {
        public float TrueDistance { get; private set; }
        public void Advance(float meters) => TrueDistance += meters;
        public void OnOriginShift() { /* true distance unaffected by design; hook kept for symmetry/logging */ }
        public void Reset() => TrueDistance = 0f;
    }
}
