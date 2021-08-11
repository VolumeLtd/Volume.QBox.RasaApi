using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volume.QBox.RasaApi.Interfaces;
using Volume.QBox.RasaApi.Models;

namespace Volume.QBox.RasaApi.Services
{
    public class ProcessService : IProcessService
    {
        private ConcurrentDictionary<string, RasaVersionModel> _rasaVersions = new ConcurrentDictionary<string, RasaVersionModel>();
        private ConcurrentDictionary<string, RasaProcess> _processes = new ConcurrentDictionary<string, RasaProcess>();
        private int _predictionTimeout;
        private int _processTimeout;
        private const int PAUSE_IN_MS = 250;
        private int gpu = 1;
        public string ActiveVersion { get; set; }
        private const string RASA_ACTION_TRAIN = "train";
        private const string GPU_0_PORT = "5005";
        private const string GPU_1_PORT = "5006";

        private readonly ILogger<ProcessService> _logger;

        private readonly IServiceProvider _serviceProvider;

        public ProcessService(ILogger<ProcessService> logger, IServiceProvider serviceProvider, IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _processTimeout = configuration.GetValue<int>("AppSettings:ProcessTimeout");
            _predictionTimeout = configuration.GetValue<int>("AppSettings:PredictionTimeout");
            _logger?.LogDebug("Process initiated {processTimeout} {predictionTimeout}", _processTimeout, _predictionTimeout);
        }

        public void StartProcess(string version, string modelName, string action, out bool newProcess)
        {
            CheckProcessesForTimeout();
            newProcess = false;

            if (!HasProcess(version, modelName, action))
            {
                _logger?.LogDebug("Creating new process for {modelName}, action is {action}", modelName, action);
                newProcess = true;
                CreateProcess(version, modelName, action);
            }
        }

        public async Task<RasaProcess> GetProcess(string version, string modelName, string action)
        {
            _processes.TryGetValue(GetProcessName(version, modelName, action), out RasaProcess rasaProcess);

            if (rasaProcess != null && !rasaProcess.InUse)
            {
                var watch = Stopwatch.StartNew();

                while (watch.Elapsed.TotalMinutes < this._predictionTimeout)
                {
                    await Task.Delay(PAUSE_IN_MS);

                    if (!rasaProcess.InUse)
                    {
                        return rasaProcess;
                    }
                }
                throw new Exception("Timeout while waiting for process to be available");
            }
            else
            {
                return rasaProcess;
            }
        }

        public RasaProcess CreateProcess(string version, string modelName, string action)
        {
            RasaVersionModel rasaVersion = GetRasaVersionModel(version);
            if (action == RASA_ACTION_TRAIN)
            {
                gpu = gpu == 0 ? 1 : 0;
            }

            RasaProcess rasaProcess = new RasaProcess(rasaVersion.ModelDir, rasaVersion.RasaDir, version, modelName, gpu, action, _predictionTimeout, (ILogger<RasaProcess>)_serviceProvider.GetService(typeof(ILogger<RasaProcess>)));
            _processes.TryAdd(GetProcessName(version, modelName, action), rasaProcess);

            return rasaProcess;
        }

        public string GetPortForModel(string modelName)
        {
            foreach (var rasaProcess in _processes.Values)
            {
                if (rasaProcess.ModelName == modelName)
                {
                    if (rasaProcess.GPU == 0)
                    {
                        return GPU_0_PORT;
                    }
                    else
                    {
                        return GPU_1_PORT;
                    }
                }
            }
            throw new Exception($"Could not get port for {modelName}");
        }

        public void RemoveProcess(string version, string modelName, string action)
        {
            try
            {
                _logger.LogDebug("Attempting to remove process {version}-{modelName}-{action}", version, modelName, action);

                if (HasProcess(version, modelName, action))
                {
                    _processes.TryRemove(GetProcessName(version, modelName, action), out RasaProcess rasaProcess);

                    if (rasaProcess == null)
                    {
                        _logger.LogDebug("Could not get RasaProcess");
                        return;
                    }

                    rasaProcess.ProcessInstance.Kill(true);
                    rasaProcess.ProcessInstance.Dispose();
                    rasaProcess.ProcessInstance = null;
                    rasaProcess = null;
                }
                else
                {
                    _logger.LogDebug("Could not find process. Available processes:");
                    foreach (var process in _processes)
                    {
                        _logger.LogDebug("{version}-{modelName}-{action}", process.Value.Version, process.Value.ModelName, process.Value.Action);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not remove process");
                throw ex;
            }
        }

        private string GetProcessName(string version, string modelName, string action)
        {
            return $"{version}-{modelName}-{action}";
        }

        public bool HasProcess(string version, string modelName, string action)
        {
            return _processes.ContainsKey(GetProcessName(version, modelName, action));
        }

        public void CreateFoldersAndFiles(string modelDirectory, IFormFile zip)
        {
            if (!Directory.Exists(modelDirectory))
            {
                Directory.CreateDirectory(modelDirectory);
            }

            var zipPath = $"{modelDirectory}/{zip.FileName}";
            using (var zipStream = new FileStream(zipPath, FileMode.Create))
            {
                zip.CopyTo(zipStream);
            }

            ZipFile.ExtractToDirectory(zipPath, modelDirectory);
            File.Delete(zipPath);
        }

        /// <summary>
        /// Loop through the list of processes and check if any of them have timed out and if they have then kill that process
        /// </summary>
        private void CheckProcessesForTimeout()
        {
            List<RasaProcess> processesToRemove = new List<RasaProcess>();
            foreach (RasaProcess rasaProcess in _processes.Values)
            {
                double minutes = (DateTime.Now - rasaProcess.StartTime).TotalMinutes;
                if (minutes > (double)_processTimeout)
                {
                    processesToRemove.Add(rasaProcess);
                }
            }

            for (int i = 0; i < processesToRemove.Count; i++)
            {
                RasaProcess rasaProcess = processesToRemove[i];
                RemoveProcess(rasaProcess.Version, rasaProcess.ModelName, rasaProcess.Action);
            }
        }

        public int NumberOfProcesses()
        {
            return _processes.Count;
        }

        public void SetRasaVersions(RasaVersionModel[] rasaVersions)
        {
            foreach (RasaVersionModel rasaVersion in rasaVersions)
            {
                _rasaVersions.TryAdd(rasaVersion.Version, rasaVersion);
            }
        }

        public List<string> GetRasaVersions()
        {
            return _rasaVersions.Keys.ToList();
        }

        public RasaVersionModel GetRasaVersionModel(string version)
        {
            if (!_rasaVersions.TryGetValue(version, out RasaVersionModel rasaVersion))
            {
                throw new Exception($"The version {version} is not supported");
            }
            return rasaVersion;
        }
    }
}