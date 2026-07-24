using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
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

        float _trickTimer, _multPulse;
        float _lastMult = 1f;

        void Awake()
        {
            tricks.OnTrickLanded += HandleTrickLanded;
        }

        void OnDestroy()
        {
            if (tricks != null) tricks.OnTrickLanded -= HandleTrickLanded;
        }

        void HandleTrickLanded(TrickResult r, LandingGrade grade, float airtime)
        {
            if (grade == LandingGrade.Clean && r.Total > 0)
            { trickText.text = $"{TrickSystem.Describe(r)}  +{r.Total}"; _trickTimer = 1.5f; }
            else if (grade == LandingGrade.Sloppy)
            { trickText.text = "Sloppy!"; _trickTimer = 1f; }
        }

        void Update()
        {
            var logic = score.Logic;
            scoreText.text = logic.TotalScore.ToString("N0");
            multText.text = $"x{logic.Multiplier:0.0}";
            if (logic.Multiplier > _lastMult) _multPulse = 0.3f;
            _lastMult = logic.Multiplier;
            _multPulse -= Time.deltaTime;
            multText.transform.localScale = Vector3.one * (1f + Mathf.Max(0f, _multPulse) * 0.8f);

            distText.text = $"{terrain.Distance.TrueDistance:0} m";
            speedBar.fillAmount = Mathf.Clamp01(board.Velocity.magnitude / tuning.tuckSpeedCap);

            _trickTimer -= Time.deltaTime;
            trickText.enabled = _trickTimer > 0f;
            pauseText.enabled = GameState.I.Paused;
            if (pauseText.enabled)
            {
                pauseText.text = $"PAUSED\nBest: {PlayerPrefs.GetInt("best_score", 0):N0}  {PlayerPrefs.GetFloat("best_dist", 0f):0}m\n[Esc] resume  [R] restart  [Q] quit";
                if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
                    Application.Quit();
            }
        }
    }
}
