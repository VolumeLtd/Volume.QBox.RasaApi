using Microsoft.AspNetCore.Http;

namespace Volume.QBox.RasaApi.Models
{
    public class RasaModel
    {
        public string Version { get; set; }

        public string ModelName { get; set; }

        public IFormFile Zip { get; set; }

        public bool Force { get; set; }

        public string Token { get; set; }
    }
}