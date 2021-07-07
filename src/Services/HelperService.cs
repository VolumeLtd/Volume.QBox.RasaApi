using System;

namespace Volume.QBox.RasaApi.Services
{
    public static class HelperService
    {
        public static bool IgnoreError(string error)
        {
            if (error == null)
            {
                return true;
            }
            if (error.IndexOf("No domain file", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (error.IndexOf("failed call to cuInit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (error.IndexOf("Found intents", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }
    }
}
