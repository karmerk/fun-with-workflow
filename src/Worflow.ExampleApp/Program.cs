using Worflow.ExampleApp;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddLogging(configure => configure.AddConsole());
        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();
