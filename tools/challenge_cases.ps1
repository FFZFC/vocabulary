# Executed by desktop_check.ps1 -ChallengeOnly with that script's isolated process and helpers.
$catalog=Get-Content -LiteralPath (Join-Path $qa 'catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
function SelectChoice([string]$Id,[string]$Name){
    $combo=Await {FindId $main $Id} $Id
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 180
    $item=Await {FindItem $main $Name} $Name
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 200
}
function Fill([string]$Text){
    $edit=Await {FindId $main 'ChallengeInput'} 'challenge input'
    $edit.SetFocus()
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
}
function CurrentAnswer {
    $prompt=(Await {FindId $main 'ChallengePrompt'} 'challenge prompt').Current.Name
    return ($catalog | Where-Object prompt -eq $prompt).word
}
function HubGame([string]$Name){
    Click $main '挑战首页'
    Click $main $Name
}
Click $main '挑战'
Snap $main 'challenge-hub.png'
Click $main '中文默写'
SelectChoice 'ChallengeScope' '生词本'
[void](Await {FindItem $main '四级 · 可用 4 词'} 'candidate count')
Click $main '开始新一轮'
Snap $main 'dictation.png'
$first=CurrentAnswer
Fill $first.Substring(0,1)
Click $main '词库'
Click $main '挑战'
Assert ((FindId $main 'ChallengeInput').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $first.Substring(0,1)) 'input and question survive navigation'
[void][DesktopCheck]::PostMessage($main,0x10,[IntPtr]0,[IntPtr]0)
[void](Await {![DesktopCheck]::IsWindowVisible($main)} 'main hidden during game')
TrayClick $main 0x202
[void](Await {[DesktopCheck]::IsWindowVisible($main)} 'main restored during game')
Click $main '提示首字母'
Fill $first.ToUpperInvariant()
(FindId $main 'ChallengeInput').SetFocus()
[void][DesktopCheck]::SetForegroundWindow($main)
[Windows.Forms.SendKeys]::SendWait('{ENTER}')
[void](Await {FindItem $main '提示后答对'} 'keyboard submission')
Click $main '下一题'
Fill 'wrong'
Click $main '提交答案'
Click $main '下一题'
for($i=0;$i -lt 2;$i++){
    Fill (CurrentAnswer)
    Click $main '提交答案'
    if($i -eq 0){Click $main '下一题'}else{Click $main '查看本轮成绩'}
}
[void](Await {FindItem $main '成绩已保存'} 'dictation result saved')
Snap $main 'challenge-results.png'
Click $main '重练未独立答对的词'
[void](Await {FindItem $main '第 1 / 2 词'} 'wrong and assisted words retried')
HubGame '字母拼词'
Click $main '开始新一轮'
Snap $main 'scramble.png'
for($i=0;$i -lt 4;$i++){
    Fill (CurrentAnswer)
    Click $main '提交答案'
    if($i -lt 3){Click $main '下一题'}else{Click $main '查看本轮成绩'}
}
[void](Await {FindItem $main '成绩已保存'} 'scramble result saved')
HubGame '中英配对'
Click $main '开始新一轮'
Snap $main 'matching.png'
for($i=0;$i -lt 4;$i++){
    $left=FindId $main "MatchEnglish$i"
    $left.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 130
    $right=FindId $main "MatchMeaning$i"
    $right.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 200
}
[void](Await {FindItem $main '成绩已保存'} 'matching result saved')
HubGame 'Wordle 猜词'
Click $main '开始新一轮'
Fill 'zzzzz'
Click $main '猜一次'
[void](Await {FindItem $main '本地词典未收录这个词，请换一个。'} 'invalid guess')
Fill 'apple'
Click $main '猜一次'
Fill 'apple'
Click $main '猜一次'
[void](Await {FindItem $main '已经猜过这个词，请换一个。'} 'duplicate guess')
Fill 'state'
Click $main '猜一次'
[void](Await {FindItem $main '猜中了！用了 2 次机会'} 'Wordle win')
Snap $main 'wordle-win.png'
Click $main '新局设置'
SelectChoice 'ChallengeSize' '4 字母'
Click $main '开始新一轮'
foreach($guess in 'able','book','home','life','work','word'){
    Fill $guess
    Click $main '猜一次'
}
[void](Await {FindItem $main '本局机会已用完'} 'Wordle loss after six')
Click $main '新局设置'
SelectChoice 'ChallengeSize' '6 字母'
Click $main '开始新一轮'
Fill 'system'
Click $main '猜一次'
[void](Await {FindItem $main '猜中了！用了 1 次机会'} 'six-letter Wordle')
Click $main '新局设置'
SelectChoice 'ChallengeSize' '7 字母'
Click $main '开始新一轮'
Fill 'about'
Click $main '猜一次'
Snap $main 'wordle.png'
if($SystemChecks){
    python tools/check_display.py $main.ToInt64() $qa $widget.ToInt64()
    if($LASTEXITCODE){throw 'Display acceptance failed; display settings restored by helper.'}
}
Click $main '学习统计'
Click $main '挑战成绩'
[void](Await {FindItem $main 'Wordle 猜词　·　3 局'} 'independent challenge statistics')
Snap $main 'challenge-statistics.png'
Stop-Process -Id $proc.Id
$proc.WaitForExit()
$proc=Start-Process -FilePath $exe -PassThru
$main=Window 'LexiPane'
Click $main '挑战'
Click $main 'Wordle 猜词'
Assert (!(FindId $main 'ChallengeInput')) 'restart discards unfinished round'
python -c "import sqlite3,sys; from pathlib import Path; p=Path(sys.argv[1]); c=sqlite3.connect(p/'study.db'); assert c.execute('pragma user_version').fetchone()[0]==3; assert c.execute('select count(*) from challenge_rounds').fetchone()[0]==6; assert c.execute('select count(*) from reviews').fetchone()[0]==0; assert c.execute('select count(*) from bookmarks').fetchone()[0]==4; assert len(list(p.glob('study.db.*.bak')))==1; print('PASS version-2 migration, completed games persist, daily logs remain clean')" $qa
if($LASTEXITCODE){throw 'Challenge database checks failed'}
Write-Output 'PASS four game UIs, keyboard, window restore, variable lengths, results and restart'
