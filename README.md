# UpFlux IoT Services

Welcome to the UpFlux IoT Services repository. This repo is basically the backbone of the entire UpFlux ecosystem. It's responsible for handling all the crucial bits, from device monitoring and updates, to secure communication between devices, gateways, and the cloud.

## What does this repo contain?

### Project Structure:

Here's what you'll find inside:

- **`.github/workflows`**: CI/CD pipelines and workflows we use for automatic builds, tests, and deployments. Basically ensures we don't break stuff.

- **`UpFlux.Cloud.Simulator`**: This simulates our cloud environment, allowing us to test package updates, communication protocols, and device management before hitting the real cloud. Think of it like our practice pitch before the actual game.

- **`UpFlux.Gateway.Server`**: The Gateway Server application that acts like the middleman. It fetches update packages from the cloud, validates them (checks if they're authentic and secure), and then carefully sends them over to the devices.

- **`UpFlux.Monitoring.Library`**: A reusable C# library. It gathers device details and performance metrics such as CPU usage, memory, network stats, and other system information. It gives us quick insights into the health and state of our devices.

- **`UpFlux.Monitoring.Service`**: The service application built on the Monitoring Library. Runs continuously on each UpFlux device (like Raspberry Pi), sending real-time data back to the gateway. It's essentially our eyes and ears on the ground.

- **`UpFlux.Update.Service`**: The actual update handler that listens for new update packages. When updates come through, this service verifies the packages, installs them on devices, and handles rollbacks if something goes wrong. Like your phone updating apps overnight automatically, but smarter and industrial-grade.

- **`ai_script`**: Python scripts that power our AI clustering and scheduling functionality. It analyses devices based on usage data, clusters them logically, and schedules the updates at the best possible times. Keeps everything running smoothly and efficiently.

- **`sensors_script`**: Holds Python scripts for the RGB sensor. These scripts run continuously, monitoring and detecting colors on paper moving along the conveyor belt. Super critical for real-time quality control and tracking in the production environment.

### 🛠️ Main Technologies & Tools:

- `.NET (C#)`
- `Python`
- `gRPC`
- `Raspberry Pi Devices (Ubuntu)`
