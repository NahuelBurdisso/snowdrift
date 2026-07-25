using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Snowdrift.Game;
using Snowdrift.Core;

namespace Snowdrift.PlayModeTests
{
    /// <summary>
    /// PlayMode smoke tests that exercise the real Main scene at runtime.
    /// Each test loads the scene fresh; GameState is not DontDestroyOnLoad so a
    /// new instance is created each time via Awake().
    ///
    /// The EventSystem in Main.unity now uses InputSystemUIInputModule (new Input System)
    /// instead of the legacy StandaloneInputModule, so no frame-by-frame exceptions are
    /// expected in batchmode.
    /// </summary>
    public class PlaySmokeTests
    {

        // ── helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Load Main scene and wait for physics to settle (one FixedUpdate pass).
        /// Returns after scene is fully active.
        /// </summary>
        static IEnumerator LoadMainScene()
        {
            var op = SceneManager.LoadSceneAsync("Main");
            while (!op.isDone) yield return null;
            // Give Start() / Awake() one frame and one physics step to run
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        static BoardController FindBoard()
        {
            return Object.FindAnyObjectByType<BoardController>();
        }

        static CrashHandler FindCrashHandler()
        {
            return Object.FindAnyObjectByType<CrashHandler>();
        }

        static TerrainStream FindTerrainStream()
        {
            return Object.FindAnyObjectByType<TerrainStream>();
        }

        // ── Test 1 ─────────────────────────────────────────────────────────────

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Smoke_SceneRuns_NoErrors()
        {
            yield return LoadMainScene();

            var board = FindBoard();
            Assert.IsNotNull(board, "BoardController not found in Main scene");

            Vector3 startPos = board.transform.position;

            // Let the board slide downhill for 10 real seconds of game time
            yield return new WaitForSeconds(10f);

            // Board must still exist and not be in a crashed state
            Assert.IsNotNull(board, "BoardController destroyed during run");
            Assert.IsFalse(board.Crashed, "Board is crashed after 10s free run");

            // Should have moved; either velocity has magnitude > 1 m/s OR position changed > 3m
            float speed = board.Velocity.magnitude;
            float dist  = Vector3.Distance(board.transform.position, startPos);
            Assert.IsTrue(
                speed > 1f || dist > 3f,
                $"Board does not appear to be moving: speed={speed:F2} dist={dist:F2}");

            // Velocity must be finite (no NaN/Inf from physics blowup)
            Assert.IsTrue(
                float.IsFinite(board.Velocity.x) &&
                float.IsFinite(board.Velocity.y) &&
                float.IsFinite(board.Velocity.z),
                $"Board velocity is not finite: {board.Velocity}");
        }

        // ── Test 2 ─────────────────────────────────────────────────────────────

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Smoke_CrashAndRespawn_StateResets()
        {
            yield return LoadMainScene();

            var board        = FindBoard();
            var crashHandler = FindCrashHandler();
            Assert.IsNotNull(board,        "BoardController not found");
            Assert.IsNotNull(crashHandler, "CrashHandler not found");

            // Let the board stabilise on the slope before crashing it
            yield return new WaitForSeconds(2f);

            // Grab animator reference (may be null if no ragdoll prefab)
            // CrashHandler caches _animator internally; we find the Animator independently
            var visual      = board.transform.Find("Visual");
            Animator riderAnimator = null;
            if (visual != null)
            {
                var riderVisual = visual.Find("RiderVisual");
                if (riderVisual != null)
                    riderAnimator = riderVisual.GetComponent<Animator>();
            }

            // ── Crash ──
            board.Crash();

            // Wait one frame for crash callbacks to propagate
            yield return null;

            Assert.IsTrue(board.Crashed, "board.Crashed should be true immediately after Crash()");

            if (riderAnimator != null)
                Assert.IsFalse(riderAnimator.enabled, "Animator should be disabled after crash (ragdoll path)");

            // ── Respawn ──
            // respawnDelay default is 1s; add generous margin for CI timing
            float respawnDelay = 1f;
            if (crashHandler.tuning != null)
                respawnDelay = crashHandler.tuning.respawnDelay;

            yield return new WaitForSeconds(respawnDelay + 2f);

            // Core state reset
            Assert.IsFalse(board.Crashed, "board.Crashed should be false after respawn");

            // Velocity must be finite
            Assert.IsTrue(
                float.IsFinite(board.Velocity.x) &&
                float.IsFinite(board.Velocity.y) &&
                float.IsFinite(board.Velocity.z),
                $"board.Velocity is not finite after respawn: {board.Velocity}");

            // ── Ragdoll bones must be kinematic again (if ragdoll path) ──
            if (visual != null)
            {
                var riderVisual = visual.Find("RiderVisual");
                if (riderVisual != null)
                {
                    // Animator re-enabled
                    var anim = riderVisual.GetComponent<Animator>();
                    if (anim != null)
                        Assert.IsTrue(anim.enabled, "Animator should be re-enabled after respawn");

                    // All ragdoll Rigidbodies back to kinematic
                    var ragdollBodies = riderVisual.GetComponentsInChildren<Rigidbody>(true);
                    foreach (var rb in ragdollBodies)
                        Assert.IsTrue(rb.isKinematic,
                            $"Ragdoll bone '{rb.name}' is not kinematic after respawn");
                }

                // BoardVisual must be re-parented under Visual (not world root)
                var boardVisual = visual.Find("BoardVisual");
                if (boardVisual != null)
                {
                    Assert.AreEqual(visual, boardVisual.parent,
                        "BoardVisual parent should be Visual after respawn");
                    // Board visual Y local position should be near its bind pose (not blown out)
                    Assert.IsTrue(
                        Mathf.Abs(boardVisual.localPosition.y) < 1f,
                        $"BoardVisual localPosition.y looks wrong after respawn: {boardVisual.localPosition.y}");
                }
            }
        }

        // ── Test 3 ─────────────────────────────────────────────────────────────

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Smoke_PauseUnpause_TimeScaleAndMovement()
        {
            yield return LoadMainScene();

            var board = FindBoard();
            Assert.IsNotNull(board, "BoardController not found");

            // Let board build up some speed before pausing
            yield return new WaitForSeconds(2f);

            Assert.IsFalse(board.Crashed, "Board crashed unexpectedly before pause test");

            // ── Pause ──
            GameState.I.SetPaused(true);
            Assert.IsTrue(GameState.I.Paused, "GameState.Paused should be true after SetPaused(true)");
            Assert.AreEqual(0f, Time.timeScale, 1e-4f, "Time.timeScale should be 0 while paused");

            // Wait a wall-clock moment; board must not move while paused
            // WaitForSecondsRealtime is unaffected by timeScale
            yield return new WaitForSecondsRealtime(0.5f);

            Vector3 posWhilePaused = board.transform.position;

            // ── Unpause ──
            GameState.I.SetPaused(false);
            Assert.IsFalse(GameState.I.Paused, "GameState.Paused should be false after SetPaused(false)");
            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "Time.timeScale should be 1 after unpause");

            // Let board run again and verify it moves (proves physics resumed)
            yield return new WaitForSeconds(2f);

            Assert.IsFalse(board.Crashed, "Board crashed after unpause");

            float speed = board.Velocity.magnitude;
            float dist  = Vector3.Distance(board.transform.position, posWhilePaused);
            Assert.IsTrue(
                speed > 1f || dist > 1f,
                $"Board does not appear to be moving after unpause: speed={speed:F2} dist={dist:F2}");

            // Velocity must be finite
            Assert.IsTrue(
                float.IsFinite(board.Velocity.x) &&
                float.IsFinite(board.Velocity.y) &&
                float.IsFinite(board.Velocity.z),
                $"Board velocity is not finite after unpause: {board.Velocity}");

            // Clean up: restore timeScale in case teardown relies on it
            GameState.I.SetPaused(false);
        }

        // ── Test 4 ─────────────────────────────────────────────────────────────

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Smoke_RestartMidRun_Clean()
        {
            yield return LoadMainScene();

            var board = FindBoard();
            Assert.IsNotNull(board, "BoardController not found");

            // Let the board run for 3 seconds
            yield return new WaitForSeconds(3f);

            Assert.IsFalse(board.Crashed, "Board crashed unexpectedly before restart");

            // Trigger mid-run restart via GameState
            Assert.IsNotNull(GameState.I, "GameState.I is null before Restart()");
            GameState.I.Restart();

            // Wait for Regenerate + Respawn to settle (TerrainStream.Regenerate runs synchronously)
            yield return new WaitForFixedUpdate();
            yield return null;
            yield return new WaitForSeconds(2f);

            Assert.IsNotNull(board, "BoardController destroyed after Restart()");
            Assert.IsFalse(board.Crashed, "board.Crashed should be false after Restart()");

            // Velocity must be finite
            Assert.IsTrue(
                float.IsFinite(board.Velocity.x) &&
                float.IsFinite(board.Velocity.y) &&
                float.IsFinite(board.Velocity.z),
                $"board.Velocity is not finite after Restart(): {board.Velocity}");

            // After restart, TerrainStream respawns the board via CorridorSpawnNear(Vector3.zero).
            // The board should be on or near the slope (not fallen through the world).
            // We verify the board is not absurdly far below origin — if it is, something broke.
            Assert.IsTrue(
                board.transform.position.y > -100f,
                $"Board appears to have fallen through the world: y={board.transform.position.y:F1}");
        }

        // ── Test 5 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Verifies floating-origin shift correctness:
        ///   - Board world-z is reduced by the shift amount (lands near zero).
        ///   - Board velocity remains finite after the warp.
        ///   - DistanceTracker.TrueDistance is monotonically non-decreasing (not reset).
        ///   - No unexpected console errors are emitted.
        ///
        /// Strategy: set TerrainStream.shiftEvery to a low value (10 m) so the shift
        /// fires within a short run, without any reflection — shiftEvery is a public field.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Smoke_OriginShift_BoardAndDistanceTrackerConsistent()
        {
            yield return LoadMainScene();

            var board   = FindBoard();
            var terrain = FindTerrainStream();
            Assert.IsNotNull(board,   "BoardController not found");
            Assert.IsNotNull(terrain, "TerrainStream not found");

            // Let the board stabilise for one second before we arm the low threshold.
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(board.Crashed, "Board crashed during warm-up");

            // Sample distance before forcing the shift.
            float distanceBefore = terrain.Distance.TrueDistance;
            Assert.IsTrue(distanceBefore >= 0f, "TrueDistance should be non-negative before shift");

            // Lower the shift threshold so it fires as soon as board.z > 10 m.
            // The board should cross this in well under the test timeout.
            terrain.shiftEvery = 10f;

            // Wait until the shift fires (board.z will snap back near 0) or timeout.
            // We poll every physics step; give up to 15 real seconds.
            float elapsed = 0f;
            float boardZBeforeShift = board.transform.position.z;
            bool shiftObserved = false;

            while (elapsed < 15f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;

                // After the origin shift, board.z must be significantly smaller than it was
                // at the moment the threshold was armed.  A drop of > 5 m is unambiguous.
                if (boardZBeforeShift > 5f && board.transform.position.z < boardZBeforeShift - 5f)
                {
                    shiftObserved = true;
                    break;
                }
                // Update reference z only while board hasn't crossed the threshold yet
                if (board.transform.position.z <= terrain.shiftEvery)
                    boardZBeforeShift = board.transform.position.z;
            }

            Assert.IsTrue(shiftObserved,
                $"Origin shift was not observed within 15 s (board.z={board.transform.position.z:F1}, shiftEvery={terrain.shiftEvery})");

            // ── Post-shift assertions ──

            // 1. Board z is reduced: must be < shiftEvery (the board lands near 0).
            Assert.IsTrue(
                board.transform.position.z < terrain.shiftEvery,
                $"Board world-z not reduced after origin shift: z={board.transform.position.z:F2}");

            // 2. Velocity must be finite — no NaN/Inf from the position warp.
            Assert.IsTrue(
                float.IsFinite(board.Velocity.x) &&
                float.IsFinite(board.Velocity.y) &&
                float.IsFinite(board.Velocity.z),
                $"Board velocity is not finite after origin shift: {board.Velocity}");

            // 3. DistanceTracker must be monotonically non-decreasing.
            //    OnOriginShift() intentionally leaves TrueDistance untouched (by design),
            //    so distance after the shift must be >= distance before.
            float distanceAfter = terrain.Distance.TrueDistance;
            Assert.IsTrue(
                distanceAfter >= distanceBefore,
                $"TrueDistance decreased across origin shift: before={distanceBefore:F2} after={distanceAfter:F2}");

            // 4. Board must not have crashed during the shift.
            Assert.IsFalse(board.Crashed, "Board crashed during or after origin shift");

            // 5. No unexpected log errors during the entire test.
            LogAssert.NoUnexpectedReceived();
        }
    }
}
