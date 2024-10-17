import RPi.GPIO as GPIO
import time

s0 = 23
s1 = 14
oe = 15
out = 18
s2 = 25
s3 = 24

NUM_CYCLES = 10  

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

    print("\n")

def loop():
    while True:
        
        GPIO.output(s2, GPIO.LOW)
        GPIO.output(s3, GPIO.LOW)
        time.sleep(0.3)
        
        start = time.time()
        for impulse_count in range(NUM_CYCLES):
            GPIO.wait_for_edge(out, GPIO.FALLING)
        duration = time.time() - start
        red_freq = NUM_CYCLES / duration
        print("Red frequency: ", red_freq)
        
        GPIO.output(s2, GPIO.HIGH)
        GPIO.output(s3, GPIO.HIGH)
        time.sleep(0.3)
        
        start = time.time()
        for impulse_count in range(NUM_CYCLES):
            GPIO.wait_for_edge(out, GPIO.FALLING)
        duration = time.time() - start
        green_freq = NUM_CYCLES / duration
        print("Green frequency: ", green_freq)
        
        GPIO.output(s2, GPIO.LOW)
        GPIO.output(s3, GPIO.HIGH)
        time.sleep(0.3)
        
        start = time.time()
        for impulse_count in range(NUM_CYCLES):
            GPIO.wait_for_edge(out, GPIO.FALLING)
        duration = time.time() - start
        blue_freq = NUM_CYCLES / duration
        print("Blue frequency: ", blue_freq)

        time.sleep(2)

if __name__ == '__main__':
    setup()
    try:
        loop()
    except KeyboardInterrupt:
        GPIO.cleanup()
