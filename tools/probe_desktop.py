"""Read the live Explorer topology and test ownership using a disposable hidden window."""
import ctypes as c
from ctypes import wintypes as w
u = c.WinDLL("user32", use_last_error=True)
callback = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
u.FindWindowExW.argtypes = [w.HWND, w.HWND, w.LPCWSTR, w.LPCWSTR]
u.FindWindowExW.restype = w.HWND
u.GetShellWindow.restype = w.HWND
u.GetWindowThreadProcessId.argtypes = [w.HWND,c.POINTER(w.DWORD)]
shellpid = w.DWORD()
u.GetWindowThreadProcessId(u.GetShellWindow(),c.byref(shellpid))
hosts=[]
@callback
def visit(hwnd, _):
    pid=w.DWORD()
    u.GetWindowThreadProcessId(hwnd,c.byref(pid))
    if pid.value == shellpid.value:
        view=u.FindWindowExW(hwnd,None,"SHELLDLL_DefView",None)
        if view: hosts.append(view)
    return True
u.EnumWindows(visit,0)
assert hosts, "Explorer desktop host unavailable in this session"
u.CreateWindowExW.argtypes=[w.DWORD,w.LPCWSTR,w.LPCWSTR,w.DWORD,c.c_int,c.c_int,c.c_int,c.c_int,w.HWND,w.HMENU,w.HINSTANCE,w.LPVOID]
u.CreateWindowExW.restype=w.HWND
u.GetWindow.argtypes=[w.HWND,w.UINT]
u.GetWindow.restype=w.HWND
u.SetWindowLongPtrW.argtypes=[w.HWND,c.c_int,c.c_ssize_t]
u.SetWindowLongPtrW.restype=c.c_ssize_t
hwnd=u.CreateWindowExW(0,"STATIC","Vocabulary probe",0x80000000,0,0,20,20,None,None,None,None)
assert hwnd
try:
    u.SetWindowLongPtrW(hwnd,-8,hosts[0])
    assert u.GetWindow(hwnd,4) == hosts[0], "Desktop owner rejected"
    print("Explorer desktop owner verified",hex(hosts[0]))
finally:
    u.DestroyWindow(w.HWND(hwnd))

