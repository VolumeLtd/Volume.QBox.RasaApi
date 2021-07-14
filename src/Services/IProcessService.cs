using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Volume.QBox.RasaApi.Models;
using Volume.QBox.RasaApi.Services;

namespace Volume.QBox.RasaApi.Interfaces
{
    public interface IProcessService
    {
        void StartProcess(string version, string modelName, string action, out bool newProcess);

        Task<RasaProcess> GetProcess(string version, string modelName, string action);

        RasaProcess CreateProcess(string version, string modelName, string action);

        void RemoveProcess(string version, string modelName, string action);

        bool HasProcess(string version, string modelName, string action);

        void CreateFoldersAndFiles(string modelDirectory, IFormFile zip);

        int NumberOfProcesses();

        void SetRasaVersions(RasaVersionModel[] rasaVersions);

        List<string> GetRasaVersions();

        RasaVersionModel GetRasaVersionModel(string version);

        string ActiveVersion { get; set; }

        string GetPortForModel(string modelName);
    }
}