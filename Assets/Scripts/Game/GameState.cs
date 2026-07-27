using UnityEngine;
using System;
using Snowdrift.Core;

namespace Snowdrift.Game
{
    public class GameState : MonoBehaviour
    {
        static GameState _i;
        // Lazy: safe against undefined Awake order (other components subscribe in their Awake).
        public static GameState I => _i != null ? _i : _i = FindAnyObjectByType<GameState>();
        public event Action OnRestart;
        public event Action<RunPhase> OnPhaseChanged;
        public bool Paused { get; private set; }

        readonly RunFlow _flow = new();
        public RunPhase Phase => _flow.Phase;

        void Awake()
        {
            _i = this;
            ApplyTimeScale(); // boot in Title: frozen until StartRun
        }

        void OnDestroy()
        {
            if (_i == this)
            {
                _i = null;
                // Scene may unload while frozen (Title/Results/pause) — never leak timeScale 0.
                Time.timeScale = 1f;
                AudioListener.pause = false;
            }
        }

        /// <summary>Begin a run from Title, Crashed or Results. From non-Title phases fires OnRestart first.</summary>
        public void StartRun()
        {
            bool fromTitle = _flow.Phase == RunPhase.Title;
            if (!_flow.StartRun()) return;
            Paused = false;
            if (!fromTitle)
                OnRestart?.Invoke(); // TerrainStream, ComboScore, HUD, CrashHandler reset in place
            ApplyTimeScale();
            OnPhaseChanged?.Invoke(Phase);
        }

        /// <summary>Riding -> Crashed (ragdoll plays out at normal speed).</summary>
        public void NotifyCrash()
        {
            if (_flow.NotifyCrash())
                OnPhaseChanged?.Invoke(Phase);
        }

        /// <summary>Crashed -> Results (freeze-frame + results panel).</summary>
        public void ShowResults()
        {
            if (!_flow.ShowResults()) return;
            ApplyTimeScale();
            OnPhaseChanged?.Invoke(Phase);
        }

        /// <summary>Mid-run reset (R while riding). Phase stays Riding.</summary>
        public void Restart()
        {
            SetPaused(false);
            OnRestart?.Invoke(); // TerrainStream, ComboScore, HUD reset in place; TerrainStream respawns the board
        }

        public void SetPaused(bool p)
        {
            Paused = p;
            ApplyTimeScale();
        }

        void ApplyTimeScale()
        {
            bool frozen = Paused || Phase == RunPhase.Title || Phase == RunPhase.Results;
            Time.timeScale = frozen ? 0f : 1f;
            AudioListener.pause = Paused; // wind keeps blowing on title/results screens
        }

        public static void SaveBest(int score, float distance)
        {
            try
            {
                if (score > PlayerPrefs.GetInt("best_score", 0)) PlayerPrefs.SetInt("best_score", score);
                if (distance > PlayerPrefs.GetFloat("best_dist", 0f)) PlayerPrefs.SetFloat("best_dist", distance);
                PlayerPrefs.Save();
            }
            catch { /* best-effort per spec */ }
        }
    }
}
