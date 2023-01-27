using Hoard2;

IHost host = Host.CreateDefaultBuilder(args)
	.ConfigureServices(services =>
	{
		services.AddHostedService<Worker>();
	})
	.Build();

HoardMain.HoardHost = host;
await host.RunAsync();
