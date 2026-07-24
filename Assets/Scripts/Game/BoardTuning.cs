using UnityEngine;

namespace Snowdrift.Game
{
    [CreateAssetMenu(menuName = "Snowdrift/BoardTuning")]
    public class BoardTuning : ScriptableObject
    {
        [Header("Carve")]
        public float carveGrip = 10f;          // 8-12/s per spec
        public float driftGrip = 2.5f;
        public float gripRedirect = 0.7f;      // fraction of removed sideways -> forward
        public float steerDegPerSec = 140f;
        public float leanAttack = 12f;         // ~80ms to full lean
        public float leanRelease = 5f;
        [Header("Speed")]
        public float softSpeedCap = 28f;
        public float tuckSpeedCap = 34f;
        public float dragK = 0.02f;
        [Header("Ground stick")]
        public float normalSmoothHz = 15f;
        public float stickForce = 30f;
        public float stickReleaseAngle = 30f;  // crest release threshold
        [Header("Jump")]
        public float jumpMin = 4.5f;
        public float jumpMax = 8f;
        public float chargeTime = 0.3f;
        public float coyoteTime = 0.1f;
        public float jumpBuffer = 0.1f;
        [Header("Alignment Rates")]
        public float groundAlignRate = 10f;      // slerp rate aligning board to surface on ground
        public float autoLandSlerpRate = 8f;     // slerp rate of auto-land assist
        public float autoLandMomentumDecay = 0.8f; // rotation momentum damp during assist
        [Header("Air")]
        public float spinDegPerSec = 420f;
        public float flipDegPerSec = 380f;
        public float airRotAccel = 8f;         // rotation momentum authority
        public float autoLandWindow = 0.25f;
        public float autoLandMaxAngle = 40f;
        [Header("Landing")]
        public float cleanDeg = 25f;
        public float sloppyDeg = 60f;
        public float sloppySpeedLoss = 0.4f;   // fraction of speed lost
        public float minAirtimeForHitstop = 0.5f;
        [Header("Crash")]
        public float obstacleCrashSpeed = 8f;
        public float respawnDelay = 1f;
        [Header("Scoring")]
        public float grabScorePerSec = 50f;
        public float speedTricklePerSec = 2f;  // at soft cap
        public float nearMissScore = 25f;
    }
}
