using System.ComponentModel.DataAnnotations;

namespace UpFlux.Cloud.Simulator
{
    public class CloudSettings
    {
        public int ListeningPort { get; set; } = 5002;

        [Required]
        public string GatewayAddress { get; set; }
    }
}
