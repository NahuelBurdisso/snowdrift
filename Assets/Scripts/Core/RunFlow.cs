namespace Snowdrift.Core
{
    public enum RunPhase { Title, Riding, Crashed, Results }

    /// <summary>
    /// Pure run-lifecycle state machine (no UnityEngine dependency).
    /// Title -> Riding -> Crashed -> Results -> Riding -> ...
    /// StartRun is also legal straight from Crashed (skip the results wait).
    /// </summary>
    public class RunFlow
    {
        public RunPhase Phase { get; private set; } = RunPhase.Title;

        /// <summary>Title/Crashed/Results -> Riding. Returns false if already Riding.</summary>
        public bool StartRun()
        {
            if (Phase == RunPhase.Riding) return false;
            Phase = RunPhase.Riding;
            return true;
        }

        /// <summary>Riding -> Crashed. Returns false from any other phase.</summary>
        public bool NotifyCrash()
        {
            if (Phase != RunPhase.Riding) return false;
            Phase = RunPhase.Crashed;
            return true;
        }

        /// <summary>Crashed -> Results. Returns false from any other phase.</summary>
        public bool ShowResults()
        {
            if (Phase != RunPhase.Crashed) return false;
            Phase = RunPhase.Results;
            return true;
        }
    }
}
