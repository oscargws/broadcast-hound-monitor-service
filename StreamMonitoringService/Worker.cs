using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StreamMonitoringService
{
    public class Worker : BackgroundService
    {
        private readonly StreamMonitoringService _monitorService;
        private readonly ILogger<Worker> _logger;
        private Timer _timer;

        public Worker(StreamMonitoringService monitorService, ILogger<Worker> logger)
        {
            _monitorService = monitorService;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
            return Task.CompletedTask;
        }

        private void DoWork(object state)
        {
            _logger.LogInformation("Fetching and monitoring streams at: {time}", DateTimeOffset.Now);
            _monitorService.FetchAndMonitorStreamsAsync().Wait();
        }

        public override Task StopAsync(CancellationToken stoppingToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }
    }
}