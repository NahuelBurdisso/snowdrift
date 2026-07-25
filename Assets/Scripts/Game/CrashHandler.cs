using UnityEngine;
using System.Collections;

namespace Snowdrift.Game
{
    public class CrashHandler : MonoBehaviour
    {
        public BoardController board;
        public BoardTuning tuning;
        public Transform visual;          // "Visual" child (parent of BoardVisual + RiderVisual)
        public TerrainStream terrain;

        // Ragdoll state
        Animator _animator;
        Transform _riderVisual;           // RiderVisual under Visual
        Transform _boardVisual;           // BoardVisual under Visual
        Transform _visualRoot;            // same as visual field

        Rigidbody[] _ragdollBodies;       // all Rigidbodies under RiderVisual
        Collider[]  _ragdollColliders;    // all Colliders under RiderVisual (for pairwise ignore)

        // Bind-pose cache (restored on respawn)
        Vector3[]    _bindPos;
        Quaternion[] _bindRot;

        // Board bind pose
        Vector3    _boardBindLocalPos;
        Quaternion _boardBindLocalRot;

        // Board detach state
        Rigidbody _boardRb;
        bool      _crashed;

        // Player collider (to pairwise-ignore ragdoll vs player)
        Collider _playerCol;

        void Awake()
        {
            board.OnCrashed += HandleCrash;
            GameState.I.OnRestart += HandleRestart;
            _playerCol = board.GetComponent<Collider>();

            if (visual == null)
            {
                Debug.LogWarning("[CrashHandler] 'visual' is not assigned — ragdoll and board detach disabled.");
                return;
            }

            _visualRoot  = visual;
            _boardVisual = visual.Find("BoardVisual");
            _riderVisual = visual.Find("RiderVisual");

            if (_boardVisual != null)
            {
                _boardBindLocalPos = _boardVisual.localPosition;
                _boardBindLocalRot = _boardVisual.localRotation;
            }

            if (_riderVisual != null)
            {
                _animator = _riderVisual.GetComponent<Animator>();

                // Collect all Rigidbodies under RiderVisual (these are the ragdoll bones)
                _ragdollBodies    = _riderVisual.GetComponentsInChildren<Rigidbody>(true);
                _ragdollColliders = _riderVisual.GetComponentsInChildren<Collider>(true);

                // Cache bind pose
                if (_ragdollBodies != null && _ragdollBodies.Length > 0)
                {
                    _bindPos = new Vector3[_ragdollBodies.Length];
                    _bindRot = new Quaternion[_ragdollBodies.Length];
                    for (int i = 0; i < _ragdollBodies.Length; i++)
                    {
                        _bindPos[i] = _ragdollBodies[i].transform.localPosition;
                        _bindRot[i] = _ragdollBodies[i].transform.localRotation;
                    }
                }
            }
            else
            {
                Debug.LogWarning("[CrashHandler] RiderVisual not found under Visual — ragdoll disabled; will use fallback tumble.");
            }
        }

        void OnEnable()
        {
            if (terrain != null)
                terrain.OnOriginShifted += HandleOriginShift;
        }

        void OnDisable()
        {
            if (terrain != null)
                terrain.OnOriginShifted -= HandleOriginShift;
        }

        void OnDestroy()
        {
            board.OnCrashed -= HandleCrash;
            if (GameState.I != null) GameState.I.OnRestart -= HandleRestart;
        }

        // ── Crash ──────────────────────────────────────────────────────────────

        void HandleCrash()
        {
            if (_crashed) return;
            _crashed = true;

            // Velocity is still valid here (board zeros it AFTER firing OnCrashed)
            var crashVel = board.Velocity;

            bool hasRagdoll = _ragdollBodies != null && _ragdollBodies.Length > 0;

            if (hasRagdoll && _riderVisual != null)
            {
                // Disable animator so it stops overriding bone transforms
                if (_animator != null)
                    _animator.enabled = false;

                // Activate ragdoll and ignore collisions with player collider
                foreach (var rb in _ragdollBodies)
                {
                    rb.isKinematic   = false;
                    rb.linearVelocity = crashVel;
                }

                // Pairwise-ignore ragdoll colliders vs player collider so ragdoll
                // doesn't push the player root around, but ragdoll still hits terrain.
                // (Terrain chunks are on Ground layer; player is Default — ignoring
                //  Ragdoll vs Default would also suppress ragdoll-vs-terrain since
                //  rocks/trees are Default. Pairwise ignore is safer.)
                if (_playerCol != null)
                {
                    foreach (var rc in _ragdollColliders)
                        Physics.IgnoreCollision(rc, _playerCol, true);
                }
            }
            else
            {
                // Fallback: simplified tumble (original behaviour when prefab not built)
                FallbackTumble(crashVel);
                return;
            }

            // Detach board visual
            if (_boardVisual != null)
            {
                _boardVisual.SetParent(null, true);
                // Belt-and-braces guard: DestroyImmediate any leftover Rigidbody before adding
                // (can happen if crash+restart+re-crash hit same frame and Destroy was still deferred).
                if (!_boardVisual.TryGetComponent(out _boardRb))
                    _boardRb = _boardVisual.gameObject.AddComponent<Rigidbody>();
                _boardRb.linearVelocity = crashVel * 1.1f; // board shoots ahead
                _boardRb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.VelocityChange);
            }

            StartCoroutine(RespawnAfterDelay());
        }

        // ── Respawn ────────────────────────────────────────────────────────────

        void HandleRestart()
        {
            StopAllCoroutines();
            // DoRespawn handles both ragdoll and fallback paths; it guards _crashed itself.
            if (_crashed)
                DoRespawn(skipSpawnCall: true);
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(tuning.respawnDelay);
            if (!_crashed) yield break;
            DoRespawn(skipSpawnCall: false);
        }

        void DoRespawn(bool skipSpawnCall)
        {
            _crashed = false;

            bool hasRagdoll = _ragdollBodies != null && _ragdollBodies.Length > 0;

            if (hasRagdoll && _riderVisual != null)
            {
                // ── Ragdoll cleanup ──────────────────────────────────────────
                // Kinematic again
                foreach (var rb in _ragdollBodies)
                    rb.isKinematic = true;

                // Re-enable pairwise collision with player
                if (_playerCol != null)
                {
                    foreach (var rc in _ragdollColliders)
                        Physics.IgnoreCollision(rc, _playerCol, false);
                }

                // Restore bind pose for every ragdoll bone
                ResetLocalPose();

                // Re-enable animator
                if (_animator != null)
                {
                    _animator.enabled = true;
                    _animator.Rebind();
                }
            }
            else if (_tumbleBody != null)
            {
                // ── Fallback tumble cleanup (I-2) ────────────────────────────
                // Restore collision with board before destroying the collider
                if (_boardCol != null && _tumbleCol != null)
                    Physics.IgnoreCollision(_boardCol, _tumbleCol, false);

                DestroyImmediate(_tumbleCol);
                DestroyImmediate(_tumbleBody);
                _tumbleCol  = null;
                _tumbleBody = null;

                // Reparent visual back to its original parent/pose
                if (visual != null)
                {
                    visual.SetParent(_visualOriginalParent, false);
                    visual.localPosition = _visualOriginalLocalPos;
                    visual.localRotation = _visualOriginalLocalRot;
                }

                _visualOriginalParent = null;
            }

            // ── Reattach board (ragdoll path only — board not detached in fallback) ──
            if (_boardVisual != null)
            {
                if (_boardRb != null)
                {
                    DestroyImmediate(_boardRb); // immediate: no deferred-Destroy window for a re-crash
                    _boardRb = null;
                }
                _boardVisual.SetParent(_visualRoot, false);
                _boardVisual.localPosition = _boardBindLocalPos;
                _boardVisual.localRotation = _boardBindLocalRot;
            }

            if (!skipSpawnCall)
            {
                // M-1: null guard — terrain may be unassigned in test/offline scenes
                if (terrain == null)
                {
                    Debug.LogWarning("[CrashHandler] terrain is null — skipping CorridorSpawnNear, respawning at current position.");
                    board.Respawn(board.transform.position, board.transform.rotation);
                    return;
                }
                var pos = terrain.CorridorSpawnNear(board.transform.position);
                board.Respawn(pos, terrain.Field, terrain.OriginZOffset);
            }
        }

        void ResetLocalPose()
        {
            if (_ragdollBodies == null || _bindPos == null) return;
            for (int i = 0; i < _ragdollBodies.Length; i++)
            {
                if (_ragdollBodies[i] == null) continue;
                _ragdollBodies[i].transform.localPosition = _bindPos[i];
                _ragdollBodies[i].transform.localRotation = _bindRot[i];
            }
        }

        // ── Floating origin ────────────────────────────────────────────────────

        void HandleOriginShift(Vector3 delta)
        {
            // Ragdoll bones are children of RiderVisual which is under Visual → under Player root.
            // TerrainStream.OriginShift moves board.transform (the player root), so bones are
            // auto-shifted. Only the detached board and fallback tumble body need manual shifting.
            if (_boardRb != null)
                _boardRb.position += delta;

            // I-3: fallback tumble visual is detached from the player root, so shift it too.
            // (TerrainStream currently suppresses OriginShift while crashed, but guard for robustness.)
            if (_tumbleBody != null)
                _tumbleBody.position += delta;
        }

        // ── Fallback tumble (no ragdoll prefab) ────────────────────────────────

        Rigidbody _tumbleBody;
        CapsuleCollider _tumbleCol;
        Collider _boardCol;

        // Cached visual parent/pose so DoRespawn can restore after fallback tumble
        Transform  _visualOriginalParent;
        Vector3    _visualOriginalLocalPos;
        Quaternion _visualOriginalLocalRot;

        void FallbackTumble(Vector3 crashVel)
        {
            if (_tumbleBody != null) return;
            if (_boardCol == null) _boardCol = board.GetComponent<Collider>();

            if (visual != null)
            {
                // Cache before detach so DoRespawn can restore (covers HandleRestart path too)
                _visualOriginalParent   = visual.parent;
                _visualOriginalLocalPos = visual.localPosition;
                _visualOriginalLocalRot = visual.localRotation;

                visual.SetParent(null);
                _tumbleBody = visual.gameObject.AddComponent<Rigidbody>();
                _tumbleBody.linearVelocity = crashVel;
                _tumbleBody.AddTorque(Random.insideUnitSphere * 20f, ForceMode.VelocityChange);
                var col = visual.gameObject.AddComponent<CapsuleCollider>();
                col.height = 1.6f; col.radius = 0.35f; col.center = Vector3.up * 0.8f;
                _tumbleCol = col;
                if (_boardCol != null) Physics.IgnoreCollision(_boardCol, _tumbleCol, true);
                StartCoroutine(FallbackRespawnAfterDelay());
            }
        }

        IEnumerator FallbackRespawnAfterDelay()
        {
            yield return new WaitForSeconds(tuning.respawnDelay);
            // If HandleRestart already called DoRespawn, _tumbleBody was DestroyImmediate'd — bail out.
            if (_tumbleBody == null) yield break;
            // Delegate all cleanup to DoRespawn so there is exactly one cleanup path.
            DoRespawn(skipSpawnCall: false);
        }
    }
}
