using System.ComponentModel.DataAnnotations;

namespace UpFlux.Cloud.Simulator
{
    /// <summary>
    /// Holds settings for the Cloud Simulator.
    /// since the Gateway now dials out to the Cloud.
    /// </summary>
    public class CloudSettings
    {
        /// <summary>
        /// The port on which the Cloud gRPC server will listen for incoming connections.
        /// </summary>
        public int ListeningPort { get; set; } = 5002;

        /// <summary>
        /// Passphrase to use for GPG encryption of the update package.
        /// </summary>
        public string GpgPassphrase { get; set; }
    }
}
