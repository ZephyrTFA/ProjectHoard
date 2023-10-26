using Hoard2;

var host = Host.CreateDefaultBuilder(args)
    .UseSystemd()
    .ConfigureServices(services => { services.AddHostedService<Worker>(); })
    .Build();

HoardMain.HoardHost = host;
await host.RunAsync();