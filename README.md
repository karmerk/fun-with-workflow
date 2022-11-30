# fun-with-workflow
Yeah what is workflows? Yes I dont know, but what i wanted to try out was to see if I could make some easier way to describe business flows.

I had the idea that it should be possible to write an entire business flow in one method - instead of having it chewed apart by multiple methods, maybe in different handlers, having to load up SAGA objects everywhere ensuring state and so on.

So what I have come up with, looks something like this:
```csharp
// IWorkflow<Request>.RunAsync
public async Task RunAsync(Request request, IWorkflowContext context)
{
  var aData = await context.Query("ServiceA", () => _serivceA.GetData(request.SomeArgument));
  
  await context.Command( "ServiceB-Wait", () => FavoriteDataTimeUtil.WaitUntil(aData.NextGetDeadline));
  
  var bData = await context.Query("ServiceB", () => _serviceB.GetData(request.SomeArgument));
  
  this.SomeState = aData.Value + bData.Value;
}
```

Edit: I thought about this some during the last year.. If the workflow is replayable (which is the idea), then the order command/query calls should also be constant, making the key ("ServiceA", "ServiceB-Wait" and "ServiceB" in the example above) unnessasary.


When the method runs out the workflow is completed. For it to work it requires the calls out to be 
wrapped in the ``context.Query`` or ``context.Command``, these methods are the ones that save the 
progress made inside the workflow - and reinjects this data when the workflow is continued later.

The whole idea is that if everything external is just data, then the computation inside the workflow
should always be the same. Yielding the same result when feed with the same data.

This is what makes it possible to ``Continue`` workflows, which is key for long running workflows.


The following example runs a workflow for 3 seconds and continues it afterwards. The ``StartNewAsync`` task will
swallow ``OperationCanceledException`` if they are coming from the ``cancellationToken``
```csharp
cancellationTokenSource.CancelAfter(3000);

var workflowObject = new MyWorkflow(...other dependencies...);
var status = await Workflow.StartNewAsync(workflowObject, request, cancellationTokenSource.Token);

if(!status.IsCompleted)
{
  // Should/must be new workflow instance to avoid state leaking from the other one
  var newWorkflowObject = new MyWorkflow(...other dependencies...);
  
  var ended = await Workflow.ContinueAsync<MyWorkflow, int>(newWorkflowObject, status, default);
  // ended.IsCompleted is now true, because no cancellationToken was passed as argument
}
```

Currently I have the most important internals working, which more or less boiles down to ``Workflow`` and ``IWorkflowContext``. 
Next step I think is to look into something to help managing starting, stopping and continuing workflows, as this currently requires
to much work - just look at my ``Worflow.ExampleApp`` :|

Other than that - its just experimenting and having fun.
