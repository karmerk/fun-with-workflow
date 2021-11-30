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

    Task<T> ExecuteAsync<T>(string checkpoint, Func<Task<T>> func) where T : notnull;

    void SetCompleted();
}


public sealed class WorkflowStatus
{
    public bool IsCompleted { get; }
    
    internal ReadOnlyDictionary<string, string> State { get; }

    internal WorkflowStatus(bool isCompleted, bool isCanceled, ReadOnlyDictionary<string, string> state)
    {
        
        IsCompleted = isCompleted;
        State = state;
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
            context.SetCheckpoint("_WORKFLOW_START_", argument);

            await workflow.RunAsync(argument, context);
        }
        catch(OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // NOP
        }

        return new WorkflowStatus(context.IsCompleted, cancellationToken.IsCancellationRequested, context.State);
    }

    // could it make sense to let the StartNew and Continue methods new the workflow objects to better to ensure the state?
    // but this will also force this workflow to know about DI because i want the workflows to be able to use services.
    // maybe that should be another thing that wraps all workflows and manages them
    public static async Task<WorkflowStatus> ContinueAsync<T, TArgument>(T workflow, WorkflowStatus status, CancellationToken cancellationToken) where T : IWorkflow<TArgument> where TArgument : notnull
    {
        await Task.Yield();

        var context = new WorkflowContext(cancellationToken, status.State.ToDictionary(x => x.Key, x => x.Value));

        try
        {
            if(!context.DoesCheckpointExist("_WORKFLOW_START_", out TArgument argument))
            {
                throw new InvalidOperationException("_WORKFLOW_START_ must be present to continue existing workflow");
            }

            await workflow.RunAsync(argument, context);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // NOP
        }

        return new WorkflowStatus(context.IsCompleted, cancellationToken.IsCancellationRequested, context.State);
    }
    
}


internal sealed class WorkflowContext : IWorkflowContext
{
    public bool IsReplaying { get; private set; }
    public bool IsCompleted { get; private set; }

    private CancellationToken _cancellationToken;

    // Could drop the concurrent collection
    private readonly ConcurrentDictionary<string, string> _state = new ConcurrentDictionary<string, string>();
    private readonly ConcurrentBag<string> _session = new ConcurrentBag<string>();

    internal ReadOnlyDictionary<string, string> State => new ReadOnlyDictionary<string, string>(_state);

    internal WorkflowContext(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;

        IsReplaying = false;
    }

    internal WorkflowContext(CancellationToken cancellationToken, Dictionary<string, string> state)
    {
        _cancellationToken = cancellationToken;

        IsReplaying = true;

        foreach (var kv in state)
        {
            _state.TryAdd(kv.Key, kv.Value);
        }
    }

    internal bool DoesCheckpointExist<T>(string checkpoint, out T value) where T : notnull
    {
        // TODO rethink this session check thing
        if (_session.Contains(checkpoint))
        {
            throw new InvalidOperationException($"Cant call {nameof(ExecuteAsync)} with the same {nameof(checkpoint)} during the same session");
        }
        _session.Add(checkpoint);

        if (_state.TryGetValue(checkpoint, out var json))
        {
            IsReplaying = true;
            value = System.Text.Json.JsonSerializer.Deserialize<T>(json)!;
            return true;
        }

        IsReplaying = false;

        value = default!;
        return false;
    }

    internal void SetCheckpoint<T>(string checkpoint, T value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        var add = _state.TryAdd(checkpoint, json);
        if (!add)
        {
            throw new InvalidOperationException($"Can not add the same checkpoint twice: Checkpoint={checkpoint}");
        }
    }

    public T Execute<T>(string checkpoint, Func<T> func) where T : notnull
    {
        _cancellationToken.ThrowIfCancellationRequested();

        if(DoesCheckpointExist<T>(checkpoint, out var value))
        {
            return value;
        }

        value = func();

        SetCheckpoint(checkpoint, value);

        _cancellationToken.ThrowIfCancellationRequested();

        return value;
    }

    public async Task<T> ExecuteAsync<T>(string checkpoint, Func<Task<T>> func) where T : notnull
    {
        _cancellationToken.ThrowIfCancellationRequested();

        if (DoesCheckpointExist<T>(checkpoint, out var value))
        {
            return value;
        }

        value = await func();

        SetCheckpoint(checkpoint, value);

        _cancellationToken.ThrowIfCancellationRequested();

        return value;
    }

    

    public void SetCompleted()
    {
        IsCompleted = true;
    }
}


public sealed class MyWorkflow : IWorkflow<int>
{
    public async Task RunAsync(int argument, IWorkflowContext context)
    {
        await Task.Yield();

        // Todo, could we grab the state from line number is that to crazy ? what if someone needs to fix a bug ?
        var value = await context.ExecuteAsync("1", () => GetSomethingFromExternalServiceAsync(argument));

    }

    private Task<int> GetSomethingFromExternalServiceAsync(int argument)
    {
        return Task.FromResult(argument+1);
    }
}