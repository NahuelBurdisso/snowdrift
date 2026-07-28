using UnityEngine;
using System.Collections;
using Snowdrift.Core;

namespace Snowdrift.Game
{
    public class GameFeel : MonoBehaviour
    {
        public BoardController board;
        public TrickSystem tricks;
        public BoardTuning tuning;
        public TerrainStream terrain;
        public ParticleSystem carveSpray, landBurst, speedStreaks;
        public TrailRenderer trail;
        public UnityEngine.Rendering.Volume postVolume; // has a Vignette override
        public AudioSource windLoop, carveLoop, oneShots, musicLoop;
        public AudioClip landThump, crashClip, chimeClip;
        public float speedLinesFrom = 20f;

        bool _hitstopping;
        UnityEngine.Rendering.Universal.Vignette _vignette;

        void Awake()
        {
            if (postVolume != null) postVolume.profile.TryGet(out _vignette);
            tricks.OnTrickLanded += HandleTrickLanded;
            board.OnCrashed += HandleCrashed;
            terrain.OnOriginShifted += HandleOriginShifted;
            if (carveLoop != null && !carveLoop.isPlaying) carveLoop.Play();
            if (windLoop != null && !windLoop.isPlaying) windLoop.Play();

            // Restore persisted audio settings
            AudioListener.volume = PlayerPrefs.GetFloat("vol_master", 1f);
            if (musicLoop != null)
            {
                musicLoop.mute = PlayerPrefs.GetInt("music_on", 1) == 0;
                if (!musicLoop.isPlaying) musicLoop.Play();
            }
        }

        void OnDestroy()
        {
            if (tricks != null) tricks.OnTrickLanded -= HandleTrickLanded;
            if (board != null) board.OnCrashed -= HandleCrashed;
            if (terrain != null) terrain.OnOriginShifted -= HandleOriginShifted;
        }

        void HandleTrickLanded(TrickResult r, LandingGrade grade, float airtime)
        {
            if (grade != LandingGrade.Clean) return;
            landBurst.Play();
            if (landThump != null && oneShots != null) oneShots.PlayOneShot(landThump, Mathf.Clamp01(0.3f + airtime * 0.4f));
            if (chimeClip != null && oneShots != null && r.Total > 0) oneShots.PlayOneShot(chimeClip, 0.5f);
            if (airtime > tuning.minAirtimeForHitstop) StartCoroutine(Hitstop(0.04f));
        }

        void HandleCrashed()
        {
            if (crashClip != null && oneShots != null) oneShots.PlayOneShot(crashClip, 0.9f);
        }

        void HandleOriginShifted(Vector3 delta)
        {
            trail.Clear();
            carveSpray.Clear(); landBurst.Clear(); speedStreaks.Clear(); // world-space particles would pop 2km
        }

        IEnumerator Hitstop(float seconds)
        {
            if (_hitstopping || GameState.I.Paused) yield break;
            _hitstopping = true;
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(seconds);
            if (!GameState.I.Paused) Time.timeScale = 1f;
            _hitstopping = false;
        }

        void Update()
        {
            float speed = board.Velocity.magnitude;
            float speedFrac = Mathf.Clamp01(speed / tuning.softSpeedCap);

            // Carve spray scaled by sideways slip
            var em = carveSpray.emission;
            float slip = board.Airborne ? 0f
                : Vector3.ProjectOnPlane(board.Velocity, board.transform.forward).magnitude;
            em.rateOverTime = slip * 20f;

            // Speed lines above 20 m/s: streak particles + vignette creep
            float lines = Mathf.InverseLerp(speedLinesFrom, tuning.tuckSpeedCap, speed);
            var sem = speedStreaks.emission;
            sem.rateOverTime = lines * 60f;
            if (_vignette != null) _vignette.intensity.value = 0.15f + lines * 0.2f;

            if (windLoop != null)
            {
                windLoop.volume = Mathf.Lerp(0.1f, 0.8f, speedFrac) * (board.GrabHeld && board.Airborne ? 0.4f : 1f);
                windLoop.pitch = Mathf.Lerp(0.8f, 1.4f, speedFrac);
            }
            if (carveLoop != null) carveLoop.volume = board.Airborne ? 0f : Mathf.Clamp01(slip * 0.15f);
            trail.emitting = !board.Airborne && !board.Crashed;

            // Audio-only input (game-flow input stays in BoardController):
            // M toggles music, -/= adjust master volume. Persisted via PlayerPrefs.
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k != null)
            {
                if (k.mKey.wasPressedThisFrame && musicLoop != null)
                {
                    musicLoop.mute = !musicLoop.mute;
                    PlayerPrefs.SetInt("music_on", musicLoop.mute ? 0 : 1);
                }
                float dv = (k.equalsKey.wasPressedThisFrame ? 0.1f : 0f) - (k.minusKey.wasPressedThisFrame ? 0.1f : 0f);
                if (dv != 0f)
                {
                    AudioListener.volume = Mathf.Clamp01(AudioListener.volume + dv);
                    PlayerPrefs.SetFloat("vol_master", AudioListener.volume);
                }
            }
        }
    }
}
