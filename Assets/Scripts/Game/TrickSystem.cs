using UnityEngine;
using Snowdrift.Core;
using System;

namespace Snowdrift.Game
{
    public class TrickSystem : MonoBehaviour
    {
        public BoardController board;
        public BoardTuning tuning;
        public event Action<TrickResult, LandingGrade, float> OnTrickLanded; // result, grade, airtime
        readonly TrickTracker _tracker = new();

        void OnAirborneChanged(bool air) { if (air) _tracker.BeginAir(); }
        void OnLand(LandingGrade grade, float airtime) => OnTrickLanded?.Invoke(_tracker.EndAir(), grade, airtime);
        void OnCrashed() => _tracker.EndAir();

        void Awake()
        {
            board.OnAirborneChanged += OnAirborneChanged;
            board.OnLand += OnLand;
            board.OnCrashed += OnCrashed;
        }

        void OnDestroy()
        {
            board.OnAirborneChanged -= OnAirborneChanged;
            board.OnLand -= OnLand;
            board.OnCrashed -= OnCrashed;
        }

        void FixedUpdate()
        {
            if (!board.Airborne) return;
            _tracker.AddRotation(board.YawDegPerFix, board.PitchDegPerFix);
            if (board.GrabHeld) _tracker.TickGrab(Time.fixedDeltaTime, tuning.grabScorePerSec);
        }

        public static string Describe(TrickResult r)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (r.Spin180s > 0) parts.Add($"{r.Spin180s * 180}");
            if (r.Flips > 0) parts.Add(r.Flips == 1 ? "Flip" : $"{r.Flips}x Flip");
            if (r.GrabScore > 0) parts.Add("Grab");
            return string.Join(" + ", parts);
        }
    }
}
