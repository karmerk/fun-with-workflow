using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Workflow.Test
{
    [TestClass]
    public class WorkflowTest
    {
        [TestMethod]
        public async Task StartNew()
        {
            var workflow = new IncrementValueWorkflow();
            var status = await Workflow.StartNewAsync(workflow, 42, CancellationToken.None);

            Assert.IsTrue(status.IsCompleted);
            Assert.AreEqual(1, workflow.CallsToIncrement);
            Assert.AreEqual(43, workflow.Result);
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

        [TestMethod]
        public async Task ParallelWorkflow()
        {
            var workflow = new ParallelWorkflow();

            var status = await Workflow.StartNewAsync(workflow, 50, CancellationToken.None);

            Assert.IsTrue(status.IsCompleted);
            Assert.AreEqual(1 + 2 + 3, workflow.Result);

        }
    }

    internal sealed class IncrementValueWorkflow : IWorkflow<int>
    {
        public int Result { get; private set; }

        public async Task RunAsync(int argument, IWorkflowContext context)
        {
            var value = await context.ExecuteAsync("+1", () => Increment(argument));
            
            Result = value;
        }

        public int CallsToIncrement { get; private set; }
        public Task<int> Increment(int value)
        {
            CallsToIncrement++;

            return Task.FromResult(value + 1);
        }
    }


    internal sealed class ParallelWorkflow : IWorkflow<int>
    {
        public int Result { get; private set; }

        public async Task RunAsync(int argument, IWorkflowContext context)
        {
            var tasks = new Task<int>[]
            {
                context.ExecuteAsync("1", () => Wait(argument)),
                context.ExecuteAsync("2", () => Wait(argument)),
                context.ExecuteAsync("3", () => Wait(argument))
            };

            await Task.WhenAll(tasks);

            var value = tasks.Sum(x => x.Result);

            Result = value;
        }


        private long _callsToWait;
        public int CallsToWait
        {
            get
            {
                return (int)Interlocked.Read(ref _callsToWait);
            }
        }
        public async Task<int> Wait(int value)
        {
            var result = Interlocked.Increment(ref _callsToWait);
            await Task.Delay(value);

            return (int)result;
        }
    }
}
