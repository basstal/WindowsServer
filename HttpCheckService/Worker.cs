using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HttpCheckService;

namespace HttpCheckService
{
        
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IEnumerable<IManageableApplication> _applications;

        public Worker(ILogger<Worker> logger, IEnumerable<IManageableApplication> applications)
        {
            _logger = logger;
            _applications = applications;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Application Monitoring Service is starting...");

            if (!_applications.Any())
            {
                _logger.LogWarning("No manageable applications registered. The service will do nothing.");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug("Checking status of all registered applications...");

                foreach (var app in _applications)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        _logger.LogDebug("Checking {ApplicationName}...", app.Name);
                        if (!await app.IsRunningAsync())
                        {
                            _logger.LogWarning("{ApplicationName} is not running. Attempting to start...", app.Name);
                            app.Start();
                        }
                        else
                        {
                            _logger.LogInformation("{ApplicationName} is running correctly.", app.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An error occurred while managing {ApplicationName}: {Message}", app.Name, ex.Message);
                    }
                }

                try
                {
                    // Wait for a configured interval before checking again.
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // This is expected when the service is stopping.
                    break;
                }
            }

            _logger.LogInformation("Application Monitoring Service is shutting down.");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Application Monitoring Service is stopping...");

            foreach (var app in _applications.Reverse())
            {
                try
                {
                    _logger.LogInformation("Stopping {ApplicationName}...", app.Name);
                    app.Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping {ApplicationName}: {Message}", app.Name, ex.Message);
                }
            }

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("Application Monitoring Service has stopped.");
        }
    }
}
