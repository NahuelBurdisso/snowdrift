// DiagnosticRunTests.cs
// ---------------------------------------------------------------------------
// Long-run physics diagnostics for the "rider flies into the air" report.
//
// Logs board position/velocity/grounded state every second for 30 s of
// unattended riding plus periodic jump injections, and reports any overlap
// between ragdoll bone colliders and the player root collider (suspected
// depenetration source: Ragdoll layer collides with everything in the
// collision matrix).
//
// CATEGORY: [Category("Diag")] — excluded from normal test runs.
// CLI: -runTests -testPlatform PlayMode -testFilter "Snowdrift.PlayModeTests.DiagnosticRunTests"
// ---------------------------------------------------------------------------

using System.Collections;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Snowdrift.Game;

namespace Snowdrift.PlayModeTests
{
    [Category("Diag")]
    public class DiagnosticRunTests
    {
        static IEnumerator LoadMainScene()
        {
            var op = SceneManager.LoadSceneAsync("Main");
            while (!op.isDone) yield return null;
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        static void InjectJump(BoardController board)
        {
            var field = typeof(BoardController).GetField(
                "_jumpRequest", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(board, true);
        }

        /// <summary>
        /// Report every ragdoll collider whose bounds intersect the player root collider,
        /// and each one's penetration depth via Physics.ComputePenetration.
        /// </summary>
        static void LogRagdollOverlap(BoardController board)
        {
            var playerCol = board.GetComponent<Collider>();
            if (playerCol == null) { Debug.Log("[Diag] no player collider"); return; }

            var cols = board.GetComponentsInChildren<Collider>(true);
            int overlaps = 0;
            foreach (var c in cols)
            {
                if (c == playerCol) continue;
                if (c.gameObject.layer != LayerMask.NameToLayer("Ragdoll")) continue;
                bool pen = Physics.ComputePenetration(
                    c, c.transform.position, c.transform.rotation,
                    playerCol, playerCol.transform.position, playerCol.transform.rotation,
                    out var dir, out float dist);
                if (pen)
                {
                    overlaps++;
                    Debug.Log($"[Diag] OVERLAP {c.name} depth={dist:F3} dir={dir}");
                }
            }
            Debug.Log($"[Diag] ragdoll-vs-player overlaps: {overlaps} " +
                      $"(Ragdoll vs Default layer collision: {!Physics.GetIgnoreLayerCollision(LayerMask.NameToLayer("Ragdoll"), 0)})");
        }

        /// <summary>
        /// Side + front closeup of the rider at speed: manually reposition Camera.main
        /// immediately before a RenderTexture render (no frame advance, so Cinemachine
        /// cannot overwrite the transform). Writes Screenshots/diag_closeup_*.png.
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        [Category("Diag")]
        public IEnumerator Diag_Closeup_RiderAndBoard()
        {
            yield return LoadMainScene();
            var board = Object.FindAnyObjectByType<BoardController>();
            Assert.IsNotNull(board);

            yield return new WaitForSeconds(3f);

            string dir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "Screenshots"));
            System.IO.Directory.CreateDirectory(dir);

            var cam = Camera.main;
            var p = board.transform.position;

            cam.transform.position = p + board.transform.right * 3.5f + Vector3.up * 1.2f;
            cam.transform.LookAt(p + Vector3.up * 0.9f);
            CaptureTo(cam, System.IO.Path.Combine(dir, "diag_closeup_side.png"));

            cam.transform.position = p + board.transform.forward * 3.5f + Vector3.up * 1.2f;
            cam.transform.LookAt(p + Vector3.up * 0.9f);
            CaptureTo(cam, System.IO.Path.Combine(dir, "diag_closeup_front.png"));

            LogRiderSeatMetrics(board);
            Debug.Log("[Diag] closeups written");
        }

        /// <summary>
        /// Log runtime world-Y of humanoid feet, rider root, board visual and player root
        /// so the seating offset can be computed from real animated pose (edit-time bind
        /// bounds are meaningless once the Animator drives hips from pose clips).
        /// </summary>
        static void LogRiderSeatMetrics(BoardController board)
        {
            var visual = board.transform.Find("Visual");
            if (visual == null) { Debug.Log("[Diag] no Visual"); return; }
            var rider = visual.Find("RiderVisual");
            var boardVis = visual.Find("BoardVisual");
            float rootY = board.transform.position.y;
            Debug.Log($"[Diag] playerRootY={rootY:F3} visualY={visual.position.y:F3}");
            if (boardVis != null)
            {
                var brs = boardVis.GetComponentsInChildren<Renderer>(true);
                if (brs.Length > 0)
                {
                    var bb = brs[0].bounds;
                    foreach (var r in brs) bb.Encapsulate(r.bounds);
                    Debug.Log($"[Diag] boardVis y={boardVis.position.y:F3} deckTopY={bb.max.y:F3} rel={(bb.max.y - rootY):F3}");
                }
            }
            if (rider != null)
            {
                Debug.Log($"[Diag] riderVis localY={rider.localPosition.y:F3} worldY={rider.position.y:F3}");
                var anim = rider.GetComponent<Animator>();
                if (anim != null && anim.isHuman)
                {
                    var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                    var rf = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                    var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                    if (lf != null) Debug.Log($"[Diag] LeftFoot y={lf.position.y:F3} rel={(lf.position.y - rootY):F3}");
                    if (rf != null) Debug.Log($"[Diag] RightFoot y={rf.position.y:F3} rel={(rf.position.y - rootY):F3}");
                    if (hips != null) Debug.Log($"[Diag] Hips y={hips.position.y:F3} rel={(hips.position.y - rootY):F3}");
                }
                else Debug.Log($"[Diag] animator human={(anim != null ? anim.isHuman.ToString() : "no animator")}");
                var smrs = rider.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length > 0)
                {
                    var b = smrs[0].bounds;
                    foreach (var r in smrs) b.Encapsulate(r.bounds);
                    Debug.Log($"[Diag] riderMesh minY={b.min.y:F3} maxY={b.max.y:F3} relMin={(b.min.y - rootY):F3} relMax={(b.max.y - rootY):F3}");
                }
            }
        }

        static void CaptureTo(Camera cam, string path)
        {
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prev;
            rt.Release();
            Object.Destroy(rt);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        [UnityTest]
        [Timeout(90000)]
        [Category("Diag")]
        public IEnumerator Diag_LongRun_TrackAltitude()
        {
            yield return LoadMainScene();
            var board = Object.FindAnyObjectByType<BoardController>();
            Assert.IsNotNull(board);

            yield return new WaitForSeconds(1f);
            LogRagdollOverlap(board);

            var sb = new StringBuilder("[Diag] t | y | velY | speed | airborne | crashed\n");
            float maxY = float.MinValue; float maxVelY = float.MinValue;

            for (int t = 0; t <= 30; t++)
            {
                var p = board.transform.position;
                var v = board.Velocity;
                maxY = Mathf.Max(maxY, p.y);
                maxVelY = Mathf.Max(maxVelY, v.y);
                sb.AppendLine($"[Diag] {t,2}s | y={p.y,8:F2} | velY={v.y,7:F2} | spd={v.magnitude,6:F2} | air={board.Airborne} | crash={board.Crashed}");

                // Inject a jump every 6 s to mimic player input.
                if (t > 0 && t % 6 == 0 && !board.Crashed && !board.Airborne)
                    InjectJump(board);

                yield return new WaitForSeconds(1f);
            }

            Debug.Log(sb.ToString());
            Debug.Log($"[Diag] maxY={maxY:F2} maxVelY={maxVelY:F2}");
            LogRagdollOverlap(board);

            // The slope descends in +Z; terrain height near the corridor stays within
            // roughly ±40 m of spawn over 30 s. A rider launched by depenetration will
            // show runaway +velY / +y. Fail loudly so CI catches physics explosions.
            Assert.Less(maxVelY, 25f, "Vertical velocity exploded — physics depenetration launch.");
        }
    }
}
