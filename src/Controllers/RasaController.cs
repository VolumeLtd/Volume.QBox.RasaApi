using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Volume.QBox.RasaApi.Models;
using Volume.QBox.RasaApi.Services;

namespace Volume.QBox.RasaApi.Controllers
{

    [ApiController]
    [Route("[controller]")]
    public class RasaController : ControllerBase
    {
        private readonly ILogger<RasaController> _logger;
        private readonly Interfaces.IProcessService _processService;
        private readonly HttpClient _httpClient;
        private readonly string _token;

        private const string RASA_ACTION_SHELL = "run";
        private const string RASA_ACTION_TRAIN = "train";
        private const int PREDICTION_TIMEOUT = 5;

        public RasaController(ILogger<RasaController> logger, Interfaces.IProcessService processService, IConfiguration configuration)
        {
            _logger = logger;
            _processService = processService;
            _token = configuration.GetValue<string>("AppSettings:Token");
            _processService.ActiveVersion = configuration.GetValue<string>("AppSettings:DefaultRasaVersion");
            _httpClient = new HttpClient();
        }

        [HttpPost]
        [Route("Train")]
        public IActionResult Train(RasaModel model)
        {
            try
            {
                if (model == null)
                {
                    return BadRequest("Parameter model missing");
                }

                _processService.ActiveVersion = model.Version;

                _logger.LogDebug("Version {ActiveVersion} started training {ModelName}", _processService.ActiveVersion, model.ModelName);

                bool finishedTraining = false;

                if (!IsValidToken(model.Token))
                {
                    return Unauthorized();
                }

                RasaVersionModel rasaVersion = _processService.GetRasaVersionModel(_processService.ActiveVersion);
                string modelDirectory = rasaVersion.ModelDir + model.ModelName + "/";

                _logger.LogDebug("About to create folder {ModelDirectory} and copy files", modelDirectory);

                _processService.CreateFoldersAndFiles(modelDirectory, model.Config, model.Nlu);

                RasaProcess rasaProcess = _processService.CreateProcess(_processService.ActiveVersion, model.ModelName, RASA_ACTION_TRAIN);

                rasaProcess.ProcessInstance.ErrorDataReceived += (object sender, DataReceivedEventArgs args) =>
                {
                    HandleError(args.Data, modelDirectory, ref finishedTraining);
                };

                SetModelStatus("Training", modelDirectory);
                var watch = Stopwatch.StartNew();

                //When the process stops, clean up and write the status to the status file                
                rasaProcess.ProcessInstance.Exited += (object sender, EventArgs e) =>
                {
                    FinishedTraining(rasaProcess, modelDirectory, watch, finishedTraining);
                };

                return Ok("Started");
            }
            catch (Exception x)
            {
                _logger.LogError(x, "Training failed for {ModelName} on version {Version}", model.ModelName, model.Version);
                _processService.RemoveProcess(model.Version, model.ModelName, RASA_ACTION_TRAIN);
                return StatusCode(StatusCodes.Status500InternalServerError, x.Message);
            }
        }

        [HttpPost]
        [Route("TrainZip")]
        public IActionResult TrainZip([FromForm] RasaZipModel model)
        {
            try
            {
                if (model == null)
                {
                    return BadRequest("Parameter model missing");
                }

                _processService.ActiveVersion = model.Version;

                _logger.LogDebug("Version {ActiveVersion} started training {ModelName}", _processService.ActiveVersion, model.ModelName);

                bool finishedTraining = false;

                if (!IsValidToken(model.Token))
                {
                    return Unauthorized();
                }

                RasaVersionModel rasaVersion = _processService.GetRasaVersionModel(_processService.ActiveVersion);
                string modelDirectory = rasaVersion.ModelDir + model.ModelName + "/";

                _logger.LogDebug("About to create folder {ModelDirectory} and copy files", modelDirectory);

                _processService.CreateFoldersAndFiles(modelDirectory, model.Zip);

                RasaProcess rasaProcess = _processService.CreateProcess(_processService.ActiveVersion, model.ModelName, RASA_ACTION_TRAIN);

                rasaProcess.ProcessInstance.ErrorDataReceived += (object sender, DataReceivedEventArgs args) =>
                {
                    HandleError(args.Data, modelDirectory, ref finishedTraining);
                };

                SetModelStatus("Training", modelDirectory);
                var watch = Stopwatch.StartNew();

                //When the process stops, clean up and write the status to the status file                
                rasaProcess.ProcessInstance.Exited += (object sender, EventArgs e) =>
                {
                    FinishedTraining(rasaProcess, modelDirectory, watch, finishedTraining);
                };

                return Ok("Started");
            }
            catch (Exception x)
            {
                _logger.LogError(x, "Training failed for {ModelName} on version {Version}", model.ModelName, model.Version);
                _processService.RemoveProcess(model.Version, model.ModelName, RASA_ACTION_TRAIN);
                return StatusCode(StatusCodes.Status500InternalServerError, x.Message);
            }
        }

        private void FinishedTraining(RasaProcess rasaProcess, string modelDirectory, Stopwatch watch, bool finishedTraining)
        {
            try
            {
                _logger.LogDebug("Process finished {version}-{modelName}, it took {TotalSeconds}s.", rasaProcess.Version, rasaProcess.ModelName, watch.Elapsed.TotalSeconds.ToString("N1"));

                _processService.RemoveProcess(rasaProcess.Version, rasaProcess.ModelName, rasaProcess.Action);

                if (finishedTraining)
                {
                    SetModelStatus("Ready", modelDirectory);
                }
                else
                {
                    string errorMessage = "Process exited without training.";
                    _logger.LogDebug(errorMessage);
                    SetModelStatus($"Error{Environment.NewLine}{errorMessage}", modelDirectory);
                }
            }
            catch (Exception x)
            {
                SetModelStatus($"Error{Environment.NewLine}{x.Message}", modelDirectory);
                _logger.LogError(x, "Error in FinishedTraining for {modelName}", rasaProcess.ModelName);
            }
        }

        private void HandleError(string error, string modelDirectory, ref bool finishedTraining)
        {
            try
            {
                if (HelperService.IgnoreError(error))
                {
                    return;
                }

                if (error.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _logger.LogDebug("HandleError received error: {error}", error);
                    SetModelStatus($"Error{Environment.NewLine}{error}", modelDirectory);
                    throw new Exception(error);
                }

                if (error.IndexOf("Successfully saved model", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _logger.LogDebug("Successfully saved model. {error}", error);
                    finishedTraining = true;
                }
                _logger.LogError(error);
            }
            catch (Exception x)
            {
                _logger.LogError(x, "Error handling error from process. Error: {error} for {modelDirectory}", error, modelDirectory);
            }
        }

        private void SetModelStatus(string status, string directory)
        {
            _logger.LogDebug("Set Model status: {status} in directory {directory}", status, directory);
            string content = String.Empty;
            if (System.IO.File.Exists($"{directory}/status"))
            {
                content = System.IO.File.ReadAllText($"{directory}/status");
            }

            if (!content.Contains("Error"))
            {
                System.IO.File.WriteAllText($"{directory}/status", status);
            }
        }

        [HttpPost]
        [Route("Prediction")]
        public async Task<IActionResult> Prediction(PredictionModel model)
        {
            if (model == null)
            {
                return BadRequest("Parameter missing");
            }

            if (!IsValidToken(model.Token))
            {
                return Unauthorized();
            }

            _processService.ActiveVersion = model.Version;

            _logger.LogDebug("Prediction: {ActiveVersion}-{ModelName}: Text: {Text}", _processService.ActiveVersion, model.ModelName, model.Text);

            RasaVersionModel rasaVersion = _processService.GetRasaVersionModel(_processService.ActiveVersion);
            string modelDirectory = rasaVersion.ModelDir + model.ModelName;

            if (!Directory.Exists(modelDirectory) || !Directory.Exists(modelDirectory + "/models"))
            {
                _logger.LogDebug("Model {ModelName} not found in directory {modelDirectory}", model.ModelName, modelDirectory);
                return NotFound("Model not found");
            }

            try
            {
                _processService.StartProcess(_processService.ActiveVersion, model.ModelName, RASA_ACTION_SHELL, out bool newProcess);
                var rasaProcess = await _processService.GetProcess(_processService.ActiveVersion, model.ModelName, RASA_ACTION_SHELL);
                bool serverStarted = false;

                rasaProcess.ProcessInstance.ErrorDataReceived += (object sender, DataReceivedEventArgs args) =>
                {
                    if (args.Data.Contains("Rasa server is up and running", StringComparison.OrdinalIgnoreCase))
                    {
                        serverStarted = true;
                    }
                };

                Stopwatch watch = new Stopwatch();

                if (newProcess == true)
                {
                    while (!serverStarted)
                    {
                        await Task.Delay(1000);

                        if (watch.Elapsed.TotalMinutes > PREDICTION_TIMEOUT)
                        {
                            return StatusCode(StatusCodes.Status500InternalServerError, "Timeout for waiting for Rasa server to start");
                        }
                    }
                }

                string port = _processService.GetPortForModel(model.ModelName);
                string url = $"http://localhost:{port}/model/parse";

                var body = new
                {
                    text = LatinToAscii(model.Text)
                };

                StringContent content = new StringContent(JsonConvert.SerializeObject(body));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(url))
                {
                    Content = content
                };

                HttpResponseMessage response = await _httpClient.SendAsync(request);
                string responseMessage = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    responseMessage = await response.Content.ReadAsStringAsync();
                }
                else
                {
                    _logger.LogDebug("Status not ok. reponse: {responseMessage}", responseMessage);
                    return StatusCode(StatusCodes.Status500InternalServerError, responseMessage);
                }

                return Ok(responseMessage);
            }
            catch (Exception x)
            {
                _logger.LogError(x, "Prediction, exception: {Message}", x.Message);
                _processService.RemoveProcess(_processService.ActiveVersion, model.ModelName, RASA_ACTION_SHELL);
                return StatusCode(StatusCodes.Status500InternalServerError, x.Message);
            }
        }


        [HttpDelete]
        [Route("DeleteModel/{version}/{modelName}/{token}")]
        public IActionResult DeleteModel(string version, string modelName, string token)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(modelName))
                {
                    return BadRequest("Model name missing");
                }

                if (!IsValidToken(token))
                {
                    return Unauthorized();
                }

                _logger.LogDebug("Delete model {version} {modelName}", version, modelName);

                _processService.ActiveVersion = version;

                RasaVersionModel rasaVersion = _processService.GetRasaVersionModel(_processService.ActiveVersion);
                Directory.Delete(rasaVersion.ModelDir + modelName, true);
                return Ok("true");
            }
            catch (Exception x)
            {
                _logger.LogError(x, "Exception in DeleteModel for {modelName}", modelName);
                return StatusCode(StatusCodes.Status500InternalServerError, x.Message);
            }
            finally
            {
                try
                {
                    _processService.RemoveProcess(_processService.ActiveVersion, modelName, RASA_ACTION_TRAIN);
                    _processService.RemoveProcess(_processService.ActiveVersion, modelName, RASA_ACTION_SHELL);

                }
                catch (Exception x)
                {
                    _logger.LogError(x, "Error in DeleteModel for {modelName} when removing process", modelName);
                }
            }
        }

        [HttpGet]
        [Route("Status/{version}/{modelName}/{token}")]
        public IActionResult Status(string version, string modelName, string token)
        {
            if (!IsValidToken(token))
            {
                return Unauthorized();
            }

            RasaVersionModel rasa = _processService.GetRasaVersionModel(version);
            string modelDirectory = rasa.ModelDir + modelName;
            string statusFile = modelDirectory + "/status";
            if (!System.IO.File.Exists(statusFile))
            {
                return NotFound(new StatusModel() { Status = "Error", Message = "No model found" });
            }

            string[] lines = System.IO.File.ReadAllLines(statusFile);

            if (lines.Length == 1)
            {
                return Ok(new StatusModel() { Status = lines[0], Message = $"Processes active: {_processService.NumberOfProcesses()}" });
            }
            else if (lines.Length > 1)
            {
                return Ok(new StatusModel() { Status = "Error", Message = String.Join(Environment.NewLine, lines.Skip(1).ToArray()) });
            }
            else
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpGet]
        [Route("active/{token}")]
        public IActionResult Active(string token)
        {
            if (!IsValidToken(token))
            {
                _logger.LogDebug("Active called with an invalid token");
                return Unauthorized();
            }
            return Ok();
        }

        [HttpGet]
        [Route("versions/{token}")]
        public IActionResult Versions(string token)
        {
            if (!IsValidToken(token))
            {
                _logger.LogDebug("Active called with an invalid token");
                return Unauthorized();
            }

            List<string> versions = _processService.GetRasaVersions();
            return Ok(versions);
        }

        private bool IsValidToken(string token)
        {
            return token.Equals(_token);
        }

        // Based on http://www.codeproject.com/Articles/13503/Stripping-Accents-from-Latin-Characters-A-Foray-in
        private static string LatinToAscii(string inString)
        {
            var newStringBuilder = new StringBuilder();
            newStringBuilder.Append(inString.Normalize(NormalizationForm.FormKD)
                                            .Where(x => x < 128)
                                            .ToArray());
            return newStringBuilder.ToString();
        }
    }
}