# Run with Windows PowerShell in an interactive session. Only targets the test instance.
param([string]$AppExe = 'artifacts\LexiPane-v2.3.1-windows-x64-portable\LexiPane.exe',[switch]$SystemChecks,[switch]$GoalOnly,[switch]$NotebookOnly,[switch]$ChallengeOnly)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DesktopCheck {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(string cls,string text);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr GetWindow(IntPtr h,uint cmd);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(Pt p);
 [DllImport("user32.dll")]public static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr h,uint msg,IntPtr wp,IntPtr lp);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr info);
 [DllImport("user32.dll")]public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
 [DllImport("user32.dll")]public static extern uint RegisterWindowMessage(string name);
 public delegate bool Visit(IntPtr h,IntPtr l);
 [DllImport("user32")] public static extern bool EnumWindows(Visit f,IntPtr l);
 [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h,out int pid);
 [DllImport("user32",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder text,int len);
 public static IntPtr FindByTitle(string s,int target){IntPtr found=IntPtr.Zero;EnumWindows(delegate(IntPtr h,IntPtr l){int pid;GetWindowThreadProcessId(h,out pid);if(pid==target){var b=new StringBuilder(256);GetWindowText(h,b,256);if(b.ToString()==s){found=h;return false;}}return true;},IntPtr.Zero);return found;} public static IntPtr Popup(){return FindWindow("#32768",null);} public struct Pt{public int x,y;public Pt(int a,int b){x=a;y=b;}}
 [DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]public static extern bool GetMenuItemRect(IntPtr h,IntPtr m,uint i,out Rect r);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
 [StructLayout(LayoutKind.Sequential)]public struct IconId{public uint cbSize;public IntPtr hwnd;public uint uID;public Guid guid;}
 [DllImport("shell32.dll")]public static extern int Shell_NotifyIconGetRect(ref IconId id,out Rect rect);
 public static bool HasTray(IntPtr hwnd){IconId id=new IconId();id.cbSize=(uint)Marshal.SizeOf(typeof(IconId));id.hwnd=hwnd;id.uID=1;Rect rect;return Shell_NotifyIconGetRect(ref id,out rect)>=0;}
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 public struct Rect{public int l,t,r,b;}
}
'@
[void][DesktopCheck]::SetThreadDpiAwarenessContext([IntPtr](-4))
$exe=(Resolve-Path -LiteralPath $AppExe).Path
$qa=Join-Path (Resolve-Path '.cache') ("gui-"+[Guid]::NewGuid().ToString('N'))
$env:VOCABULARY_TEST_DATA=$qa
$env:DOTNET_MULTILEVEL_LOOKUP='0'
$env:DOTNET_ROOT=(Join-Path $qa 'empty-runtime')
function Await([scriptblock]$Read,[string]$Name) {
    $end=[DateTime]::UtcNow.AddSeconds(12)
    do {
        $v=& $Read
        if($v -and !($v -is [IntPtr] -and $v -eq [IntPtr]::Zero)){return $v}
        Start-Sleep -Milliseconds 150
    } while([DateTime]::UtcNow -lt $end)
    throw "Timeout: $Name"
}
function FindItem([IntPtr]$H,[string]$Name){
    $root=[System.Windows.Automation.AutomationElement]::FromHandle($H)
    $cond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$Name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$cond)
}
function FindId([IntPtr]$H,[string]$Id){
    $root=[System.Windows.Automation.AutomationElement]::FromHandle($H)
    $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$Id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
function Click([IntPtr]$H,[string]$Name){
    $item=Await {FindItem $H $Name} $Name
    $pat=$null
    if($item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pat)){$pat.Invoke()}
    elseif($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pat)){$pat.Select()}
    elseif($item.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern,[ref]$pat)){$pat.Expand()}
    else {throw "Control cannot be invoked: $Name"}
    Start-Sleep -Milliseconds 300
}
function Window([string]$Title){return Await {[DesktopCheck]::FindByTitle($Title,$proc.Id)} $Title}
function Assert($Test,[string]$Label){if(!$Test){throw $Label};Write-Output ("PASS "+$Label)}
function Snap([IntPtr]$H,[string]$Name){
    $r=New-Object DesktopCheck+Rect
    [void][DesktopCheck]::GetWindowRect($H,[ref]$r)
    $bmp=New-Object Drawing.Bitmap ($r.r-$r.l),($r.b-$r.t)
    $g=[Drawing.Graphics]::FromImage($bmp)
    $dc=$g.GetHdc()
    [void][DesktopCheck]::PrintWindow($H,$dc,2)
    $g.ReleaseHdc($dc);$g.Dispose()
    $bmp.Save((Join-Path $qa $Name));$bmp.Dispose()
}
function TrayClick([IntPtr]$H,[int]$event){
    [void][DesktopCheck]::PostMessage($H,0x8043,[IntPtr]1,[IntPtr]$event)
    Start-Sleep -Milliseconds 400
}
$proc=$null
try {
    if($ChallengeOnly){
        python tools/challenge_fixture.py $qa
        if($LASTEXITCODE){throw 'Could not create isolated challenge fixture'}
    }
    elseif(!$GoalOnly -and !$NotebookOnly){
        python tools/seed_history.py $qa
        if($LASTEXITCODE){throw 'Could not create isolated test history'}
    }
    $proc=Start-Process -FilePath $exe -PassThru
    $main=Window 'LexiPane'
    if($GoalOnly){
        $root=[System.Windows.Automation.AutomationElement]::FromHandle($main)
        $edits=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit)
        $edit=Await {$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$edits)} 'daily target input'
        $edit.SetFocus()
        $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('1')
    }
    Click $main '开始使用'
    $widget=Window 'LexiPane · 桌面卡片'
    $owner=Await {[DesktopCheck]::GetWindow($widget,4)} "desktop attachment"
    Assert ($owner -ne [IntPtr]::Zero) 'desktop owner'
    if($ChallengeOnly){
        . (Join-Path $PSScriptRoot 'challenge_cases.ps1')
        Write-Output "QA artifacts: $qa"
        exit 0
    }
    if($NotebookOnly){
        Click $main '开始学习'
        $word=(FindId $main 'StudyWord').Current.Name
        Click $widget '加入生词本'
        [void](Await {FindItem $main '移出生词本'} 'star synchronized')
        Click $main '记得'
        $second=(FindId $widget 'StudyWord').Current.Name
        Click $main '加入生词本'
        Click $main '生词本'
        [void](Await {FindItem $main '已收藏 2 词'} 'two bookmarks')
        Click $main "查看词条 $word"
        $edit=Await {FindId $main 'WordNote'} 'note editor'
        $hint=if($word -eq 'state'){'state of mind 指心境。作动词时，state 可以表示“陈述”。'}else{'用一个短语记住这个词。'}
        $edit.SetFocus()
        $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($hint)
        Click $main '保存笔记'
        $search=Await {FindItem $main '搜索单词、释义或笔记'} 'notebook search'
        $searchValue=$search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $searchValue.SetValue($hint)
        [void](Await {FindItem $main "查看词条 $word"} 'find by note')
        Assert (!(FindItem $main "查看词条 $second")) 'note search filters other words'
        $searchValue.SetValue('')
        [void](Await {FindItem $main "查看词条 $second"} 'notebook search cleared')
        Start-Sleep -Milliseconds 450
        Snap $main 'notebook.png'
        Click $main "查看词条 $word"
        [void](Await {FindItem $main '学习记录　1 次'} 'per-word review history')
        Snap $main 'word-detail.png'
        Click $main '关闭'
        Click $main '练习生词本'
        [void](Await {FindItem $widget '生词本练习'} 'notebook practice in widget')
        Assert ((FindId $main 'StudyWord').Current.Name -eq $second) 'newest bookmark first'
        Click $widget '记得'
        Click $main '查看释义'
        [void](Await {FindItem $widget $hint} 'saved note on reverse side')
        Snap $widget 'notebook-card.png'
        Click $widget '下一词'
        [void](Await {FindItem $main '本轮已浏览完'} 'finite notebook round')
        Click $main '返回日常学习'
        [void](Await {FindItem $widget '英译中'} 'resume daily round')
        Stop-Process -Id $proc.Id
        $proc.WaitForExit()
        $proc=Start-Process -FilePath $exe -PassThru
        $main=Window 'LexiPane'
        Click $main '生词本'
        [void](Await {FindItem $main '已收藏 2 词'} 'bookmarks persisted')
        Click $main "查看词条 $word"
        $edit=Await {FindId $main 'WordNote'} 'persisted note editor'
        Assert ($edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $hint) 'note persisted after restart'
        Click $main '移出生词本'
        Click $main '关闭'
        [void](Await {FindItem $main '已收藏 1 词'} 'removed from notebook')
        Write-Output "PASS cross-window bookmarks, note search, detail history, notebook practice and restart"
        Write-Output "QA artifacts: $qa"
        exit 0
    }
    if($GoalOnly){
        Click $main '开始学习'
        $word=(FindId $main 'StudyWord').Current.Name
        Click $widget '记得'
        Assert ((FindId $main 'StudyWord').Current.Name -ne $word) 'goal reached advances automatically'
        [void](Await {FindItem $main '1 / 1'} 'main target count')
        [void](Await {FindItem $widget '今日目标已达成，可继续学习'} 'widget goal hint')
        [void](Await {FindItem $main '今日目标已达成，可继续学习'} 'main goal hint')
        Click $main '记得'
        [void](Await {FindItem $widget '四级 · 今日新学 2 / 1'} 'over-goal widget count')
        Click $widget '查看释义'
        [void](Await {FindItem $main '2 / 1'} 'neutral lookup preserves count')
        Click $main '下一词'
        Click $widget '不记得'
        [void](Await {FindItem $main '3 / 1'} 'over-goal forgotten word')
        Click $main '撤销最近一次评分'
        [void](Await {FindItem $widget '四级 · 今日新学 2 / 1'} 'undo updates both windows')
        Click $main '记得'
        [void](Await {FindItem $main '3 / 1'} 'learning continues after undo')
        Snap $main 'goal-main.png'
        Snap $widget 'goal-widget.png'
        Click $main '学习统计'
        [void](Await {FindItem $main '3'} 'statistics include three new words')
        Snap $main 'goal-statistics.png'
        Stop-Process -Id $proc.Id
        $proc.WaitForExit()
        $proc=Start-Process -FilePath $exe -PassThru
        $main=Window 'LexiPane'
        [void](Await {FindItem $main '3 / 1'} 'restart retains actual progress and goal')
        Click $main '开始学习'
        Click $main '记得'
        [void](Await {FindItem $main '4 / 1'} 'restart still permits unlimited study')
        Write-Output "PASS continuous study, synchronized goal hints, neutral lookup, undo, statistics and restart"
        Write-Output "QA artifacts: $qa"
        exit 0
    }
    Snap $main 'overview.png'
    $installDir=[IO.Path]::GetDirectoryName($exe)
    $nativeModules=(Get-Process -Id $proc.Id).Modules
    foreach($moduleName in 'coreclr.dll','Microsoft.ui.xaml.dll'){
        $module=$nativeModules | Where-Object ModuleName -eq $moduleName | Select-Object -First 1
        Assert ($module -and [IO.Path]::GetDirectoryName($module.FileName) -eq $installDir) "app-local $moduleName"
    }
    if($SystemChecks) {
        python tools/check_display.py $main.ToInt64() $qa $widget.ToInt64()
        if($LASTEXITCODE){throw 'Display acceptance failed; display settings restored by helper.'}
        $folders=@((New-Object -ComObject Shell.Application).Windows() | Where-Object {$_.FullName -like '*\explorer.exe'} | ForEach-Object { try {$_.Document.Folder.Self.Path} catch {} })
        $shellProcess=0
        [void][DesktopCheck]::GetWindowThreadProcessId([DesktopCheck]::GetShellWindow(),[ref]$shellProcess)
        $shellExe=Join-Path $env:windir 'explorer.exe'
        $shellProc=Get-Process -Id $shellProcess
        if($shellProc.Path -ne $shellExe){throw 'Unexpected shell process'}
        try {
            Stop-Process -Id $shellProc.Id
            Start-Sleep -Seconds 3
            if([DesktopCheck]::GetShellWindow() -eq [IntPtr]::Zero){Start-Process -FilePath $shellExe}
            $newShell=Await {[DesktopCheck]::GetShellWindow()} 'Explorer recovery'
            $newPid=0
            [void][DesktopCheck]::GetWindowThreadProcessId($newShell,[ref]$newPid)
            Assert ($newPid -ne $shellProcess) 'real Explorer restart'
            $widget=Window 'LexiPane · 桌面卡片'
            [void](Await {
                $actual=[DesktopCheck]::GetWindow($widget,4)
                $ownerPid=0
                [void][DesktopCheck]::GetWindowThreadProcessId($actual,[ref]$ownerPid)
                $ownerPid -eq $newPid
            } 'attachment to restarted Explorer')
            [void](Await {[DesktopCheck]::HasTray($main)} 'tray icon registration after Explorer restart')
            Assert $true 'card and tray recovered'
        } finally {
            if([DesktopCheck]::GetShellWindow() -eq [IntPtr]::Zero){Start-Process -FilePath $shellExe;Start-Sleep -Seconds 3}
            $shellApp=New-Object -ComObject Shell.Application
            $open=@($shellApp.Windows() | ForEach-Object {try {$_.Document.Folder.Self.Path} catch {}})
            foreach($folder in $folders){if($folder -and $folder -notin $open){$shellApp.Explore($folder)}}
        }
    }
    Click $main '词库'
    $search=Await {FindItem $main '搜索单词或释义'} 'search box'
    $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('abandon')
    [void](Await {FindItem $main 'abandon'} 'English search')
    $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('放弃')
    [void](Await {FindItem $main 'abandon'} 'Chinese search')
    Assert $true 'English and Chinese dictionary search'
    Click $main '今日学习'
    Click $main '开始学习'
    [void][DesktopCheck]::SetForegroundWindow($main)
    if ([DesktopCheck]::GetForegroundWindow() -ne $main) {
        $caption = New-Object DesktopCheck+Rect
        [void][DesktopCheck]::GetWindowRect($main,[ref]$caption)
        $point = New-Object DesktopCheck+Pt ([int](($caption.l+$caption.r)/2)),($caption.t+20)
        if ([DesktopCheck]::GetAncestor([DesktopCheck]::WindowFromPoint($point),2) -eq $main) {
            [void][DesktopCheck]::SetCursorPos($point.x,$point.y)
            [DesktopCheck]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
            [DesktopCheck]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
        }
    }
    [void](Await {[DesktopCheck]::GetForegroundWindow() -eq $main} 'keyboard target')
    (FindItem $main '记得').SetFocus()
    [Windows.Forms.SendKeys]::SendWait(' ')
    [void](Await {FindItem $widget '仅查看'} 'space peeks')
    [Windows.Forms.SendKeys]::SendWait(' ')
    [void](Await {FindItem $widget '记得'} 'space continues')
    [Windows.Forms.SendKeys]::SendWait('2')
    [void](Await {FindItem $widget '已记录为不记得'} 'keyboard forget')
    Click $main '撤销最近一次评分'
    (FindItem $main '记得').SetFocus()
    [Windows.Forms.SendKeys]::SendWait('1')
    [void](Await {FindItem $widget '撤销最近一次评分'} 'keyboard remember')
    Click $main '撤销最近一次评分'
    Click $widget '查看释义'
    [void](Await {FindItem $main '仅查看'} 'neutral lookup sync')
    Click $main '下一词'
    Click $widget '不记得'
    [void](Await {FindItem $main '已记录为不记得'} 'forget synchronizes')
    Click $main '撤销最近一次评分'
    [void](Await {FindItem $widget '记得'} 'undo returns to front')
    Click $main '不记得'
    Snap $main 'study.png'
    Click $widget '下一词'
    Click $main '遗忘词'
    Click $main '开始专项复习'
    [void](Await {FindItem $main '遗忘词复习'} 'practice mode')
    Click $widget '记得'
    Click $main '返回概览'
    Click $main '学习统计'
    [void](Await {FindItem $main '近 12 周学习日历'} 'heatmap')
    Snap $main 'statistics.png'
    Click $main '设置'
    $slider=FindItem $main '毛玻璃浓度'
    $range=$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $range.SetValue(25)
    $range.SetValue(50)
    Click $main '今日学习'
    Click $main '开始学习'
    Assert $true 'neutral lookup, instant grading, undo, targeted practice and charts'
    $again=Start-Process -FilePath $exe -PassThru
    Assert ($again.WaitForExit(8000)) 'second instance redirects'
    [void][DesktopCheck]::PostMessage($main,0x10,[IntPtr]0,[IntPtr]0)
    [void](Await {![DesktopCheck]::IsWindowVisible($main)} 'hide main')
    Assert ([DesktopCheck]::IsWindowVisible($widget)) 'close to tray keeps widget'
    TrayClick $main 0x202
    [void](Await {[DesktopCheck]::IsWindowVisible($main)} 'tray restore')
    Assert $true 'tray restores main'
    $wr=New-Object DesktopCheck+Rect
    $mr=New-Object DesktopCheck+Rect
    [void][DesktopCheck]::GetWindowRect($widget,[ref]$wr)
    [void][DesktopCheck]::GetWindowRect($main,[ref]$mr)
    try {
        [void][DesktopCheck]::SetWindowPos($main,[IntPtr]0,($wr.l-20),($wr.t-20),($wr.r-$wr.l+60),($wr.b-$wr.t+60),0x40)
        Start-Sleep -Milliseconds 300
        $hitPoint=New-Object DesktopCheck+Pt ($wr.l+40),($wr.t+50)
        Assert ([DesktopCheck]::GetAncestor([DesktopCheck]::WindowFromPoint($hitPoint),2) -eq $main) 'ordinary window covers card'
    } finally {
        [void][DesktopCheck]::SetWindowPos($main,[IntPtr]0,$mr.l,$mr.t,($mr.r-$mr.l),($mr.b-$mr.t),0x14)
    }
    [void][DesktopCheck]::SetWindowPos($widget,[IntPtr]0,45000,45000,0,0,0x15)
    [void][DesktopCheck]::PostMessage($main,0x7e,[IntPtr]32,[IntPtr]0)
    [void](Await {$r=New-Object DesktopCheck+Rect;[void][DesktopCheck]::GetWindowRect($widget,[ref]$r);$r.l -lt 45000} 'card recovered from missing display')
    [void][DesktopCheck]::SetWindowPos($widget,[IntPtr]0,$wr.l,$wr.t,0,0,0x15)
    Assert $true 'off-screen position recovered on display change'
    $shell=New-Object -ComObject Shell.Application
    $shell.ToggleDesktop()
    try{
        Start-Sleep -Milliseconds 900
        $r=New-Object DesktopCheck+Rect
        [void][DesktopCheck]::GetWindowRect($widget,[ref]$r)
        $point=New-Object DesktopCheck+Pt ([int](($r.l+$r.r)/2)),([int](($r.t+$r.b)/2))
        $hit=[DesktopCheck]::GetAncestor([DesktopCheck]::WindowFromPoint($point),2)
        Assert ($hit -eq $widget) 'Win+D desktop hit-test'
        Snap $widget 'widget.png'
        python tools/check_glass.py $widget.ToInt64() $qa front
        if($LASTEXITCODE){throw 'Inactive desktop glass failed'}

        $button=FindItem $widget '查看释义'
        $b=$button.Current.BoundingRectangle
        [void][DesktopCheck]::SetCursorPos([int]($b.X+$b.Width/2),[int]($b.Y+$b.Height/2))
        [DesktopCheck]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
        [DesktopCheck]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
        [void](Await {FindItem $widget '仅查看'} 'physical click on desktop card')
        $header=(FindId $widget 'WidgetGoalProgress').Current.BoundingRectangle
        $dragX=[int]($header.X+20)
        $dragY=[int]($header.Y+$header.Height/2)
        [void][DesktopCheck]::SetCursorPos($dragX,$dragY)
        [DesktopCheck]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
        Start-Sleep -Milliseconds 120
        [void][DesktopCheck]::SetCursorPos(($dragX-25),($dragY+25))
        Start-Sleep -Milliseconds 120
        [void][DesktopCheck]::SetCursorPos(($dragX-65),($dragY+65))
        Start-Sleep -Milliseconds 160
        [DesktopCheck]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
        $moved=New-Object DesktopCheck+Rect
        [void][DesktopCheck]::GetWindowRect($widget,[ref]$moved)
        Assert ($moved.l -ne $r.l -or $moved.t -ne $r.t) 'drag on desktop'
        python tools/check_glass.py $widget.ToInt64() $qa dragged-definition
        if($LASTEXITCODE){throw 'Desktop glass after drag failed'}
    } finally {$shell.ToggleDesktop()}
    TrayClick $main 0x205
    $menu=Await {[DesktopCheck]::Popup()} 'tray context menu'
    $handle=[DesktopCheck]::SendMessage($menu,0x1e1,[IntPtr]0,[IntPtr]0)
    $choice=New-Object DesktopCheck+Rect
    Assert ([DesktopCheck]::GetMenuItemRect([IntPtr]0,$handle,3,[ref]$choice)) 'native tray menu'
    [void][DesktopCheck]::SetCursorPos([int](($choice.l+$choice.r)/2),[int](($choice.t+$choice.b)/2))
    [DesktopCheck]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    [DesktopCheck]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Assert ($proc.WaitForExit(6000)) 'tray exit'
    $proc=Start-Process -FilePath $exe -PassThru
    $main=Window 'LexiPane'
    [void](Await {FindItem $main '开始学习'} 'reopen')
    Assert (!(FindItem $main '开始使用')) 'onboarding persisted'
    Write-Output "QA artifacts: $qa"
} catch {
    if($proc -and !$proc.HasExited){
        $pc=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$proc.Id)
        $windows=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$pc)
        foreach($w in $windows){$w.Current.Name; $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } | Select-Object -First 30}
    }
    throw
} finally {
    if($proc -and !$proc.HasExited){Stop-Process -Id $proc.Id}
}
