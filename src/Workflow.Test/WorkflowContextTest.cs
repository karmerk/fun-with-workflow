using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Workflow.Test
{
    [TestClass]
    public class WorkflowContextTest
    {
        [TestMethod]
        public async Task QueryAsync_ThrowsCancellationExceptionIfTokenCancellationIsRequested()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var context = new WorkflowContext(cts.Token, null);

                Assert.AreEqual(0, context.GetProgress().Count);
                var exception = await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => context.QueryAsync("key", () => Task.FromResult(true)));

                Assert.AreEqual(0, context.GetProgress().Count);
            }
        }

        [TestMethod]
        public async Task NoCancelableQueryAsync_DoesNotThrowExceptionIfTokenCancellationIsRequested()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var context = new WorkflowContext(cts.Token, null);

                Assert.AreEqual(0, context.GetProgress().Count);
                var value = await context.NoCancelableQueryAsync("key", () => Task.FromResult(true));
                Assert.IsTrue(value);
                Assert.AreEqual(1, context.GetProgress().Count);
            }
        }

        [TestMethod]
        public async Task GetProgress_MustContainTheKeysAndValuesFromTheQueryCalls()
        {
            var context = new WorkflowContext(CancellationToken.None, null);

            Assert.AreEqual(0, context.GetProgress().Count);
            
            _ = await context.QueryAsync("a", () => Task.FromResult("a"));
            _ = await context.QueryAsync("b", () => Task.FromResult("b"));
            _ = await context.QueryAsync("c", () => Task.FromResult("c"));
            _ = await context.QueryAsync("d", () => Task.FromResult("d"));

            Assert.AreEqual(4, context.GetProgress().Count);

            var expected = new KeyValuePair<string, string>[] {
                KeyValuePair.Create("a", "\"a\""),
                KeyValuePair.Create("b", "\"b\""),
                KeyValuePair.Create("c", "\"c\""),
                KeyValuePair.Create("d", "\"d\""),
            };

            CollectionAssert.AreEquivalent(expected, context.GetProgress().ToList());
        }

        [TestMethod]
        public async Task QueryAsync_SubsequentlyCallsWithSameKeyIsNotCalled()
        {
            var context = new WorkflowContext(CancellationToken.None, null);

            Assert.AreEqual(1337, await context.QueryAsync("key", () => Task.FromResult(1337)));
            Assert.AreEqual(1337, await context.QueryAsync("key", () =>
            {
                Assert.Fail("Should not get here");
                return Task.FromResult(42);
            }));
        }

        [TestMethod]
        public async Task QueryAsync_SubsequentlyCallsReturnTheFirstResult()
        {
            var context = new WorkflowContext(CancellationToken.None, null);

            Assert.AreEqual(1337, await context.QueryAsync("key", () => Task.FromResult(1337)));
            Assert.AreEqual(1337, await context.QueryAsync("key", () => Task.FromResult(1)));
            Assert.AreEqual(1337, await context.QueryAsync("key", () => Task.FromResult(2)));
            Assert.AreEqual(1337, await context.QueryAsync("key", () => Task.FromException<int>(new InvalidOperationException("Exception occurred"))));
        }

        [TestMethod]
        public async Task QueryAsync_CalledInParallelDoOnlyExecuteOneCall()
        {
            var context = new WorkflowContext(CancellationToken.None, null);

            long count = 0;

            var tasks = Enumerable.Range(0, 100)
                .Select(_ => context.QueryAsync("key", () => Task.FromResult(Interlocked.Increment(ref count))))
                .ToArray();

            await Task.WhenAll(tasks);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, context.GetProgress().Count);
        }

        [TestMethod]
        public async Task QueryAsync_QueryingWithDifferentTypeInSubsequentlyCallsThrowsJsonException()
        {
            var context = new WorkflowContext(CancellationToken.None, null);

            Assert.AreEqual(1337, await context.QueryAsync("key", () => Task.FromResult(1337)));
            
            _ = await Assert.ThrowsExceptionAsync<JsonException>(() => context.QueryAsync("key", () => Task.FromResult("string")));
            _ = await Assert.ThrowsExceptionAsync<JsonException>(() => context.QueryAsync("key", () => Task.FromResult(new ComplexType(1337, "1337"))));
        }

        private record ComplexType(int Value, string Text);

        
    }
}
