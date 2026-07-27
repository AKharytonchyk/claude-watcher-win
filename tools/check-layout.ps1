# Geometry verification for the flyout, via UI Automation.
# Asserts: the window sits inside the work area; every row and every piece of text
# sits inside the window; and no two texts on the same line overlap. Works on a
# locked session, where screenshots do not.
param([switch] $Reopen)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

$p = Get-Process ClaudeWatcher -ErrorAction Stop
$root = [System.Windows.Automation.AutomationElement]::RootElement
$pc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $p.Id
$bc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Button)
$tc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Text)
$li = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::ListItem)

function Flyout { $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pc) }
function TrayIcon {
  foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($w.Current.ClassName -match 'Overflow|Shell_TrayWnd') {
      foreach ($b in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
        if ($b.Current.Name -match 'Claude') { return $b } } } }
  return $null }
function Toggle { $t = TrayIcon; if ($t) { $t.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1800 } }

if ($Reopen) { if (Flyout) { Toggle }; Toggle }
if (-not (Flyout)) { Toggle }
$fw = Flyout
if (-not $fw) { Write-Output 'FAIL: flyout would not open'; exit 1 }

$W = $fw.Current.BoundingRectangle
$area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
Write-Output "work area : $($area.X),$($area.Y) $($area.Width)x$($area.Height)"
Write-Output "flyout    : $([int]$W.X),$([int]$W.Y) $([int]$W.Width)x$([int]$W.Height)"

$fail = 0
function Check($ok, $msg) { if ($ok) { Write-Output "  PASS  $msg" } else { Write-Output "  FAIL  $msg"; $script:fail++ } }

Write-Output "`n== window inside the screen =="
Check ($W.X -ge $area.X) "left edge $([int]$W.X) >= $($area.X)"
Check ($W.Y -ge $area.Y) "top edge $([int]$W.Y) >= $($area.Y)"
Check (($W.X + $W.Width) -le ($area.X + $area.Width)) "right edge $([int]($W.X+$W.Width)) <= $($area.X+$area.Width)"
Check (($W.Y + $W.Height) -le ($area.Y + $area.Height)) "bottom edge $([int]($W.Y+$W.Height)) <= $($area.Y+$area.Height)"

Write-Output "`n== rows inside the window =="
$rows = @($fw.FindAll([System.Windows.Automation.TreeScope]::Descendants, $li) | Where-Object { -not $_.Current.IsOffscreen })
Write-Output "  visible rows: $($rows.Count)"
foreach ($r in $rows) {
  $b = $r.Current.BoundingRectangle
  $nm = if ($r.Current.Name -match 'Name = ([^,]+),') { $Matches[1] } else { '?' }
  $inside = ($b.Y -ge $W.Y) -and (($b.Y + $b.Height) -le ($W.Y + $W.Height + 0.5)) -and
            ($b.X -ge $W.X) -and (($b.X + $b.Width) -le ($W.X + $W.Width + 0.5))
  Check $inside "row '$nm' $([int]$b.Y)..$([int]($b.Y+$b.Height)) within $([int]$W.Y)..$([int]($W.Y+$W.Height))"
}

Write-Output "`n== text inside the window, and no overlap on a line =="
$texts = @()
foreach ($t in $fw.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tc)) {
  $b = $t.Current.BoundingRectangle
  if ($t.Current.IsOffscreen -or $b.Width -le 0 -or $b.Height -le 0) { continue }
  $texts += [pscustomobject]@{ Name = $t.Current.Name; X = $b.X; Y = $b.Y; W = $b.Width; H = $b.Height }
}
Write-Output "  visible text elements: $($texts.Count)"
$out = @($texts | Where-Object { $_.X -lt $W.X -or $_.Y -lt $W.Y -or ($_.X + $_.W) -gt ($W.X + $W.Width + 0.5) -or ($_.Y + $_.H) -gt ($W.Y + $W.Height + 0.5) })
Check ($out.Count -eq 0) "all text within the window bounds$(if($out.Count){' — outside: ' + (($out | ForEach-Object { "'" + $_.Name + "' @" + [int]$_.X + ',' + [int]$_.Y }) -join '; ')})"

# Same visual line = vertical ranges overlap by more than half the shorter height.
$overlaps = @()
for ($i = 0; $i -lt $texts.Count; $i++) {
  for ($j = $i + 1; $j -lt $texts.Count; $j++) {
    $a = $texts[$i]; $b = $texts[$j]
    $vOverlap = [Math]::Min($a.Y + $a.H, $b.Y + $b.H) - [Math]::Max($a.Y, $b.Y)
    if ($vOverlap -le ([Math]::Min($a.H, $b.H) / 2)) { continue }
    $hOverlap = [Math]::Min($a.X + $a.W, $b.X + $b.W) - [Math]::Max($a.X, $b.X)
    if ($hOverlap -gt 1) { $overlaps += "'$($a.Name)' x '$($b.Name)' by $([int]$hOverlap)px" }
  }
}
Check ($overlaps.Count -eq 0) "no horizontal overlap between texts sharing a line$(if($overlaps.Count){' — ' + ($overlaps -join '; ')})"

Write-Output "`n$(if ($fail -eq 0) { 'ALL CHECKS PASSED' } else { "$fail CHECK(S) FAILED" })"
exit $fail
