"""
Module that monitor package installation and control LEDs accordingly  
"""
import RPi.GPIO as GPIO
import time
import subprocess

GPIO.setmode(GPIO.BCM)

GREEN_LED = 21
YELLOW_LED = 13
RED_LED = 5
WARMUP_PIN = 16

DPKG_LOG_PATH = '/var/log/dpkg.log'
UPFLUX_LOG_PATH = '/var/log/upflux/upflux-sensors.log'

GPIO.setup(GREEN_LED, GPIO.OUT)
GPIO.setup(YELLOW_LED, GPIO.OUT)
GPIO.setup(RED_LED, GPIO.OUT)
GPIO.setup(WARMUP_PIN, GPIO.IN, pull_up_down=GPIO.PUD_DOWN)

def set_green_led():
    GPIO.output(GREEN_LED, GPIO.HIGH)
    GPIO.output(YELLOW_LED, GPIO.LOW)
    GPIO.output(RED_LED, GPIO.LOW)

def set_yellow_led():
    GPIO.output(GREEN_LED, GPIO.LOW)
    GPIO.output(YELLOW_LED, GPIO.HIGH)
    GPIO.output(RED_LED, GPIO.LOW)

def set_red_led():
    GPIO.output(GREEN_LED, GPIO.LOW)
    GPIO.output(YELLOW_LED, GPIO.LOW)
    GPIO.output(RED_LED, GPIO.HIGH)

def is_package_installing():
    """
    check if an installation is processing by checking for active 'apt' or 'dpkg' processes
    """
    try:
        apt_process = subprocess.run(['pgrep', 'apt'], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        dpkg_process = subprocess.run(['pgrep', 'dpkg'], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if apt_process.stdout or dpkg_process.stdout:
            return True
    except subprocess.SubprocessError as e:
        print(f"Error checking installation processes: {e}")

    return False

def get_log_tail():
    """
    read the last line of upflux and dpkg log file 
    """
    try:
        result_dpkg = subprocess.run(['tail', '-n', '1', DPKG_LOG_PATH], stdout=subprocess.PIPE, text=True)
        result_uplflux = subprocess.run(['tail', '-n', '1', UPFLUX_LOG_PATH], stdout=subprocess.PIPE, text=True)
        return result_dpkg.stdout.lower(), result_uplflux.stdout.lower()
    except subprocess.SubprocessError as e:
        print(f"Error reading logs: {e}")

def is_installation_failed(log_data):
    return "error" in log_data[0] or "error" in log_data[1]

def is_installation_success(log_data):
    return "status installed" in log_data[0]

def is_rgb_sensor_warming_up():
    return GPIO.input(WARMUP_PIN) == GPIO.HIGH

try:
    while True:
        log_data = get_log_tail() 
        
        if is_rgb_sensor_warming_up(): 
            set_yellow_led()
        elif is_package_installing():
            set_yellow_led()
        elif is_installation_failed(log_data):
            set_red_led()
        elif is_installation_success(log_data):
            set_green_led()
        else:
            set_green_led()

        time.sleep(2)

except KeyboardInterrupt:
    GPIO.cleanup()

except Exception as e:
    print(f"An error occurred: {e}")
    GPIO.cleanup()

finally:
    GPIO.cleanup()
