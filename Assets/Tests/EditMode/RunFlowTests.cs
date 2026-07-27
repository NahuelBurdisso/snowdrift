using NUnit.Framework;
using Snowdrift.Core;

namespace Snowdrift.Tests
{
    public class RunFlowTests
    {
        [Test]
        public void StartsInTitle()
        {
            Assert.AreEqual(RunPhase.Title, new RunFlow().Phase);
        }

        [Test]
        public void StartRun_FromTitle_EntersRiding()
        {
            var f = new RunFlow();
            Assert.IsTrue(f.StartRun());
            Assert.AreEqual(RunPhase.Riding, f.Phase);
        }

        [Test]
        public void StartRun_WhileRiding_Rejected()
        {
            var f = new RunFlow();
            f.StartRun();
            Assert.IsFalse(f.StartRun());
            Assert.AreEqual(RunPhase.Riding, f.Phase);
        }

        [Test]
        public void NotifyCrash_OnlyFromRiding()
        {
            var f = new RunFlow();
            Assert.IsFalse(f.NotifyCrash(), "crash from Title must be rejected");
            f.StartRun();
            Assert.IsTrue(f.NotifyCrash());
            Assert.AreEqual(RunPhase.Crashed, f.Phase);
            Assert.IsFalse(f.NotifyCrash(), "double crash must be rejected");
        }

        [Test]
        public void ShowResults_OnlyFromCrashed()
        {
            var f = new RunFlow();
            Assert.IsFalse(f.ShowResults());
            f.StartRun();
            Assert.IsFalse(f.ShowResults());
            f.NotifyCrash();
            Assert.IsTrue(f.ShowResults());
            Assert.AreEqual(RunPhase.Results, f.Phase);
            Assert.IsFalse(f.ShowResults(), "double results must be rejected");
        }

        [Test]
        public void FullLoop_TitleRideCrashResultsRideAgain()
        {
            var f = new RunFlow();
            Assert.IsTrue(f.StartRun());
            Assert.IsTrue(f.NotifyCrash());
            Assert.IsTrue(f.ShowResults());
            Assert.IsTrue(f.StartRun(), "restart from Results must be allowed");
            Assert.AreEqual(RunPhase.Riding, f.Phase);
        }

        [Test]
        public void StartRun_FromCrashed_SkipsResults()
        {
            var f = new RunFlow();
            f.StartRun();
            f.NotifyCrash();
            Assert.IsTrue(f.StartRun());
            Assert.AreEqual(RunPhase.Riding, f.Phase);
        }
    }
}
