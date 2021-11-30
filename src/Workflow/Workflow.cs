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
}


// Consider renaming this to WorkflowProgress ? 
public sealed class WorkflowStatus
{
    public bool IsCompleted { get; }
    
    internal ReadOnlyDictionary<string, string> State { get; }

    internal WorkflowStatus(bool isCompleted, ReadOnlyDictionary<string, string> state)
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

            if (!cancellationToken.IsCancellationRequested)
            {
                // Is it possible we can infer this? a workflow should not return unless its completed
                return new WorkflowStatus(true, context.State);
            }
        }
        catch(OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // NOP
        }

        return new WorkflowStatus(false, context.State);
    }

    // could it make sense to let the StartNew and Continue methods new the workflow objects to better to ensure the state?
    // but this will also force this workflow to know about DI because i want the workflows to be able to use services.
    // maybe that should be another thing that wraps all workflows and manages them
    // this "other thing" could also handle persistency or at least facilitate it
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

            if (!cancellationToken.IsCancellationRequested)
            {
                // Is it possible we can infer this? a workflow should not return unless its completed
                return new WorkflowStatus(true, context.State);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // NOP
        }

        return new WorkflowStatus(false, context.State);
    }
}

// context needs to be newed from a factory, because we need the option to replace the JsonSerializer
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
        // TODO rethink this session check thing => maybe IWorkflowCallerGuard thing?
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

        // in order to better support parallel workflows, we need to have "reservations" of the checkpoint
        // so multiple tasks does not call the same checkpoint twice.
        // And also, should we allow multiple calls during the same session ? then multiple calls to same
        // checkpoint should just block and then the result could be sent to all callers ?
        if (DoesCheckpointExist<T>(checkpoint, out var value))
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
}


