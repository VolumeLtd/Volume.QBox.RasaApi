using System;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Volume.QBox.RasaApi.Services
{
    public class RasaProcess
    {
        private const string RASA_ACTION_SHELL = "run";
        private const string RASA_ACTION_TRAIN = "train";
        public Process ProcessInstance { get; set; }
        public bool InUse { get; set; }
        public string Version { get; set; }
        public string ModelName { get; set; }
        public string Action { get; set; }
        public string ModelDirectory { get; set; }
        public int GPU { get; set; }
        public int PredictionTimeout { get; }
        public bool Ready { get; set; } = false;
        public bool Done { get; set; } = false;
        public StringBuilder Intents { get; set; }
        public DateTime StartTime { get; set; }

        private readonly ILogger<RasaProcess> _logger;


        public RasaProcess(string modelDirectory, string rasaDirectory, string version, string modelName,
                           int gpu, string action, string pythonPath, int predictionTimeout,
                           ILogger<RasaProcess> logger)
        {
            Version = version;
            ModelName = modelName;
            Action = action;
            ModelDirectory = modelDirectory;
            PredictionTimeout = predictionTimeout;
            GPU = gpu;
            _logger = logger;

            InitProcess(modelDirectory, rasaDirectory, modelName, gpu, action, pythonPath);
        }

        private void InitProcess(string modelDirectory, string rasaDirectory, string modelName, int gpu, string action, string pythonPath)
        {
            ProcessInstance = new Process();
            StartTime = DateTime.Now;
            var startInfo = new ProcessStartInfo();
            string modelDir = modelDirectory + modelName;

            startInfo.WorkingDirectory = modelDir;
            startInfo.EnvironmentVariables["CURRENTMODELPATH"] = modelDir;
            startInfo.EnvironmentVariables["PYTHONPATH"] = pythonPath;
            startInfo.FileName = $"{rasaDirectory}rasa";
            startInfo.Arguments = GenerateArgumentForAction(action, modelDir, GetPort(gpu));

            _logger?.LogDebug("WorkingDirectory {workingDirectory}", startInfo.WorkingDirectory);
            _logger?.LogDebug("CurrentModelPath {currentModelPath}", startInfo.EnvironmentVariables["CURRENTMODELPATH"]);
            _logger?.LogDebug("PythonPath {pythonPath}", startInfo.EnvironmentVariables["PYTHONPATH"]);
            _logger?.LogDebug("FileName {fileName}", startInfo.FileName);
            _logger?.LogDebug($"Arguments= {startInfo.Arguments}");
            _logger?.LogDebug($"Setting CUDA_VISIBLE_DEVICES TO GPU {gpu}");

            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardError = true;
            ProcessInstance.StartInfo = startInfo;
            ProcessInstance.EnableRaisingEvents = true;
            Environment.SetEnvironmentVariable("CUDA_VISIBLE_DEVICES", gpu.ToString());

            ProcessInstance.Start();
            ProcessInstance.BeginOutputReadLine();
            ProcessInstance.BeginErrorReadLine();
        }

        private string GenerateArgumentForAction(string action, string modelDir, string port)
        {
            string arguments = action;

            if (action == RASA_ACTION_TRAIN)
            {
                if (Version.StartsWith('2'))
                {
                    arguments += " --config " + modelDir + "/config.yml --data " + modelDir + "/data/nlu.yml --out " + modelDir + "/models/";
                }
                else
                {
                    arguments += " --config " + modelDir + "/config.yml --data " + modelDir + "/data/nlu.md --out " + modelDir + "/models/";
                }
            }
            else if (action == RASA_ACTION_SHELL)
            {
                arguments += " --model " + modelDir + "/models/ --enable-api -p " + port; // + " --jwt-secret " + token; //--enable-api
            }

            return arguments;
        }

        private string GetPort(int gpu)
        {
            if (gpu == 0)
            {
                return "5005";
            }
            else
            {
                return "5006";
            }
        }

        private void ProcessInstance_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            _logger?.LogDebug("ErrorDataReceived: {Data}", e.Data);
        }
    }
}