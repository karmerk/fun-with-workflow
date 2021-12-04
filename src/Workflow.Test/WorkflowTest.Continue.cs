using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace Workflow.Test
{
    public partial class WorkflowTest
    {
        [TestClass]
        public class Continue
        {
            [TestMethod]
            public async Task ContinueAsync_MustFailIfContinuingANotStartedWorkflow()
            {
                var workflow = new IncrementValueWorkflow();

                var status = new WorkflowStatus(false, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string,string>()));

                var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Workflow.ContinueAsync<IncrementValueWorkflow, int>(workflow, status, CancellationToken.None));
            }

            [TestMethod]
            public async Task ContinueAsync()
            {
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    cts.Cancel();

                    var startNewWorkflow = new IncrementValueWorkflow();
                    var startNewStatus = await Workflow.StartNewAsync(startNewWorkflow, 42, cts.Token);
                    Assert.IsFalse(startNewStatus.IsCompleted);
                    Assert.AreEqual(0, startNewWorkflow.CallsToIncrement);

                    // Because work will be reapplied to the workflow object, we must use a new instance.
                    var continueWorkflow = new IncrementValueWorkflow();
                    var continueStatus = await Workflow.ContinueAsync<IncrementValueWorkflow, int>(continueWorkflow, startNewStatus, CancellationToken.None);
                    Assert.IsTrue(continueStatus.IsCompleted);
                    Assert.AreEqual(1, continueWorkflow.CallsToIncrement);

                    Assert.AreNotEqual(startNewStatus, continueStatus);

                    // startNewStatus remains the same
                    Assert.IsFalse(startNewStatus.IsCompleted);
                    Assert.AreEqual(0, startNewWorkflow.CallsToIncrement);
                }
            }

            internal sealed class IncrementValueWorkflow : IWorkflow<int>
            {
                public int Result { get; private set; }

                public async Task RunAsync(int argument, IWorkflowContext context)
                {
                    var value = await context.QueryAsync("+1", () => Increment(argument));

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
