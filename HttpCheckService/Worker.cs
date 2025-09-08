using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly string _targetIp = "192.168.31.182";
    private readonly int _targetPort = 9000;
    private readonly string _pythonServerDirectory = @"C:\Users\xj\Documents\HttpServer";
    private readonly string _pythonServerScript = @"C:\Users\xj\AppData\Local\Programs\Python\Python313\python.exe";
    private Process? _pythonProcess;
    private Task? _monitoringTask;
    private CancellationTokenSource? _monitoringCts;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Service is starting...");

        // 创建一个新的 CancellationTokenSource，链接到传入的 stoppingToken
        _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        try
        {
            // 启动监控任务并等待它完成
            _monitoringTask = MonitorHttpServiceAsync(_monitoringCts.Token);
            await _monitoringTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Service was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in service execution: {Message}", ex.Message);
            Log.CloseAndFlush();
            Environment.Exit(1);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Service is stopping...");

        if (_monitoringCts != null)
        {
            _monitoringCts.Cancel();
            if (_monitoringTask != null)
            {
                try
                {
                    // 等待监控任务完成，但设置超时
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _monitoringTask.WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Monitoring task was cancelled.");
                }
            }
        }

        if (_pythonProcess != null && !_pythonProcess.HasExited)
        {
            try
            {
                _logger.LogInformation("Stopping Python process...");
                _pythonProcess.Kill();
                _pythonProcess.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error stopping Python process: {ex.Message}");
            }
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task MonitorHttpServiceAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Checking HTTP service status...");

            if (await IsHttpServiceRunningAsync())
            {
                _logger.LogInformation("HTTP service is already running on port {Port}.", _targetPort);
            }
            else
            {
                _logger.LogWarning("No HTTP service found. Starting Python HTTP server...");
                StartPythonHttpServer();
            }

            try
            {
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Delay was cancelled.");
                break;
            }
        }
    }

    private async Task<bool> IsHttpServiceRunningAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var result = await client.GetAsync($"http://{_targetIp}:{_targetPort}");

            if (result.IsSuccessStatusCode)
            {
                _logger.LogInformation("HTTP service is running.");
                return true;
            }
        }
        catch (HttpRequestException)
        {
            _logger.LogError("HTTP service is not reachable.");
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("HTTP request timed out.");
        }

        return false;
    }

    private void StartPythonHttpServer()
    {
        try
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                _logger.LogInformation("Stopping existing Python process...");
                _pythonProcess.Kill();
                _pythonProcess.WaitForExit(5000);
            }

            _logger.LogInformation("Starting new Python HTTP server...");
            var startInfo = new ProcessStartInfo
            {
                FileName = _pythonServerScript,
                Arguments = $"-m http.server --bind {_targetIp} 9000",
                WorkingDirectory = _pythonServerDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _pythonProcess = Process.Start(startInfo);
            if (_pythonProcess == null)
            {
                throw new InvalidOperationException("Failed to start Python process");
            }

            // 设置进程退出事件处理
            _pythonProcess.EnableRaisingEvents = true;
            _pythonProcess.Exited += (sender, e) =>
            {
                _logger.LogWarning("Python HTTP server process exited unexpectedly");
            };

            // 启动输出重定向
            _pythonProcess.BeginOutputReadLine();
            _pythonProcess.BeginErrorReadLine();

            _logger.LogInformation("Started Python HTTP server at http://{0}:{1}.", _targetIp, _targetPort);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to start Python HTTP server: {ex.Message}");
            throw;
        }
    }
}
