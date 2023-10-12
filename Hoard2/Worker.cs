#pragma warning disable CS4014
namespace Hoard2
{
	public class Worker : BackgroundService
	{
		private readonly ILogger<Worker> _logger;

		public Worker(ILogger<Worker> logger)
		{
			_logger = logger;
			HoardMain.Logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation("Executing");
			if (!await HoardMain.Start())
			{
				HoardMain.StopWorker();
				return;
			}

			while (!stoppingToken.IsCancellationRequested)
				await Task.Delay(10, stoppingToken);
			await HoardMain.Shutdown();
		}

		public override async Task StartAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation("Starting");
			await HoardMain.Initialize(_logger, stoppingToken);
			await base.StartAsync(stoppingToken);
		}

		public override async Task StopAsync(CancellationToken cancellationToken)
		{
			await HoardMain.Shutdown();
			await base.StopAsync(cancellationToken);
		}
	}
}
