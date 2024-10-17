import subprocess

DEVICE_AVAILABILITY_MONITOR = "device_availability_monitor.py"
RGB_SENSOR = "rgb_sensor.py"

try:
    process1 = subprocess.Popen(["python3", DEVICE_AVAILABILITY_MONITOR])
    process2 = subprocess.Popen(["python3", RGB_SENSOR])

    process1.wait()
    process2.wait()

except subprocess.CalledProcessError as e:
    print(f"Error while running the script: {e}")

