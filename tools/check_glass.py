"""Measure the inactive card over two contrasting regions of the live desktop."""
import ctypes as c
import sys
import time
from pathlib import Path
import win32api
import win32gui
from PIL import ImageGrab, ImageStat

widget, folder, label = int(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
u = c.windll.user32
u.SetThreadDpiAwarenessContext(c.c_ssize_t(-4))
x,y,r,b = win32gui.GetWindowRect(widget)
width,height = r-x,b-y
work = win32api.GetMonitorInfo(win32api.MonitorFromWindow(widget))["Work"]
assert win32gui.GetWindow(widget,4), "Card lost its desktop owner"
active = win32gui.GetForegroundWindow() == widget
if label == "front": assert not active

def move(left, top):
    win32gui.SetWindowPos(widget,0,left,top,width,height,0x14)
    time.sleep(.55)

# Sample the right-hand interior margin, away from text, border and buttons.
def patch(left, top):
    return (left+width-30,top+height//3,left+width-21,top+height*2//3)
def brightness(image):
    return ImageStat.Stat(image.convert("L")).mean[0]
def capture():
    return ImageGrab.grab(work,all_screens=True)
def under(image,left,top):
    x0,y0,x1,y1=patch(left,top)
    return brightness(image.crop((x0-work[0]-10,y0-work[1]-10,x1-work[0]+10,y1-work[1]+10)))

try:
    # Select uncovered wallpaper samples without changing focus or window visibility.
    desktop=capture()
    positions=[(lx,ty) for lx in range(work[0]+8,work[2]-width-8,47)
               for ty in range(work[1]+8,work[3]-height-8,53)
               if lx+width<x-12 or lx>r+12 or ty+height<y-12 or ty>b+12]
    positions.sort(key=lambda pos:under(desktop,*pos))
    assert positions
    back=[under(desktop,*pos) for pos in (positions[0],positions[-1])]
    samples=[]
    for idx,(left,top) in enumerate((positions[0],positions[-1],positions[0])):
        move(left,top)
        at=(left+width-26,top+height//2)
        assert win32gui.GetAncestor(win32gui.WindowFromPoint(at),2)==widget
        samples.append(brightness(ImageGrab.grab(patch(left,top),all_screens=True)))
        if idx==1:
            ImageGrab.grab((left+10,top+2,left+width-10,top+height-10),all_screens=True).save(folder/f"glass-{label}.png")
    assert back[1]-back[0]>20 and samples[1]-samples[0]>3 and abs(samples[2]-samples[0])<6, (back,samples)
    print(f"PASS live glass (initially active={active}) {label}, wallpaper {back}, card {samples}")
finally:
    move(x,y)
