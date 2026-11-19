"""Run the extracted release with an AppContainer token granting no network capabilities."""
import ctypes as c
from ctypes import wintypes as w
from pathlib import Path
import argparse, os, subprocess, sqlite3, uuid, zipfile

root=Path.cwd()
parser=argparse.ArgumentParser()
parser.add_argument('--release',default='artifacts/LexiPane-v2.3.1-windows-x64-portable.zip')
args=parser.parse_args()
binary=(root/('.cache/offline-'+uuid.uuid4().hex)/'LexiPane.exe').resolve()
assert binary.is_relative_to(root/".cache")
with zipfile.ZipFile(root/args.release) as release:
    release.extractall(binary.parent)
name="VocabularyOfflineQA."+uuid.uuid4().hex
env=c.WinDLL("userenv",use_last_error=True)
kernel=c.WinDLL("kernel32",use_last_error=True)
adv=c.WinDLL("advapi32",use_last_error=True)
sid=c.c_void_p()
class Caps(c.Structure):
    _fields_=[("sid",c.c_void_p),("list",c.c_void_p),("count",w.DWORD),("unused",w.DWORD)]
class SI(c.Structure):
    _fields_=[("size",w.DWORD),("reserved",w.LPWSTR),("desktop",w.LPWSTR),("title",w.LPWSTR),("dims",w.DWORD*8),("show",w.WORD),("resCount",w.WORD),("res",c.c_void_p),("handles",w.HANDLE*3),("attrs",c.c_void_p)]
class PI(c.Structure):
    _fields_=[("process",w.HANDLE),("thread",w.HANDLE),("pid",w.DWORD),("tid",w.DWORD)]
def check(ok):
    if not ok: raise c.WinError(c.get_last_error())
def acl(*args):
    subprocess.run(["icacls",str(binary.parent),*args],check=True,stdout=subprocess.DEVNULL)
def run_ui(pid):
    ps=r'''
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$pidCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,PID_VALUE)
function Find($name){
    $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$name)
    $until=[DateTime]::UtcNow.AddSeconds(10)
    do {
        $owned=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$pidCondition)
        foreach($window in $owned){
            $item=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
            if($item){return $item}
        }
        Start-Sleep -Milliseconds 200
    } while([DateTime]::UtcNow -lt $until)
    throw "Missing control: $name"
}
function Act($name){
    $a=Find $name
    $pat=$null
    if($a.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pat)){$pat.Invoke()}else{$a.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()}
    Start-Sleep -Milliseconds 250
}
Act '开始使用'
Act '词库'
(Find '搜索单词或释义').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('放弃')
[void](Find 'abandon')
Act '今日学习'
Act '开始学习'
Act '查看释义'
Act '下一词'
Act '记得'
Act '挑战'
Act 'Wordle 猜词'
Act '开始新一轮'
(Find '挑战答案').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('apple')
Act '猜一次'
Act '揭晓答案'
[void](Find '成绩已保存')
'''.replace("PID_VALUE",str(pid))
    import base64
    subprocess.run(["powershell.exe","-NoProfile","-EncodedCommand",base64.b64encode(ps.encode("utf-16le")).decode()],check=True,timeout=75)
hr=env.CreateAppContainerProfile(name,name,"Vocabulary QA",None,0,c.byref(sid))
if hr: raise OSError(hex(hr & 0xffffffff))
sidtxt=w.LPWSTR()
pi=PI()
try:
    check(adv.ConvertSidToStringSidW(sid,c.byref(sidtxt)))
    folder=w.LPWSTR()
    ret=env.GetAppContainerFolderPath(sidtxt,c.byref(folder))
    if ret:raise OSError(hex(ret & 0xffffffff))
    data=Path(folder.value)/"LocalState"/"Study"
    os.environ["VOCABULARY_TEST_DATA"]=str(data)
    os.environ["DOTNET_ROOT"]=str(data/"absent")
    acl("/grant","*"+sidtxt.value+":(OI)(CI)RX")
    size=c.c_size_t()
    kernel.InitializeProcThreadAttributeList(None,1,0,c.byref(size))
    buf=c.create_string_buffer(size.value)
    check(kernel.InitializeProcThreadAttributeList(buf,1,0,c.byref(size)))
    caps=Caps(sid,None,0,0)
    check(kernel.UpdateProcThreadAttribute(buf,0,0x20009,c.byref(caps),c.sizeof(caps),None,None))
    si=SI();si.size=c.sizeof(si);si.attrs=c.addressof(buf)
    check(kernel.CreateProcessW(str(binary),None,None,None,False,0x80000,None,str(binary.parent),c.byref(si),c.byref(pi)))
    try:
        run_ui(pi.pid)
        with sqlite3.connect((data/"study.db").as_uri()+"?mode=ro",uri=True) as db:
            assert db.execute("select count(*) from reviews").fetchone()[0]==1
            assert db.execute("select count(*) from challenge_rounds").fetchone()[0]==1
        print("PASS no-network AppContainer: onboarding, bilingual lookup, study, Wordle dictionary and persisted challenge")
    finally:
        kernel.TerminateProcess(w.HANDLE(pi.process),0)
        kernel.WaitForSingleObject(w.HANDLE(pi.process),5000)
        kernel.CloseHandle(w.HANDLE(pi.thread));kernel.CloseHandle(w.HANDLE(pi.process))
finally:
    if sidtxt.value: acl("/remove:g","*"+sidtxt.value)
    env.DeleteAppContainerProfile(name)
