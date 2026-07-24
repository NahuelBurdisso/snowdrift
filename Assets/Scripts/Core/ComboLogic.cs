using UnityEngine;

namespace Snowdrift.Core
{
    public class ComboLogic
    {
        float _passive;
        public float Multiplier { get; private set; } = 1f;
        public long TotalScore { get; private set; }

        public void OnCleanLanding(int trickScore)
        {
            TotalScore += Mathf.RoundToInt(trickScore * Multiplier);
            Multiplier = Mathf.Min(5f, Multiplier + 0.5f);
        }
        public void OnSloppyLanding() => Multiplier = 1f;
        public void OnCrash() => Multiplier = 1f;
        public void AddPassive(float amount)
        {
            _passive += amount;
            long whole = (long)_passive;
            if (whole > 0) { TotalScore += whole; _passive -= whole; }
        }
        public void Reset() { Multiplier = 1f; TotalScore = 0; _passive = 0f; }
    }
}
