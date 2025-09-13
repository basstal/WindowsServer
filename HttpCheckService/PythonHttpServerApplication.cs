using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HttpCheckService
{
    public class PythonHttpServerApplication : IManageableApplication
    {
        private readonly ILogger<PythonHttpServerApplication> _logger;
        private readonly string _targetIp = "192.168.31.182";
        private readonly int _targetPort = 9001;
        private readonly string _pythonServerDirectory = @"C:\Users\xj\Documents\HttpServer";
        private readonly string _pythonServerScript = @"C:\Users\xj\AppData\Local\Programs\Python\Python313\python.exe";
        private Process? _pythonProcess;

        public string Name => "PythonHttpServer";

        public PythonHttpServerApplication(ILogger<PythonHttpServerApplication> logger)
        {
            _logger = logger;
        }

        public async Task<bool> IsRunningAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var result = await client.GetAsync($"http://{_targetIp}:{_targetPort}");

                if (result.IsSuccessStatusCode)
                {
                    _logger.LogDebug("HTTP service ({Name}) is running.", Name);
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                _logger.LogWarning("HTTP service ({Name}) is not reachable.", Name);
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("HTTP request to {Name} timed out.", Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking status of {Name}", Name);
            }

            return false;
        }

        public void Start()
        {
            _logger.LogInformation("Attempting to start {Name}...", Name);
            try
            {
                if (_pythonProcess != null && !_pythonProcess.HasExited)
                {
                    _logger.LogInformation("Stopping existing process for {Name}...", Name);
                    Stop();
                }

                _logger.LogInformation("Starting new process for {Name}...", Name);
                var startInfo = new ProcessStartInfo
                {
                    FileName = _pythonServerScript,
                    Arguments = $"-m http.server --bind {_targetIp} {_targetPort}",
                    WorkingDirectory = _pythonServerDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _pythonProcess = Process.Start(startInfo);
                if (_pythonProcess == null)
                {
                    throw new InvalidOperationException($"Failed to start process for {Name}");
                }

                _pythonProcess.EnableRaisingEvents = true;
                _pythonProcess.BeginOutputReadLine();
                _pythonProcess.BeginErrorReadLine();

                _pythonProcess.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) _logger.LogInformation("[{Name} STDOUT]: {Data}", Name, e.Data); };
                _pythonProcess.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) _logger.LogError("[{Name} STDERR]: {Data}", Name, e.Data); };
                _pythonProcess.Exited += (sender, e) => { _logger.LogWarning("Process for {Name} exited unexpectedly.", Name); };

                _logger.LogInformation("Started {Name} at http://{Ip}:{Port}.", Name, _targetIp, _targetPort);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start {Name}: {Message}", Name, ex.Message);
                throw;
            }
        }

        public void Stop()
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                try
                {
                    _logger.LogInformation("Stopping process for {Name}...", Name);
                    _pythonProcess.Kill(true); // Kill process and its entire tree
                    _pythonProcess.WaitForExit(5000);
                    _logger.LogInformation("Process for {Name} stopped.", Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping process for {Name}: {Message}", Name, ex.Message);
                }
                finally
                {
                    _pythonProcess.Dispose();
                    _pythonProcess = null;
                }
            }
        }
    }
} 