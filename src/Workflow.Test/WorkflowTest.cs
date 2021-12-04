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
    public partial class WorkflowTest
    {

        

        [TestMethod]
        public async Task ParallelWorkflow()
        {
            var workflow = new ParallelWorkflow();

            var status = await Workflow.StartNewAsync(workflow, 50, CancellationToken.None);

            Assert.IsTrue(status.IsCompleted);
            Assert.AreEqual(1 + 2 + 3, workflow.Result);

        }
    }

    


    internal sealed class ParallelWorkflow : IWorkflow<int>
    {
        public int Result { get; private set; }

        public async Task RunAsync(int argument, IWorkflowContext context)
        {
            var tasks = new Task<int>[]
            {
                context.QueryAsync("1", () => Wait(argument)),
                context.QueryAsync("2", () => Wait(argument)),
                context.QueryAsync("3", () => Wait(argument))
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
