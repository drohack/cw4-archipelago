# Send a key to the running Creeper World 4 window.
#
# Exists because HUD-collision checks need the game in a particular UI mode -
# the dev overlay was found covering the terraform bar, which only appears while
# terraform mode is open, and there is no way to see that from a log.
#
# CW4's own keybinds live in
#   ~/Documents/My Games/creeperworld4/settings/controls.xml
# as decimal keycodes; <Terraform> is 108 ('l'), which is vk 0x4C = 76.
#
# The AttachThreadInput dance is required: Windows refuses SetForegroundWindow
# from a background process, so a plain call silently leaves focus where it was
# and the key goes to the wrong window. Attaching to the current foreground
# thread's input queue first lifts that restriction.
#
# Usage: powershell -File tools/game-key.ps1 -vk 76      # open terraform mode
param([int]$vk = 76)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Cw4Key {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
}
'@
$p = Get-Process CW4 -ErrorAction Stop
$h = $p.MainWindowHandle
$fg = [Cw4Key]::GetForegroundWindow()
$fgPid = 0
$fgThread = [Cw4Key]::GetWindowThreadProcessId($fg, [ref]$fgPid)
$me = [Cw4Key]::GetCurrentThreadId()
[Cw4Key]::AttachThreadInput($me, $fgThread, $true) | Out-Null
[Cw4Key]::ShowWindow($h, 9) | Out-Null
[Cw4Key]::BringWindowToTop($h) | Out-Null
[Cw4Key]::SetForegroundWindow($h) | Out-Null
[Cw4Key]::AttachThreadInput($me, $fgThread, $false) | Out-Null
Start-Sleep -Milliseconds 1500
$nowPid = 0
[Cw4Key]::GetWindowThreadProcessId([Cw4Key]::GetForegroundWindow(), [ref]$nowPid) | Out-Null
$name = (Get-Process -Id $nowPid).ProcessName
if ($name -ne 'CW4') { Write-Output "FAILED: foreground is $name, not CW4 - key not sent"; exit 1 }
# Park the pointer over the map; some hotkeys are ignored under a HUD panel.
[Cw4Key]::SetCursorPos(1920, 900) | Out-Null
Start-Sleep -Milliseconds 300
[Cw4Key]::keybd_event([byte]$vk, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 150
[Cw4Key]::keybd_event([byte]$vk, 0, 2, [IntPtr]::Zero)
Write-Output "sent vk=$vk to CW4"
