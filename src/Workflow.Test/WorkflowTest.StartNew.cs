using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Threading.Tasks;

namespace Workflow.Test
{
    public partial class WorkflowTest
    {
        [TestClass]
        public class StartNew
        {
            [TestMethod]
            public async Task StartNew_MustBeNonCompletedAndMustContainExactlyOneProgress()
            {
                using (var cts = new CancellationTokenSource())
                {
                    cts.Cancel();

                    var workflow = new IncrementValueWorkflow();
                    var status = await Workflow.StartNewAsync(workflow, 1337, cts.Token);

                    Assert.IsFalse(status.IsCompleted);
                    Assert.AreEqual(1, status.Progress.Count);
                }
            }


            [TestMethod]
            public async Task StartNew_RunsToCompletionWhenNotCanceled()
            {
                var workflow = new IncrementValueWorkflow();
                var status = await Workflow.StartNewAsync(workflow, 42, CancellationToken.None);

                Assert.IsTrue(status.IsCompleted);
                Assert.AreEqual(3, status.Progress.Count);

                Assert.AreEqual(1, workflow.CallsToIncrement);
                Assert.AreEqual(43, workflow.Result);
            }

            internal sealed class IncrementValueWorkflow : IWorkflow<int>
            {
                public int Result { get; private set; }

                public async Task RunAsync(int argument, IWorkflowContext context)
                {
                    var value = await context.QueryAsync("+1", () => Increment(argument));

                    await context.CommandAsync("+2", () => Task.Delay(5));

                    Result = value;
                }

                public int CallsToIncrement { get; private set; }
                public Task<int> Increment(int value)
                {
                    CallsToIncrement++;

                    return Task.FromResult(value + 1);
                }
            }
        }
    }
}
