using Workflow;

public sealed class ImportantBusinessWorkflow : IWorkflow<ImportantBusinessWorkflow.Request>
{
    public record Request(Guid TransactionId, int Count, int Size);
    
    private readonly ILogger _logger;
    public bool Completed { get; private set; }

    public Guid TransactionId { get; private set; }
    public decimal Total { get; private set; }

    public ImportantBusinessWorkflow(ILogger logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(Request request, IWorkflowContext context)
    {
        TransactionId = request.TransactionId;

        _logger.LogInformation($"Starting important business flow {request.TransactionId}");

        var result = await context.QueryAsync("GetCost", () => GetCostFromSlowExternalService(request.Count, request.Size, context.CancellationToken));

        await context.CommandAsync(nameof(SignalExternalPartyThatCostHasBeenCalculated), () => SignalExternalPartyThatCostHasBeenCalculated(request.TransactionId));

        await Task.Delay(4000, context.CancellationToken);

        var cost = result.Result;
        var tax = await context.QueryAsync("GetTax", () => CalculateTax(cost, context.CancellationToken));

        var command = new CompletedCommand(request.TransactionId, cost, tax);
        await context.CommandAsync("Completed", () => ExecuteCompletedCommand(command));

        _logger.LogInformation($"Important business flow {request.TransactionId} is completing");
        Total = cost + tax;
        Completed = true;
    }

    
    private record CostCalculatorResult(decimal Result);
    private async Task<CostCalculatorResult> GetCostFromSlowExternalService(int a, int b, CancellationToken cancellationToken)
    {
        // Simulate slow call to external cost calculation service
        await Task.Delay(5000, cancellationToken);

        return new CostCalculatorResult(a + b);
    }
    private async Task SignalExternalPartyThatCostHasBeenCalculated(Guid transactionId) => await Task.Delay(500);

    private async Task<decimal> CalculateTax(decimal value, CancellationToken cancellationToken)
    {
        // Simulate very advance tax calculation
        await Task.Delay(7500, cancellationToken);

        return value * 0.25m;
    }

    private record CompletedCommand(Guid TransactionId, decimal Cost, decimal Tax);
    private async Task ExecuteCompletedCommand(CompletedCommand command)
    {
        await Task.Delay(100);
        // Nop
    }
}

