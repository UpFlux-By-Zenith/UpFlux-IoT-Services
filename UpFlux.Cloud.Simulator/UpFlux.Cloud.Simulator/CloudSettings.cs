using System.ComponentModel.DataAnnotations;

namespace UpFlux.Cloud.Simulator
{
    public class CloudSettings
    {
        public int ListeningPort { get; set; } = 5002;

        [Required]
        public string CertificatePath { get; set; }

        public string CertificatePassword { get; set; }

        [Required]
        public string TrustedCaCertificatePath { get; set; }

        [Required]
        public string GatewayAddress { get; set; }

        public bool SkipServerCertificateValidation { get; set; } = false;
    }
}
