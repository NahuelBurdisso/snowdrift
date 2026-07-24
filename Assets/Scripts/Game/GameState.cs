using UnityEngine;
using System;

namespace Snowdrift.Game
{
    public class GameState : MonoBehaviour
    {
        static GameState _i;
        // Lazy: safe against undefined Awake order (other components subscribe in their Awake).
        public static GameState I => _i != null ? _i : _i = FindAnyObjectByType<GameState>();
        public event Action OnRestart;
        public bool Paused { get; private set; }

        void Awake() { _i = this; }

        public void Restart()
        {
            SetPaused(false);
            OnRestart?.Invoke(); // TerrainStream, ComboScore, HUD reset in place; TerrainStream respawns the board
        }

        public void SetPaused(bool p)
        {
            Paused = p;
            Time.timeScale = p ? 0f : 1f;
            AudioListener.pause = p;
        }

        public static void SaveBest(long score, float distance)
        {
            try
            {
                if (score > PlayerPrefs.GetInt("best_score", 0)) PlayerPrefs.SetInt("best_score", (int)score);
                if (distance > PlayerPrefs.GetFloat("best_dist", 0f)) PlayerPrefs.SetFloat("best_dist", distance);
                PlayerPrefs.Save();
            }
            catch { /* best-effort per spec */ }
        }
    }
}
