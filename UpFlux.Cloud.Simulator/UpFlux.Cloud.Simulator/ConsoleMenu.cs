using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Grpc.Core;
using System.Net.Http;
using UpFlux.Cloud.Simulator.Protos;
using System.Diagnostics;

namespace UpFlux.Cloud.Simulator
{
    /// <summary>
    /// A Console Menu that allows the user to trigger various 
    /// actions (rollback, log requests, update packages, etc.) 
    /// on connected gateways.
    /// </summary>
    public class ConsoleMenu
    {
        private readonly IConfiguration _configuration;
        private readonly CloudSettings _cloudSettings;
        private readonly ControlChannelService _controlSvc;

        /// <summary>
        /// Creates a new instance of ConsoleMenu using dependencies 
        /// injected from the host’s DI container.
        /// </summary>
        public ConsoleMenu(IConfiguration configuration, CloudSettings cloudSettings, ControlChannelService controlSvc)
        {
            _configuration = configuration;
            _cloudSettings = cloudSettings;
            _controlSvc = controlSvc;
        }

        /// <summary>
        /// Runs the interactive menu loop on the console, blocking until "Exit" is chosen.
        /// </summary>
        public async Task RunMenuLoop()
        {
            while (true)
            {
                ConsoleSync.WriteLine("\n--- UpFlux Cloud Simulator Menu (calls Gateway) ---");
                ConsoleSync.WriteLine("1) Send ROLLBACK command to device(s)");
                ConsoleSync.WriteLine("2) Request logs from device(s)");
                ConsoleSync.WriteLine("3) Send update package to device(s)");
                ConsoleSync.WriteLine("4) Request version info from device(s)");
                ConsoleSync.WriteLine("5) Schedule future update for device(s)");
                ConsoleSync.WriteLine("6) Exit");
                ConsoleSync.Write("Choose: ");
                char key = ConsoleSync.ReadKey();
                ConsoleSync.WriteLine("");

                if (key == '1')
                {
                    await MenuSendRollback();
                }
                else if (key == '2')
                {
                    await MenuRequestLogs();
                }
                else if (key == '3')
                {
                    await MenuSendUpdate();
                }
                else if (key == '4')
                {
                    await MenuRequestVersionData();
                }
                else if (key == '5')
                {
                    await MenuScheduleFutureUpdate();
                }
                else if (key == '6')
                {
                    ConsoleSync.WriteLine("Exiting menu...");
                    break;
                }
                else
                {
                    ConsoleSync.WriteLine("Invalid choice. Try again.");
                }
            }
        }

        /// <summary>
        /// Demonstrates sending a ROLLBACK command to a specific gateway ID.
        /// The user enters the gateway’s ID plus the target device(s).
        /// </summary>
        private async Task MenuSendRollback()
        {
            ConsoleSync.Write("Enter Gateway ID to send command to: ");
            string gatewayId = (ConsoleSync.ReadLine() ?? "").Trim();

            ConsoleSync.Write("Enter device UUID(s), comma-separated: ");
            string line = ConsoleSync.ReadLine() ?? "";
            string[] uuids = line.Split(',', StringSplitOptions.RemoveEmptyEntries);

            ConsoleSync.Write("Enter rollback version (e.g. 1.2.3): ");
            string param = ConsoleSync.ReadLine() ?? "1.0.0";

            string cmdId = Guid.NewGuid().ToString();
            await _controlSvc.SendCommandToGatewayAsync(
                gatewayId,
                cmdId,
                CommandType.Rollback,
                param,
                uuids
            );

            ConsoleSync.WriteLine($"Rollback command sent to gateway [{gatewayId}] for {uuids.Length} device(s).");
        }

        /// <summary>
        /// Demonstrates requesting logs from certain devices.
        /// </summary>
        private async Task MenuRequestLogs()
        {
            ConsoleSync.Write("Enter Gateway ID: ");
            string gatewayId = (ConsoleSync.ReadLine() ?? "").Trim();

            ConsoleSync.Write("Enter device UUID(s), comma-separated: ");
            string line = ConsoleSync.ReadLine() ?? "";
            string[] uuids = line.Split(',', StringSplitOptions.RemoveEmptyEntries);

            await _controlSvc.SendLogRequestAsync(gatewayId, uuids);

            ConsoleSync.WriteLine($"Log request sent to gateway [{gatewayId}] for {uuids.Length} device(s).");
        }

        /// <summary>
        /// Signs the update package and sends it to the Gateway for the specified devices.
        /// </summary>
        private async Task MenuSendUpdate()
        {
            ConsoleSync.Write("Enter Gateway ID: ");
            string gatewayId = (ConsoleSync.ReadLine() ?? "").Trim();

            ConsoleSync.Write("Enter device UUID(s), comma-separated: ");
            string line = ConsoleSync.ReadLine() ?? "";
            string[] uuids = line.Split(',', StringSplitOptions.RemoveEmptyEntries);

            ConsoleSync.Write("Path to .deb package: ");
            string packagePath = ConsoleSync.ReadLine() ?? "";
            if (!System.IO.File.Exists(packagePath))
            {
                ConsoleSync.WriteLine("File not found. Aborting.");
                return;
            }

            string fileName = System.IO.Path.GetFileName(packagePath);
            string signatureFile = packagePath + ".sig";

            ConsoleSync.WriteLine($"Signing {fileName} using GPG...");

            bool signed = SignPackage(packagePath, signatureFile);
            if (!signed)
            {
                ConsoleSync.WriteLine("Signing failed. Aborting.");
                return;
            }

            byte[] packageData = await System.IO.File.ReadAllBytesAsync(packagePath);
            byte[] signatureData = await File.ReadAllBytesAsync(signatureFile);

            await _controlSvc.SendUpdatePackageAsync(gatewayId, fileName, packageData, signatureData, uuids);

            ConsoleSync.WriteLine($"Update package '{fileName}' sent to gateway [{gatewayId}] for {uuids.Length} device(s).");
        }

        /// <summary>
        /// Signs a package using GPG.
        /// </summary>
        private bool SignPackage(string filePath, string signaturePath)
        {
            try
            {
                string? gpgPassphrase = _cloudSettings.GpgPassphrase;
                string gpgKeyId = "3ADAF9875CB265A5C41016D2B1902804CC7BF808";

                ConsoleSync.WriteLine($"GPG_PASSPHRASE: {gpgPassphrase}");
                Console.WriteLine($"GPG_KEY_ID: {gpgKeyId}");

                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = "gpg",
                    Arguments = $"--homedir \"/home/pi/.gnupg\" --batch --pinentry-mode=loopback --passphrase \"{gpgPassphrase}\" --yes --armor --output \"{signaturePath}\" --detach-sign -u \"{gpgKeyId}\" \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                Process process = new Process { StartInfo = processStartInfo };
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    ConsoleSync.WriteLine($"Error signing file: {error}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ConsoleSync.WriteLine($"Error signing file: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Demonstrates requesting version data from the gateway.
        /// </summary>
        private async Task MenuRequestVersionData()
        {
            ConsoleSync.Write("Enter Gateway ID: ");
            string gatewayId = (ConsoleSync.ReadLine() ?? "").Trim();

            await _controlSvc.SendVersionDataRequestAsync(gatewayId);
            ConsoleSync.WriteLine($"VersionDataRequest sent to gateway [{gatewayId}].");
        }

        /// <summary>
        /// This method demonstrates scheduling a future update for the gateway.
        /// </summary>
        private async Task MenuScheduleFutureUpdate()
        {
            ConsoleSync.Write("Enter Gateway ID: ");
            string gatewayId = (ConsoleSync.ReadLine() ?? "").Trim();

            ConsoleSync.Write("Enter device UUID(s), comma-separated: ");
            string line = ConsoleSync.ReadLine() ?? "";
            string[] uuids = line.Split(',', StringSplitOptions.RemoveEmptyEntries);

            ConsoleSync.Write("Path to .deb package: ");
            string packagePath = ConsoleSync.ReadLine() ?? "";
            if (!File.Exists(packagePath))
            {
                ConsoleSync.WriteLine("File not found. Aborting.");
                return;
            }

            string fileName = Path.GetFileName(packagePath);
            string signatureFile = packagePath + ".sig";
            Console.WriteLine($"Signing {fileName} using GPG...");

            bool signed = SignPackage(packagePath, signatureFile);
            if (!signed)
            {
                ConsoleSync.WriteLine("Signing failed. Aborting.");
                return;
            }

            byte[] packageData = await File.ReadAllBytesAsync(packagePath);
            byte[] signatureData = await File.ReadAllBytesAsync(signatureFile);

            ConsoleSync.Write("Enter start time offset in minutes from now: ");
            string offsetStr = ConsoleSync.ReadLine();
            if (!int.TryParse(offsetStr, out int offsetMin))
            {
                offsetMin = 2;
            }
            DateTime startTimeUtc = DateTime.UtcNow.AddMinutes(offsetMin);

            string scheduleId = Guid.NewGuid().ToString("N");

            await _controlSvc.SendScheduledUpdateAsync(
                gatewayId,
                scheduleId,
                uuids,
                fileName,
                packageData,
                signatureData,
                startTimeUtc
            );

            ConsoleSync.WriteLine($"Scheduled update with ID={scheduleId} at {startTimeUtc:o} for devices={string.Join(',', uuids)}");
        }

    }
}
