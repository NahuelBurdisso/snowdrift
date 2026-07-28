using UnityEngine;
using UnityEngine.UI;
using Snowdrift.Core;

namespace Snowdrift.Game
{
    public class HUD : MonoBehaviour
    {
        public ComboScore score;
        public TrickSystem tricks;
        public TerrainStream terrain;
        public BoardController board;
        public BoardTuning tuning;
        public Text scoreText, multText, distText, trickText, pauseText;
        public Image speedBar;

        static readonly Color GoldColor   = new(1f, 0.82f, 0.25f);
        static readonly Color SloppyColor = new(1f, 0.45f, 0.35f);
        static readonly Color CloseColor  = new(0.55f, 0.85f, 1f);

        float _trickTimer, _trickDur, _multPulse, _scorePunch;
        Color _trickColor = Color.white;
        float _lastMult = -1f;
        long _lastScore = -1;
        int _lastDist = -1;
        bool _wasPaused;

        void Awake()
        {
            tricks.OnTrickLanded += HandleTrickLanded;
            NearMissZone.Passed += HandleNearMiss;
            GameState.I.OnRestart += HandleRestart;
        }

        void OnDestroy()
        {
            if (tricks != null) tricks.OnTrickLanded -= HandleTrickLanded;
            NearMissZone.Passed -= HandleNearMiss;
            if (GameState.I != null) GameState.I.OnRestart -= HandleRestart;
        }

        void HandleRestart()
        {
            _trickTimer = 0f;
            _scorePunch = 0f;
            trickText.enabled = false;
        }

        void ShowPopup(string msg, float duration, Color color)
        {
            trickText.text = msg;
            _trickTimer = _trickDur = duration;
            _trickColor = color;
        }

        void HandleTrickLanded(TrickResult r, LandingGrade grade, float airtime)
        {
            if (grade == LandingGrade.Clean && r.Total > 0)
            { ShowPopup($"{TrickSystem.Describe(r)}  +{r.Total}", 1.5f, Color.white); _scorePunch = 0.25f; }
            else if (grade == LandingGrade.Sloppy)
            { ShowPopup("Sloppy!", 1f, SloppyColor); }
        }

        void HandleNearMiss()
        {
            // Don't stomp an active trick popup — near-miss is lower-priority feedback.
            if (_trickTimer <= 0f) ShowPopup("CLOSE!", 0.6f, CloseColor);
        }

        void Update()
        {
            var logic = score.Logic;
            long curScore = logic.TotalScore;
            if (curScore != _lastScore) { scoreText.text = curScore.ToString("N0"); _lastScore = curScore; }
            _scorePunch -= Time.deltaTime;
            scoreText.transform.localScale = Vector3.one * (1f + Mathf.Max(0f, _scorePunch) * 0.6f);

            float curMult = logic.Multiplier;
            if (curMult > _lastMult) _multPulse = 0.3f;
            if (curMult != _lastMult) { multText.text = $"x{curMult:0.0}"; _lastMult = curMult; }
            _multPulse -= Time.deltaTime;
            multText.transform.localScale = Vector3.one * (1f + Mathf.Max(0f, _multPulse) * 0.8f);
            // Multiplier heats up white -> gold as it climbs 1..5
            multText.color = Color.Lerp(Color.white, GoldColor, (curMult - 1f) / 4f);

            int curDist = Mathf.FloorToInt(terrain.Distance.TrueDistance);
            if (curDist != _lastDist) { distText.text = $"{curDist} m"; _lastDist = curDist; }
            speedBar.fillAmount = Mathf.Clamp01(board.Velocity.magnitude / tuning.tuckSpeedCap);

            _trickTimer -= Time.deltaTime;
            trickText.enabled = _trickTimer > 0f;
            if (trickText.enabled)
            {
                float age = _trickDur - _trickTimer;
                // Pop-in: overshoot scale for first 0.15s, then settle to 1
                float pop = age < 0.15f ? 1f + (1f - age / 0.15f) * 0.5f : 1f;
                trickText.transform.localScale = Vector3.one * pop;
                // Fade-out over last 0.3s
                float alpha = Mathf.Clamp01(_trickTimer / 0.3f);
                trickText.color = new Color(_trickColor.r, _trickColor.g, _trickColor.b, alpha);
            }
            bool paused = GameState.I.Paused;
            pauseText.enabled = paused;
            if (paused && !_wasPaused)
            {
                pauseText.text = $"PAUSED\nBest: {PlayerPrefs.GetInt("best_score", 0):N0}  {PlayerPrefs.GetFloat("best_dist", 0f):0}m\n[Esc] resume  [R] restart  [Q] quit\n[M] music  [-/=] volume";
            }
            // Q-quit handled by BoardController alongside R/Esc (single input owner).
            _wasPaused = paused;
        }
    }
}
