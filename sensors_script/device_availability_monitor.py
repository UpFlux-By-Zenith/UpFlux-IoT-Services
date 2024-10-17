import RPi.GPIO as GPIO
import time
import subprocess

GPIO.setmode(GPIO.BCM)

GREEN_LED = 10
YELLOW_LED = 9
RED_LED = 11

GPIO.setup(GREEN_LED, GPIO.OUT)
GPIO.setup(YELLOW_LED, GPIO.OUT)
GPIO.setup(RED_LED, GPIO.OUT)

def set_green_default():
    GPIO.output(GREEN_LED, GPIO.HIGH)
    GPIO.output(YELLOW_LED, GPIO.LOW)
    GPIO.output(RED_LED, GPIO.LOW)

def is_package_installing():
    try:
        apt_process = subprocess.run(['pgrep', 'apt'], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        dpkg_process = subprocess.run(['pgrep', 'dpkg'], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if apt_process.stdout or dpkg_process.stdout:
            return True
    except subprocess.SubprocessError as e:
        print(f"Error checking installation processes: {e}")

    return False

def is_installation_failed():
    try:
        result_dpkg = subprocess.run(['tail', '-n', '10', '/var/log/dpkg.log'], stdout=subprocess.PIPE, text=True)
        result_uplflux = subprocess.run(['tail', '-n', '10', '/var/log/upflux.log'], stdout=subprocess.PIPE, text=True)
        if "error" in result_dpkg.stdout.lower() or "error" in result_uplflux.stdout.lower():
            return True
    except subprocess.SubprocessError as e:
        print(f"Error reading dpkg log: {e}")

    return False

def is_installation_success():
    try:
        result_dpkg = subprocess.run(['tail', '-n', '10', '/var/log/dpkg.log'], stdout=subprocess.PIPE, text=True)
        if "status installed" in result_dpkg.stdout.lower():
            return True
    except subprocess.SubprocessError as e:
        print(f"Error reading dpkg log: {e}")

    return False

try:
    while True:
        if is_installation_failed():
            GPIO.output(GREEN_LED, GPIO.LOW)
            GPIO.output(YELLOW_LED, GPIO.LOW)
            GPIO.output(RED_LED, GPIO.HIGH)
        elif is_package_installing():
            GPIO.output(GREEN_LED, GPIO.LOW)
            GPIO.output(RED_LED, GPIO.LOW)
            GPIO.output(YELLOW_LED, GPIO.HIGH)
        elif is_installation_success():
            set_green_default()
        else:
            set_green_default()

        time.sleep(2)

except KeyboardInterrupt:
    GPIO.cleanup()

except Exception as e:
    print(f"An error occurred: {e}")
    GPIO.cleanup()

finally:
    GPIO.cleanup()
