using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;

namespace Workflow;

public interface IWorkflow<T>
{
    Task RunAsync(T argument, IWorkflowContext context);
}

public interface IWorkflowContext
{
    public bool IsReplaying { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Query to get data into the workflow
    /// </summary>
    Task<T> QueryAsync<T>(string key, Func<Task<T>> func) where T : notnull;
    /// <summary>
    /// Command to send data out of the workflow
    /// </summary>
    Task CommandAsync(string key, Func<Task> func);
}


// Consider renaming this to WorkflowProgress ? WorkflowResult ?
public sealed class WorkflowStatus
{
    public bool IsCompleted { get; }

    internal ReadOnlyDictionary<string, string> Progress { get; }

    internal WorkflowStatus(bool isCompleted, ReadOnlyDictionary<string, string> progress)
    {
        IsCompleted = isCompleted;
        Progress = progress;
    }
}


public sealed class Workflow
{
    public static async Task<WorkflowStatus> StartNewAsync<T, TArgument>(T workflow, TArgument argument, CancellationToken cancellationToken) where T : IWorkflow<TArgument> where TArgument : notnull
    {
        await Task.Yield();

        var context = new WorkflowContext(cancellationToken);
        try
        {
            _ = await context.NoCancelableQueryAsync("_WORKFLOW_START_", () => Task.FromResult(argument));

            await workflow.RunAsync(argument, context);

            if (!cancellationToken.IsCancellationRequested)
            {
                // Is it possible we can infer this? a workflow should not return unless its completed
                return new WorkflowStatus(true, context.GetProgress());
            }
        }
        catch(OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // NOP
        }

        return new WorkflowStatus(false, context.GetProgress());
    }

    // could it make sense to let the StartNew and Continue methods new the workflow objects to better to ensure the state?
    // but this will also force this workflow to know about DI because i want the workflows to be able to use services.
    // maybe that should be another thing that wraps all workflows and manages them
    // this "other thing" could also handle persistency or at least facilitate it
    public static async Task<WorkflowStatus> ContinueAsync<T, TArgument>(T workflow, WorkflowStatus status, CancellationToken cancellationToken) where T : IWorkflow<TArgument> where TArgument : notnull
    {
        await Task.Yield();

        var context = new WorkflowContext(cancellationToken, status.Progress.ToDictionary(x => x.Key, x => x.Value));

        try
        {
            var argument = await context.QueryAsync("_WORKFLOW_START_", () => Task.FromException<TArgument>(new InvalidOperationException("_WORKFLOW_START_ must be present to continue existing workflow")));

            await workflow.RunAsync(argument, context);

            if (!cancellationToken.IsCancellationRequested)
            {
                // Is it possible we can infer this? a workflow should not return unless its completed
                return new WorkflowStatus(true, context.GetProgress());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // NOP
        }

        return new WorkflowStatus(false, context.GetProgress());
    }
}

// Context needs to be newed from a factory, because we need the option to replace the JsonSerializer and options
// Also need the option to make an abstraction on _actions (progress) so this could be something that persists the 
// data on the fly
internal sealed class WorkflowContext : IWorkflowContext
{
    public bool IsReplaying { get; private set; }
    public bool IsCompleted { get; private set; }

    public CancellationToken CancellationToken { get; }
    private readonly ConcurrentDictionary<string, Task<string>> _actions = new ConcurrentDictionary<string, Task<string>>();

    // .Result is :(
    internal ReadOnlyDictionary<string, string> GetProgress() => new ReadOnlyDictionary<string, string>(_actions.Where(x=>x.Value.IsCompletedSuccessfully).ToDictionary(x=>x.Key, x=>x.Value.Result));

    internal WorkflowContext(CancellationToken cancellationToken, Dictionary<string, string>? state = null)
    {
        CancellationToken = cancellationToken;

        if (state != null)
        {
            _actions = new ConcurrentDictionary<string, Task<string>>(state.Select(x => KeyValuePair.Create(x.Key, Task.FromResult(x.Value))));
        }

        IsReplaying = state?.Any() ?? false;
    }

    private async Task<string> QueryAsync<T>(Func<Task<T>> func)
    {
        var value = await func();
        var json = System.Text.Json.JsonSerializer.Serialize(value);

        IsReplaying = false;
        return json;
    }

    private async Task<string> CommandAsync(Func<Task> func)
    {
        await func();

        IsReplaying = false;
        return "{}"; // empty object
    }

    // ¯\_(ツ)_/¯
    internal async Task<T> NoCancelableQueryAsync<T>(string key, Func<Task<T>> func) where T : notnull
    {
        var json = await _actions.GetOrAdd(key, k => QueryAsync(func));

        // the initial get, gets an unneeded roundtrip to json

        var value = System.Text.Json.JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T)} from JSON: {json}");

        return value;
    }

    public async Task<T> QueryAsync<T>(string key, Func<Task<T>> func) where T : notnull
    {
        CancellationToken.ThrowIfCancellationRequested();

        // the initial add, gets an unneeded roundtrip to json
        var json = await _actions.GetOrAdd(key, k => QueryAsync(func));
        var value = System.Text.Json.JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T)} from JSON: {json}");

        CancellationToken.ThrowIfCancellationRequested();

        return value;
    }

    public async Task CommandAsync(string key, Func<Task> func)
    {
        CancellationToken.ThrowIfCancellationRequested();

        _ = await _actions.GetOrAdd(key, k => CommandAsync(func));

        CancellationToken.ThrowIfCancellationRequested();
    }


 
}


