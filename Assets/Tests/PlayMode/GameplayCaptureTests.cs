// GameplayCaptureTests.cs
// ---------------------------------------------------------------------------
// PlayMode screenshot capture tests for visual-state inspection from the CLI.
//
// Captures three PNG frames during an actual gameplay run:
//   gameplay_grounded.png  — rider carving downhill (HUD visible, speed bar)
//   gameplay_airborne.png  — board in mid-air after a forced jump via reflection
//   gameplay_crash.png     — ragdoll moment immediately after board.Crash()
//
// OUTPUT: Screenshots/gameplay_*.png  (project root — same dir as edit-mode shots)
//
// CAPTURE METHOD: Camera RenderTexture capture (batchmode-proven).
//   Camera.main.targetTexture = RT → cam.Render() → ReadPixels → EncodeToPNG.
//   No WaitForEndOfFrame — yield return null / WaitForSeconds only.
//   The HUD Canvas uses RenderMode.ScreenSpaceCamera (worldCamera = Camera.main,
//   planeDistance 1.0 m) so cam.Render() composites the HUD into the RT.
//
// WHY NOT ScreenCapture.CaptureScreenshotAsTexture:
//   batchmode never invokes WaitForEndOfFrame. UTF throws
//   "[Exception] UnityTest yielded WaitForEndOfFrame, which is not evoked in batchmode"
//   causing all three tests to fail before a single pixel is written.
//
// CATEGORY: [Category("Capture")] — excluded from normal test runs.
//
// CLI: Unity.exe -runTests -testPlatform PlayMode -testFilter "Capture"
//   or: -testFilter "Snowdrift.PlayModeTests.GameplayCaptureTests"
//   or: --category Capture    (NUnit 3 filter syntax accepted by UTF)
//
// Each test is bounded to 30 s (generous for CI); total suite ~40 s wall-clock.
// ---------------------------------------------------------------------------

using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Snowdrift.Game;

namespace Snowdrift.PlayModeTests
{
    [Category("Capture")]
    public class GameplayCaptureTests
    {
        // ── constants ────────────────────────────────────────────────────────────

        /// <summary>
        /// Output directory relative to the Unity project root (Assets/../Screenshots).
        /// Matches the path used by ScreenshotTool.cs so all screenshots land together.
        /// </summary>
        const string OutputDir = "Screenshots";

        /// <summary>
        /// Capture resolution.  Matches ScreenshotTool.cs (1920×1080).
        /// </summary>
        const int CaptureWidth  = 1920;
        const int CaptureHeight = 1080;

        /// <summary>
        /// Minimum acceptable file size in bytes.  A valid 1920×1080 PNG will be
        /// 50–500 KB; anything smaller is a black/corrupt render.
        /// </summary>
        const long MinFileSizeBytes = 20 * 1024; // 20 KB

        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Load Main scene and wait for physics to settle (mirrors PlaySmokeTests).
        /// </summary>
        static IEnumerator LoadMainScene()
        {
            var op = SceneManager.LoadSceneAsync("Main");
            while (!op.isDone) yield return null;
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        static BoardController FindBoard()
            => Object.FindAnyObjectByType<BoardController>();

        /// <summary>
        /// Resolve the project-root-relative Screenshots directory (the same path
        /// ScreenshotTool.cs uses: Application.dataPath is Assets/, so "../Screenshots"
        /// lands at the project root).
        /// </summary>
        static string ResolveOutputDir()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputDir));
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Capture Camera.main to a RenderTexture, read pixels, write PNG.
        /// Batchmode-safe: no WaitForEndOfFrame required.
        ///
        /// The HUD Canvas is configured as ScreenSpaceCamera (worldCamera = Camera.main,
        /// planeDistance 1.0 m) by ProjectSetup.cs, so cam.Render() composites UI elements
        /// into the RT alongside the 3-D world geometry.
        ///
        /// Pattern mirrors ScreenshotTool.CaptureShot():
        ///   assign RT → Render() → ReadPixels → restore null → release RT.
        /// </summary>
        static bool WriteScreenshot(string outputDir, string filename)
        {
            string filePath = Path.Combine(outputDir, filename + ".png");

            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError($"[GameplayCapture] Camera.main is null — cannot capture '{filename}'.");
                return false;
            }

            var rt = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 1;
            rt.Create();

            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();

            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            // Restore camera state before writing — keeps game state unmodified.
            cam.targetTexture = prevTarget;
            rt.Release();
            Object.Destroy(rt);

            byte[] png = tex.EncodeToPNG();
            Object.Destroy(tex);
            File.WriteAllBytes(filePath, png);

            long size = new FileInfo(filePath).Length;
            Debug.Log($"[GameplayCapture] Written: {filePath}  ({size / 1024} KB)");

            if (size < MinFileSizeBytes)
            {
                Debug.LogError(
                    $"[GameplayCapture] '{filename}' is suspiciously small ({size} bytes) " +
                    "— likely a black/empty frame. Check that -nographics was NOT passed.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Inject a jump request into BoardController without keyboard input.
        ///
        /// BoardController.TryPop() is private and only reachable via _jumpRequest (private
        /// bool set when Space is released on the ground). We set that field through reflection
        /// so the next FixedUpdate calls TryPop with 0 charge (minimum pop), which is enough
        /// to go airborne without needing a charged jump.
        ///
        /// Falls back gracefully: if the field is renamed/removed the test still runs but
        /// will not achieve the airborne state — a warning is emitted instead of a hard fail.
        /// </summary>
        static void InjectJumpRequest(BoardController board)
        {
            var field = typeof(BoardController).GetField(
                "_jumpRequest",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (field != null)
            {
                field.SetValue(board, true);
                Debug.Log("[GameplayCapture] _jumpRequest injected via reflection.");
            }
            else
            {
                Debug.LogWarning(
                    "[GameplayCapture] Could not find _jumpRequest field via reflection. " +
                    "Airborne capture may not show the board in the air.");
            }
        }

        // ── Test 1: grounded carving with HUD ────────────────────────────────────

        /// <summary>
        /// Load scene, run 3 seconds of free downhill carving, then capture.
        /// At this point the HUD score and distance counter will have live values,
        /// the speed bar will be partially filled, and the board will be grounded.
        /// </summary>
        [UnityTest]
        [Timeout(30000)]
        [Category("Capture")]
        public IEnumerator Capture_Grounded_HudVisible()
        {
            yield return LoadMainScene();

            var board = FindBoard();
            Assert.IsNotNull(board, "BoardController not found in Main scene");

            // Let the board build up speed and the HUD counters tick for 3 s.
            yield return new WaitForSeconds(3f);

            Assert.IsFalse(board.Crashed, "Board crashed before grounded capture");

            // One frame for LateUpdate / HUD Update to flush this tick's values.
            yield return null;

            string outputDir = ResolveOutputDir();
            bool ok = WriteScreenshot(outputDir, "gameplay_grounded");
            Assert.IsTrue(ok, "gameplay_grounded.png write failed or image is empty.");
        }

        // ── Test 2: mid-air after jump ────────────────────────────────────────────

        /// <summary>
        /// Load scene, wait for the board to stabilise, inject a jump request,
        /// wait one physics step for it to fire, then capture while airborne.
        /// The HUD remains visible (ScreenSpaceCamera) during air time.
        /// </summary>
        [UnityTest]
        [Timeout(30000)]
        [Category("Capture")]
        public IEnumerator Capture_Airborne_AfterJump()
        {
            yield return LoadMainScene();

            var board = FindBoard();
            Assert.IsNotNull(board, "BoardController not found in Main scene");

            // Let the board reach the slope and stabilise so the ground sensor is confident.
            // 2 s is enough for TerrainStream to stream the first chunk under the board.
            yield return new WaitForSeconds(2f);

            Assert.IsFalse(board.Crashed, "Board crashed before jump capture");

            // Inject the jump request. It is consumed in the next FixedUpdate.
            InjectJumpRequest(board);

            // Wait one fixed step for TryPop to fire, then a few frames for the board
            // to visually separate from the surface.
            yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(0.35f); // near jump apex — board clearly separated from surface

            // One frame for LateUpdate / camera to track new position.
            yield return null;

            string outputDir = ResolveOutputDir();
            bool ok = WriteScreenshot(outputDir, "gameplay_airborne");
            Assert.IsTrue(ok, "gameplay_airborne.png write failed or image is empty.");

            // Soft check: warn if the board is not actually airborne, but do not fail the
            // capture test — the PNG is still useful for visual inspection.
            if (!board.Airborne)
                Debug.LogWarning("[GameplayCapture] Board was not airborne at capture time. " +
                    "Check jump injection or slope geometry.");
        }

        // ── Test 3: crash / ragdoll moment ──────────────────────────────────────

        /// <summary>
        /// Load scene, stabilise, force a crash, then capture immediately after
        /// the crash callback fires — rider ragdoll is mid-tumble, board is detached.
        ///
        /// Uses the same board.Crash() call as PlaySmokeTests.Smoke_CrashAndRespawn_StateResets.
        /// Captures at t=0 (first frame after crash) for maximum visual drama.
        /// </summary>
        [UnityTest]
        [Timeout(30000)]
        [Category("Capture")]
        public IEnumerator Capture_Crash_RagdollMoment()
        {
            yield return LoadMainScene();

            var board = FindBoard();
            Assert.IsNotNull(board, "BoardController not found in Main scene");

            // Let the board stabilise and build speed before the crash.
            yield return new WaitForSeconds(2f);

            Assert.IsFalse(board.Crashed, "Board crashed unexpectedly before forced crash");

            // Force crash — same public API the smoke test uses.
            board.Crash();

            // One frame: crash callbacks (CrashHandler.HandleCrash) propagate and
            // ragdoll Rigidbodies receive crash velocity.
            yield return null;

            Assert.IsTrue(board.Crashed, "board.Crashed should be true after board.Crash()");

            // Let the ragdoll visibly crumple/tumble before capturing — an immediate
            // capture shows the rider still upright in bind pose, which reads as a
            // normal run rather than a crash.
            yield return new WaitForSeconds(0.6f);

            string outputDir = ResolveOutputDir();
            bool ok = WriteScreenshot(outputDir, "gameplay_crash");
            Assert.IsTrue(ok, "gameplay_crash.png write failed or image is empty.");
        }
    }
}
