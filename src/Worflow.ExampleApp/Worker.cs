using Workflow;

namespace Worflow.ExampleApp
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var work = new Dictionary<ImportantBusinessWorkflow, Task<WorkflowStatus>>();

            foreach (var status in await GetWorkflows())
            {
                var workflow = new ImportantBusinessWorkflow(_logger); // Should be newed from IoC container
                var workflowTask = Workflow.Workflow.ContinueAsync<ImportantBusinessWorkflow, ImportantBusinessWorkflow.Request>(workflow, status, stoppingToken);

                work.Add(workflow, workflowTask);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                // Pop new requsts
                var requests = await GetPendingRequestsAsync(stoppingToken);

                foreach (var request in requests)
                {
                    var workflow = new ImportantBusinessWorkflow(_logger); // Should be newed from IoC container
                    var workflowTask = Workflow.Workflow.StartNewAsync(workflow, request, stoppingToken);

                    work.Add(workflow, workflowTask);
                }
                
                await Task.Delay(1000);
            }

            var results = await Task.WhenAll(work.Select(x=>x.Value).ToArray());

            await UpsertWorkflows(results);

            var workflows = work.Select(x => x.Key).ToList();

            _logger.LogInformation("Completed workflows");
            foreach (var workflow in workflows.Where(x => x.Completed))
            {
                _logger.LogInformation($"   {workflow.TransactionId} => {workflow.Total}");
            }
            _logger.LogInformation("Incompleted workflows");
            foreach (var workflow in workflows.Where(x => !x.Completed))
            {
                _logger.LogInformation($"   {workflow.TransactionId}");
            }
        }

        private Random _random = new Random();
        private async Task<ImportantBusinessWorkflow.Request[]> GetPendingRequestsAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            return new ImportantBusinessWorkflow.Request[]
            {
                new ImportantBusinessWorkflow.Request(Guid.NewGuid(), _random.Next(3,7), _random.Next(10,110)),
                new ImportantBusinessWorkflow.Request(Guid.NewGuid(), _random.Next(12,17), _random.Next(3,37)),
                new ImportantBusinessWorkflow.Request(Guid.NewGuid(), _random.Next(7,14), _random.Next(4,56)),
            };
        }

        private async Task UpsertWorkflows(IEnumerable<WorkflowStatus> statuses)
        {
            await Task.Yield();
        }

        private async Task<WorkflowStatus[]> GetWorkflows()
        {
            await Task.Yield();

            return new WorkflowStatus[0];
        }
    }
}

