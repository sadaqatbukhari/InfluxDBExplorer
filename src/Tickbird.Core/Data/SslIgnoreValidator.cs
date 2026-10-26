using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Tickbird.Core.Data
{
    /// <summary>
    /// Utility class used to ignore verifying SSL/TLS certificates.
    /// </summary>
    public static class SslIgnoreValidator
    {
        // Whether or not the SSL validator override has been enabled or not
        private static bool enabled = false;

        // Whether or not to allow untrusted SSL/TLS certificates.
        private static bool allowUntrusted = false;

        /// <summary>
        /// Gets whether or not to allow untrusted SSL/TLS certificates.
        /// </summary>
        public static bool AllowUntrusted
        {
            get { return allowUntrusted; }
            
            set
            {
                allowUntrusted = value;

                // Configure untrusted allowances as needed
                if (allowUntrusted && !enabled)
                {
                    OverrideValidation();
                    enabled = true;
                }
            }
        }

        // Custom validation method
        private static bool OnValidateCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                if (!allowUntrusted && sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
                {
                    return false;
                }
            }

            return true;
        }

        // Overrides SSL/TLS certificate validation.
        static void OverrideValidation()
        {
#pragma warning disable SYSLIB0014 // Required by the legacy InfluxDB 1.x client.
            ServicePointManager.ServerCertificateValidationCallback = OnValidateCertificate;
            ServicePointManager.Expect100Continue = true;
#pragma warning restore SYSLIB0014
        }
    }
}
