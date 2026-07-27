using UnityEngine;
using UnityEngine.UI;
using Snowdrift.Core;

namespace Snowdrift.Game
{
    /// <summary>
    /// Title and results overlays driven by GameState phase changes.
    /// Panels animate with unscaled time (game is frozen while they show).
    /// </summary>
    public class FlowUI : MonoBehaviour
    {
        public ComboScore score;
        public TerrainStream terrain;
        public GameObject titlePanel, resultsPanel;
        public Text titleBestText, titleHintText, resultsText, resultsHintText;

        void Awake()
        {
            GameState.I.OnPhaseChanged += Apply;
            Apply(GameState.I.Phase);
        }

        void OnDestroy()
        {
            if (GameState.I != null) GameState.I.OnPhaseChanged -= Apply;
        }

        void Apply(RunPhase phase)
        {
            titlePanel.SetActive(phase == RunPhase.Title);
            resultsPanel.SetActive(phase == RunPhase.Results);

            if (phase == RunPhase.Title)
            {
                int bestScore = PlayerPrefs.GetInt("best_score", 0);
                float bestDist = PlayerPrefs.GetFloat("best_dist", 0f);
                titleBestText.text = bestScore > 0 || bestDist > 0f
                    ? $"Best  {bestScore:N0}  ·  {bestDist:0} m"
                    : "";
            }
            else if (phase == RunPhase.Results)
            {
                long runScore = score.Logic.TotalScore;
                float runDist = terrain.Distance.TrueDistance;
                int prevBestScore = PlayerPrefs.GetInt("best_score", 0);
                float prevBestDist = PlayerPrefs.GetFloat("best_dist", 0f);
                bool newBest = runScore > prevBestScore || runDist > prevBestDist;
                GameState.SaveBest((int)System.Math.Min(runScore, int.MaxValue), runDist);

                resultsText.text =
                    "WIPEOUT\n\n" +
                    $"Score  {runScore:N0}   (best {System.Math.Max(runScore, prevBestScore):N0})\n" +
                    $"Distance  {runDist:0} m   (best {Mathf.Max(runDist, prevBestDist):0} m)" +
                    (newBest ? "\n\nNEW BEST!" : "");
            }
        }

        void Update()
        {
            // Pulse the active hint with unscaled time (timeScale is 0 on these screens)
            float a = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
            if (titlePanel.activeSelf) SetAlpha(titleHintText, a);
            if (resultsPanel.activeSelf) SetAlpha(resultsHintText, a);
        }

        static void SetAlpha(Text t, float a)
        {
            var c = t.color; c.a = a; t.color = c;
        }
    }
}
