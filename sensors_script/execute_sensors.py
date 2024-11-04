import subprocess
import os

# Get the current directory of this script
script_dir = os.path.dirname(os.path.abspath(__file__))

# Construct the full paths to the other scripts
DEVICE_AVAILABILITY_MONITOR = os.path.join(script_dir, "device_availability_monitor.py")
RGB_SENSOR = os.path.join(script_dir, "rgb_sensor.py")

try:
    process1 = subprocess.Popen(["python3", DEVICE_AVAILABILITY_MONITOR])
    # Start rgb_sensor.py and capture its output
    process2 = subprocess.Popen(
        ["python3", "-u", RGB_SENSOR],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        universal_newlines=True
    )

    # Read the output from rgb_sensor.py and print it
    for line in iter(process2.stdout.readline, ''):
        print(line, end='', flush=True)

    process1.wait()
    process2.wait()

except subprocess.CalledProcessError as e:
    print(f"Error while running the script: {e}", file=sys.stderr)

