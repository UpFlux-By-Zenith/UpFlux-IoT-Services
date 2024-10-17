import RPi.GPIO as GPIO
import time
import logging
import datetime

s0 = 23
s1 = 14
oe = 15
out = 18
s2 = 25
s3 = 24
buzzer_pin = 26

log_file = '/var/log/upflux.log'
logging.basicConfig(filename=log_file, level=logging.INFO, 
                    format='%(asctime)s %(levelname)s:%(message)s')

NUM_CYCLES = 10 
 
FREQ_MIN = {'red': 500, 'green': 500, 'blue': 500}
FREQ_MAX = {'red': 3000, 'green': 3000, 'blue': 3000}  

RED_THRESHOLD = 200
GREEN_THRESHOLD = 100
BLUE_THRESHOLD = 100

def setup():
    GPIO.setmode(GPIO.BCM)
    GPIO.setup(out, GPIO.IN, pull_up_down=GPIO.PUD_UP)
    GPIO.setup(s0, GPIO.OUT)
    GPIO.setup(s1, GPIO.OUT)
    GPIO.setup(s2, GPIO.OUT)
    GPIO.setup(s3, GPIO.OUT)
    GPIO.setup(oe, GPIO.OUT)
    
    GPIO.output(oe, GPIO.LOW)
    GPIO.output(s0, GPIO.HIGH)
    GPIO.output(s1, GPIO.LOW)
    GPIO.setup(buzzer_pin, GPIO.OUT)

    global pwm
    pwm = GPIO.PWM(buzzer_pin, 1000) 

    print("\n")
    
def buzzer_condition(red_value, green_value, blue_value):
    return red_value > RED_THRESHOLD and green_value < GREEN_THRESHOLD and blue_value < BLUE_THRESHOLD

def activate_buzzer():
    pwm.start(50)  
    time.sleep(1)  
    pwm.stop()
    
def map_frequency_to_rgb(freq, color):
    if freq < FREQ_MIN[color]:
        freq = FREQ_MIN[color]
    elif freq > FREQ_MAX[color]:
        freq = FREQ_MAX[color]
    return int((freq - FREQ_MIN[color]) / (FREQ_MAX[color] - FREQ_MIN[color]) * 255)  

def loop():
    while True:
        try:
            current_time = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')

            GPIO.output(s2, GPIO.LOW)
            GPIO.output(s3, GPIO.LOW)
            time.sleep(0.3)
            
            start = time.time()
            for impulse_count in range(NUM_CYCLES):
                GPIO.wait_for_edge(out, GPIO.FALLING)
            duration = time.time() - start
            red_freq = NUM_CYCLES / duration
            print("red frequency: ", red_freq)
            
            GPIO.output(s2, GPIO.HIGH)
            GPIO.output(s3, GPIO.HIGH)
            time.sleep(0.3)
            
            start = time.time()
            for impulse_count in range(NUM_CYCLES):
                GPIO.wait_for_edge(out, GPIO.FALLING)
            duration = time.time() - start
            green_freq = NUM_CYCLES / duration
            print("green frequency: ", green_freq)
            
            GPIO.output(s2, GPIO.LOW)
            GPIO.output(s3, GPIO.HIGH)
            time.sleep(0.3)
            
            start = time.time()
            for impulse_count in range(NUM_CYCLES):
                GPIO.wait_for_edge(out, GPIO.FALLING)
            duration = time.time() - start
            blue_freq = NUM_CYCLES / duration
            print("blue frequency: ", blue_freq)

            red_value = map_frequency_to_rgb(red_freq, 'red')
            blue_value = map_frequency_to_rgb(blue_freq, 'blue')
            green_value = map_frequency_to_rgb(green_freq, 'green')
            
            print("RGB Values -> {}, {}, {}".format(red_value, green_value, blue_value))
            
            if buzzer_condition(red_value, green_value, blue_value):
                logging.info(f"Time: {current_time} - buzzer activated due to color condition (R > {RED_THRESHOLD}, G < {GREEN_THRESHOLD}, B < {BLUE_THRESHOLD})")
                activate_buzzer()

            time.sleep(2)
            
        except Exception as e:
            current_time = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
            logging.error(f"Time: {current_time} - error occurred: {e}")
            print(f"An error occurred: {e}")
            break

def endprogram():
    GPIO.cleanup()

if __name__ == '__main__':
    setup()
    try:
        loop()
    except KeyboardInterrupt:
        GPIO.cleanup()
    finally:
        endprogram()
