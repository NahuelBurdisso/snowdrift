using UnityEngine;
using Snowdrift.Core;

namespace Snowdrift.Game
{
    public class ComboScore : MonoBehaviour
    {
        public BoardController board;
        public TrickSystem tricks;
        public BoardTuning tuning;
        public TerrainStream terrain;
        public ComboLogic Logic { get; } = new();

        void Awake()
        {
            tricks.OnTrickLanded += (r, grade, _) =>
            {
                if (grade == LandingGrade.Clean) { if (r.Total > 0) Logic.OnCleanLanding(r.Total); }
                else Logic.OnSloppyLanding();
            };
            board.OnCrashed += () => Logic.OnCrash();
            NearMissZone.Passed += OnNearMiss; // zones spawned by TerrainStream (Task 9)
            GameState.I.OnRestart += HandleRestart;
        }

        void HandleRestart()
        {
            GameState.SaveBest((int)System.Math.Min(Logic.TotalScore, int.MaxValue), terrain.Distance.TrueDistance);
            Logic.Reset();
        }

        void OnDestroy()
        {
            NearMissZone.Passed -= OnNearMiss;
            if (GameState.I != null) GameState.I.OnRestart -= HandleRestart;
        }

        void FixedUpdate()
        {
            if (board.Crashed) return;
            float speedFrac = board.Velocity.magnitude / tuning.softSpeedCap;
            Logic.AddPassive(speedFrac * tuning.speedTricklePerSec * Time.fixedDeltaTime);
        }

        public void OnNearMiss() => Logic.AddPassive(tuning.nearMissScore);

        void OnApplicationQuit() => GameState.SaveBest((int)System.Math.Min(Logic.TotalScore, int.MaxValue), terrain.Distance.TrueDistance);
    }
}
