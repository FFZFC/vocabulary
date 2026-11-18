"""Interactive QA only. Temporarily change and restore display settings for the test window."""
import collections
import ctypes as c
from ctypes import wintypes as w
import pathlib
import sys
import time
import winreg

import win32api
import win32gui
import win32ui
from PIL import Image

u = c.windll.user32
u.SetThreadDpiAwarenessContext(c.c_ssize_t(-4))
hwnd = int(sys.argv[1])
folder = pathlib.Path(sys.argv[2])
widget = int(sys.argv[3]) if len(sys.argv)>3 else None
device = win32api.GetMonitorInfo(win32api.MonitorFromWindow(hwnd))["Device"]
theme_key = r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"

class Header(c.Structure):
    _fields_ = [("kind", c.c_int), ("size", w.DWORD), ("adapter", w.DWORD*2), ("source", w.DWORD)]
class Levels(c.Structure):
    _fields_ = [("head", Header), ("low", c.c_int), ("current", c.c_int), ("high", c.c_int)]
class SetLevel(c.Structure):
    _fields_ = [("head", Header), ("level", c.c_int)]
class SourceName(c.Structure):
    _fields_ = [("head", Header), ("name", c.c_wchar*32)]
class Contrast(c.Structure):
    _fields_ = [("size", w.UINT), ("flags", w.DWORD), ("scheme", w.LPWSTR)]

def device_packet(kind, typ, adapter, source):
    obj = typ()
    obj.head = Header(kind, c.sizeof(typ), adapter, source)
    return obj

def display():
    n, m = w.UINT(), w.UINT()
    assert not u.GetDisplayConfigBufferSizes(2, c.byref(n), c.byref(m))
    paths, modes = c.create_string_buffer(n.value*72), c.create_string_buffer(m.value*64)
    assert not u.QueryDisplayConfig(2, c.byref(n), paths, c.byref(m), modes, None)
    for i in range(n.value):
        adapter = (w.DWORD*2).from_buffer_copy(paths.raw[i*72:i*72+8])
        src = int.from_bytes(paths.raw[i*72+8:i*72+12], "little")
        name = device_packet(1, SourceName, adapter, src)
        if u.DisplayConfigGetDeviceInfo(c.byref(name)) == 0 and device == name.name:
            packet = device_packet(-3, Levels, adapter, src)
            assert not u.DisplayConfigGetDeviceInfo(c.byref(packet))
            return adapter, src, packet
    raise RuntimeError("Test monitor not found")

def scale(adapter, src, value):
    packet = device_packet(-4, SetLevel, adapter, src)
    packet.level = value
    code = u.DisplayConfigSetDeviceInfo(c.byref(packet))
    if code: raise OSError(code, "DisplayConfigSetDeviceInfo")
    time.sleep(1.5)

def contrast():
    item = Contrast(c.sizeof(Contrast),0,None)
    assert u.SystemParametersInfoW(0x42,item.size,c.byref(item),0)
    return item.flags, item.scheme

def set_contrast(flags, scheme):
    item = Contrast(c.sizeof(Contrast),flags,scheme)
    assert u.SystemParametersInfoW(0x43,item.size,c.byref(item),3)
    time.sleep(1.8)

def set_theme(key, value):
    if value is None:
        try: winreg.DeleteValue(key, "AppsUseLightTheme")
        except FileNotFoundError: pass
    else: winreg.SetValueEx(key,"AppsUseLightTheme",0,winreg.REG_DWORD,value)
    u.SendMessageTimeoutW(0xffff,0x1a,0,c.c_wchar_p("ImmersiveColorSet"),2,250,c.byref(c.c_size_t()))
    time.sleep(1.5)

def snapshot(name, target=hwnd):
    left,top,right,bottom = win32gui.GetWindowRect(target)
    width,height = right-left,bottom-top
    dc = win32gui.GetWindowDC(target)
    src = win32ui.CreateDCFromHandle(dc)
    mem = src.CreateCompatibleDC()
    bmp = win32ui.CreateBitmap()
    bmp.CreateCompatibleBitmap(src,width,height)
    mem.SelectObject(bmp)
    try:
        assert u.PrintWindow(target, mem.GetSafeHdc(), 2)
        img=Image.frombuffer("RGB",(width,height),bmp.GetBitmapBits(True),"raw","BGRX",0,1)
        img.save(folder/(name+".png"))
        pix=collections.Counter(img.resize((120,80)).getdata())
        assert len(pix)>20
        return pix.most_common(1)[0][0]
    finally:
        mem.DeleteDC(); src.DeleteDC()
        win32gui.ReleaseDC(target,dc); win32gui.DeleteObject(bmp.GetHandle())

adapter,source,original=display()
flags,scheme=contrast()
dpi_table=[100,125,150,175,200,225,250,300,350,400,450,500]
with winreg.OpenKey(winreg.HKEY_CURRENT_USER,theme_key,0,winreg.KEY_READ|winreg.KEY_SET_VALUE) as key:
    try: previous=winreg.QueryValueEx(key,"AppsUseLightTheme")[0]
    except FileNotFoundError: previous=None
    try:
        for dpi in (125,150,200):
            idx=dpi_table.index(dpi)+original.low
            if not original.low<=idx<=original.high:
                print(f"Unavailable on test monitor: {dpi}%")
                continue
            scale(adapter,source,idx)
            assert u.GetDpiForWindow(hwnd)==round(96*dpi/100)
            snapshot("scale-"+str(dpi))
            if widget: snapshot("widget-scale-"+str(dpi),widget)
            print(f"PASS live DPI {dpi}%")
    finally:
        scale(adapter,source,original.current)
        assert display()[2].current==original.current
    try:
        if flags&1:set_contrast(flags&~1,scheme)
        set_theme(key,0);dark=snapshot("dark")
        if widget: snapshot("widget-dark",widget)
        set_theme(key,1);light=snapshot("light")
        if widget: snapshot("widget-light",widget)
        print("Theme pixel samples",dark,light)
        set_contrast(flags|1,scheme)
        assert contrast()[0]&1
        snapshot("high-contrast")
        if widget: snapshot("widget-high-contrast",widget)
        print("PASS high contrast active")
        assert dark!=light, "App did not repaint light/dark in response to system setting"
    finally:
        set_contrast(flags,scheme)
        set_theme(key,previous)
        assert contrast()[0]&1==flags&1
print("PASS original scale, contrast and app theme restored")
