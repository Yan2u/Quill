<#
.SYNOPSIS
    Captures promotional Light and Dark screenshots of Quill over a clean desktop and composes README graphics.

.DESCRIPTION
    1. Backs up %LOCALAPPDATA%\Quill JSON state files.
    2. Minimizes all other visible top-level desktop windows (unless -KeepDesktopWindows is set) so the clean
       desktop wallpaper sits behind Quill's Windows 11 Mica title bar.
    3. Launches Quill.exe with the showcase Markdown document for each requested theme (dark / light),
       captures the rendered window via winapp Windows.Graphics.Capture (with PrintWindow fallback), and
       records exact DWM crop insets.
    4. Invokes scripts/compose-promo.py to frame each capture onto its matching 16:10 backdrop.
    5. Restores original user settings/state and all minimized desktop windows in a finally block.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [ValidateSet("dark", "light")]
    [string[]]$Themes = @("dark", "light"),
    [ValidateSet("English", "Chinese")]
    [string]$Language = "English",
    [string]$ShowcasePath,
    [string]$OutputDir,
    [int]$LogicalWidth = 1160,
    [int]$LogicalHeight = 760,
    [double]$SidebarWidth = 216,
    [int]$RenderWaitMs = 2500,
    [switch]$UseScreenCapture,
    [switch]$SkipCompose,
    [switch]$KeepDesktopWindows
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $ShowcasePath) {
    $ShowcasePath = Join-Path $repoRoot ".agents/artifacts/promo/Welcome to Quill.md"
}
if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot "docs/assets"
}

$showcaseFullPath = [System.IO.Path]::GetFullPath($ShowcasePath)
if (-not (Test-Path -LiteralPath $showcaseFullPath)) {
    throw "Showcase document not found: $showcaseFullPath"
}

$exePath = Join-Path $repoRoot "src/Quill/bin/$Configuration/net10.0-windows10.0.19041.0/Quill.exe"
if (-not $NoBuild -or -not (Test-Path -LiteralPath $exePath)) {
    & (Join-Path $PSScriptRoot "build-app.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build Quill ($Configuration)."
    }
}
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Quill executable not found: $exePath"
}

$existingQuill = Get-Process -Name "Quill" -ErrorAction SilentlyContinue
if ($existingQuill) {
    Write-Warning "Detected pre-existing Quill process (PID: $($existingQuill.Id -join ', ')). Leaving it untouched and tracking only the newly launched instance."
}

$win32Source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class QuillPromoNative
{
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const uint GW_OWNER = 4;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 14;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    private static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow",
        "Shell_InputSwitchTopLevelWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "EdgeUiInputTopWndClass",
        "DummyDWMListenerWindow",
        "ForegroundStaging"
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    public static void EnablePerMonitorDpiAwareness()
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
    }

    public static RECT GetPrimaryWorkArea()
    {
        IntPtr monitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        MONITORINFO info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        }
        return info.rcWork;
    }

    public static IntPtr[] GetVisibleAppWindows()
    {
        List<IntPtr> result = new List<IntPtr>();
        EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
            {
                return true;
            }

            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero)
            {
                return true;
            }

            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0)
            {
                return true;
            }

            int cloaked = 0;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, Marshal.SizeOf(typeof(int))) == 0 && cloaked != 0)
            {
                return true;
            }

            StringBuilder cls = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            if (ShellClasses.Contains(cls.ToString()))
            {
                return true;
            }

            RECT rect;
            if (!GetWindowRect(hwnd, out rect) || rect.Width <= 0 || rect.Height <= 0)
            {
                return true;
            }

            result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }

    public static void PositionWindow(IntPtr hwnd, int x, int y, int width, int height)
    {
        SetWindowPos(hwnd, HWND_TOP, x, y, width, height, SWP_SHOWWINDOW);
        SetForegroundWindow(hwnd);
    }

    public static IntPtr FindVisibleWindowByPid(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
        {
            uint winPid = 0;
            GetWindowThreadProcessId(hwnd, out winPid);
            if (winPid != (uint)pid || !IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero)
            {
                return true;
            }

            RECT rect;
            if (GetWindowRect(hwnd, out rect) && rect.Width > 100 && rect.Height > 100)
            {
                found = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static RECT GetExtendedFrameBounds(IntPtr hwnd)
    {
        RECT rect;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(RECT))) == 0)
        {
            return rect;
        }
        GetWindowRect(hwnd, out rect);
        return rect;
    }
}
'@

if (-not ("QuillPromoNative" -as [type])) {
    Add-Type -TypeDefinition $win32Source -Language CSharp
}

[QuillPromoNative]::EnablePerMonitorDpiAwareness()

function Get-PngDimensions {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 24 -or $bytes[0] -ne 0x89 -or $bytes[1] -ne 0x50) {
        throw "Invalid PNG file: $Path"
    }
    $w = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $h = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    return @{ Width = $w; Height = $h }
}

function Save-PrintWindowFallback {
    param(
        [IntPtr]$Hwnd,
        [int]$Width,
        [int]$Height,
        [string]$Path
    )
    Add-Type -AssemblyName System.Drawing
    $bmp = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $gfx = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $hdc = $gfx.GetHdc()
            try {
                # PW_RENDERFULLCONTENT (2) captures DirectComposition / WinUI 3 / Win2D content.
                $ok = [QuillPromoNative]::PrintWindow($Hwnd, $hdc, 2)
                if (-not $ok) {
                    throw "Win32 PrintWindow returned false for HWND $Hwnd."
                }
            }
            finally {
                $gfx.ReleaseHdc($hdc)
            }
        }
        finally {
            $gfx.Dispose()
        }
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bmp.Dispose()
    }
}

$tempDir = Join-Path $repoRoot ".agents/temp"
New-Item -ItemType Directory -Force $tempDir | Out-Null
New-Item -ItemType Directory -Force $OutputDir | Out-Null

$appDataDir = Join-Path $env:LOCALAPPDATA "Quill"
$appDataExisted = $false
$backupDir = Join-Path $tempDir ("appdata-backup-" + [guid]::NewGuid().ToString("N"))
$backedUpFiles = @()
$backupReady = $false

$minimizedWindows = @()
$originalForeground = [QuillPromoNative]::GetForegroundWindow()
$launchedProcess = $null
$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString()

try {
    # Minimize desktop windows BEFORE backing up AppData so any minimized window's resize timers settle first.
    if (-not $KeepDesktopWindows) {
        $minimizedWindows = [QuillPromoNative]::GetVisibleAppWindows()
        foreach ($hwnd in $minimizedWindows) {
            [void][QuillPromoNative]::ShowWindow($hwnd, [QuillPromoNative]::SW_MINIMIZE)
        }
        if ($minimizedWindows.Count -gt 0) {
            Start-Sleep -Milliseconds 700
        }
    }

    $appDataExisted = Test-Path -LiteralPath $appDataDir
    if ($appDataExisted) {
        $existingJsons = Get-ChildItem -LiteralPath $appDataDir -Filter "*.json" -File -ErrorAction SilentlyContinue
        if ($existingJsons) {
            New-Item -ItemType Directory -Force $backupDir | Out-Null
            foreach ($file in $existingJsons) {
                $dest = Join-Path $backupDir $file.Name
                Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
                $backedUpFiles += [pscustomobject]@{
                    Name = $file.Name
                    OriginalPath = $file.FullName
                    BackupPath = $dest
                }
            }
        }
    }
    New-Item -ItemType Directory -Force $appDataDir | Out-Null
    $backupReady = $true

    $dpi = [QuillPromoNative]::GetDpiForSystem()
    $scale = [double]$dpi / 96.0
    $workArea = [QuillPromoNative]::GetPrimaryWorkArea()

    $physW = [Math]::Min($workArea.Width, [int][Math]::Round($LogicalWidth * $scale))
    $physH = [Math]::Min($workArea.Height, [int][Math]::Round($LogicalHeight * $scale))
    $winX = $workArea.Left + [int][Math]::Floor(($workArea.Width - $physW) / 2.0)
    $winY = $workArea.Top + [int][Math]::Floor(($workArea.Height - $physH) / 2.0)

    $langValue = if ($Language -eq "Chinese") { "chinese" } else { "english" }

    foreach ($theme in $Themes) {
        $settingsObj = [ordered]@{
            version = 1
            appTheme = $theme
            lightTheme = "github"
            darkTheme = "github-dark-dimmed"
            language = $langValue
            showStatusBar = $true
            codeLineNumbers = $false
            wrapCode = $true
        }
        $stateObj = [ordered]@{
            version = 1
            sidebarVisible = $true
            sidebarWidth = $SidebarWidth
            sidebarTab = "outline"
            filesView = "tree"
            navigationExpanded = $false
            windowWidth = $physW
            windowHeight = $physH
            windowMaximized = $false
        }

        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText((Join-Path $appDataDir "settings.json"), ($settingsObj | ConvertTo-Json -Depth 4), $utf8NoBom)
        [System.IO.File]::WriteAllText((Join-Path $appDataDir "state.json"), ($stateObj | ConvertTo-Json -Depth 4), $utf8NoBom)

        $launchedProcess = Start-Process -FilePath $exePath -ArgumentList @("`"$showcaseFullPath`"") -PassThru
        $hwnd = [IntPtr]::Zero
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($launchedProcess.HasExited) {
                throw "Quill.exe exited unexpectedly with code $($launchedProcess.ExitCode)."
            }
            $launchedProcess.Refresh()
            $candidate = [QuillPromoNative]::FindVisibleWindowByPid($launchedProcess.Id)
            if ($candidate -ne [IntPtr]::Zero) {
                $hwnd = $candidate
                break
            }
            if ($launchedProcess.MainWindowHandle -ne [IntPtr]::Zero) {
                $hwnd = $launchedProcess.MainWindowHandle
                break
            }
            Start-Sleep -Milliseconds 100
        }
        if ($hwnd -eq [IntPtr]::Zero) {
            throw "Timed out waiting for Quill main window handle."
        }

        # Let initial WinUI loaded handlers apply, then enforce exact centered physical bounds.
        Start-Sleep -Milliseconds 500
        [QuillPromoNative]::PositionWindow($hwnd, $winX, $winY, $physW, $physH)
        # Park the cursor cleanly at the bottom-right work-area corner to avoid hover states.
        [void][QuillPromoNative]::SetCursorPos($workArea.Right - 4, $workArea.Bottom - 4)

        Start-Sleep -Milliseconds $RenderWaitMs

        $winRect = New-Object QuillPromoNative+RECT
        [void][QuillPromoNative]::GetWindowRect($hwnd, [ref]$winRect)
        $extRect = [QuillPromoNative]::GetExtendedFrameBounds($hwnd)

        $rawPng = Join-Path $tempDir "raw-$theme.png"
        Remove-Item -LiteralPath $rawPng -ErrorAction SilentlyContinue

        $hwndDecimal = $hwnd.ToInt64().ToString()
        $captureMethod = $null
        try {
            $winappArgs = @("ui", "screenshot", "-w", $hwndDecimal, "--output", $rawPng)
            if ($UseScreenCapture) {
                $winappArgs += "--capture-screen"
            }
            $winappOutput = & winapp @winappArgs 2>&1
            if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $rawPng)) {
                $captureMethod = if ($UseScreenCapture) { "winapp-capture-screen" } else { "winapp-wgc" }
            }
            else {
                Write-Warning "winapp ui screenshot failed (exit $LASTEXITCODE): $($winappOutput -join ' ')"
            }
        }
        catch {
            Write-Warning "winapp ui screenshot threw an exception: $($_.Exception.Message)"
        }

        if (-not $captureMethod) {
            Save-PrintWindowFallback -Hwnd $hwnd -Width $winRect.Width -Height $winRect.Height -Path $rawPng
            $captureMethod = "win32-print-window"
        }

        $dims = Get-PngDimensions -Path $rawPng
        $insetLeft = 0
        $insetTop = 0
        $insetRight = 0
        $insetBottom = 0

        if ($dims.Width -eq $winRect.Width -and $dims.Height -eq $winRect.Height) {
            $insetLeft = [Math]::Max(0, $extRect.Left - $winRect.Left)
            $insetTop = [Math]::Max(0, $extRect.Top - $winRect.Top)
            $insetRight = [Math]::Max(0, $winRect.Right - $extRect.Right)
            $insetBottom = [Math]::Max(0, $winRect.Bottom - $extRect.Bottom)
        }
        elseif ($dims.Width -gt $extRect.Width -or $dims.Height -gt $extRect.Height) {
            $diffW = $dims.Width - $extRect.Width
            $diffH = $dims.Height - $extRect.Height
            $insetLeft = [int][Math]::Floor($diffW / 2.0)
            $insetRight = $diffW - $insetLeft
            $insetTop = 0
            $insetBottom = [Math]::Max(0, $diffH)
        }

        $metaPath = Join-Path $tempDir "raw-$theme.json"
        $metaObj = [ordered]@{
            theme = $theme
            rawPath = $rawPng
            captureMethod = $captureMethod
            dpi = $dpi
            pngWidth = $dims.Width
            pngHeight = $dims.Height
            windowRect = @{ left = $winRect.Left; top = $winRect.Top; right = $winRect.Right; bottom = $winRect.Bottom }
            extendedRect = @{ left = $extRect.Left; top = $extRect.Top; right = $extRect.Right; bottom = $extRect.Bottom }
            cropInsets = @($insetLeft, $insetTop, $insetRight, $insetBottom)
        }
        [System.IO.File]::WriteAllText($metaPath, ($metaObj | ConvertTo-Json -Depth 4), $utf8NoBom)
        Write-Host "Captured $theme ($($dims.Width)x$($dims.Height), insets=$insetLeft,$insetTop,$insetRight,$insetBottom) via $($metaObj.captureMethod)"

        if (-not $launchedProcess.HasExited) {
            [void]$launchedProcess.CloseMainWindow()
            if (-not $launchedProcess.WaitForExit(3000)) {
                $launchedProcess.Kill()
                [void]$launchedProcess.WaitForExit(2000)
            }
        }
        $launchedProcess.Dispose()
        $launchedProcess = $null
        Start-Sleep -Milliseconds 300

        if (-not $SkipCompose) {
            $backdropCandidate = Join-Path $repoRoot ".agents/artifacts/promo/backdrop-$theme.png"
            $outPng = Join-Path $OutputDir "preview-$theme.png"
            $composeArgs = @(
                (Join-Path $PSScriptRoot "compose-promo.py"),
                "--input", $rawPng,
                "--output", $outPng,
                "--theme", $theme,
                "--crop-insets", "$insetLeft,$insetTop,$insetRight,$insetBottom"
            )
            if (Test-Path -LiteralPath $backdropCandidate) {
                $composeArgs += @("--background", $backdropCandidate)
            }
            & python @composeArgs
            if ($LASTEXITCODE -ne 0) {
                throw "compose-promo.py failed for theme '$theme' with exit code $LASTEXITCODE."
            }
        }
    }
}
finally {
    if ($launchedProcess -ne $null) {
        try {
            if (-not $launchedProcess.HasExited) {
                $launchedProcess.Kill()
                [void]$launchedProcess.WaitForExit(2000)
            }
            $launchedProcess.Dispose()
        }
        catch {
        }
    }

    try {
        & winapp ui yield | Out-Null
    }
    catch {
    }

    if ($null -eq $previousWorkflowId) {
        Remove-Item Env:WINAPP_UI_WORKFLOW_ID -ErrorAction SilentlyContinue
    }
    else {
        $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId
    }

    # Restore exact original %LOCALAPPDATA%\Quill JSON files before restoring other windows.
    if ($backupReady -and (Test-Path -LiteralPath $appDataDir)) {
        $originalNames = @($backedUpFiles | ForEach-Object { $_.Name })
        $currentJsons = Get-ChildItem -LiteralPath $appDataDir -Filter "*.json" -File -ErrorAction SilentlyContinue
        foreach ($file in $currentJsons) {
            if ($file.Name -notin $originalNames) {
                Remove-Item -LiteralPath $file.FullName -Force -ErrorAction SilentlyContinue
            }
        }
        foreach ($entry in $backedUpFiles) {
            Copy-Item -LiteralPath $entry.BackupPath -Destination $entry.OriginalPath -Force
        }
        if (-not $appDataExisted) {
            $remaining = Get-ChildItem -LiteralPath $appDataDir -Force -ErrorAction SilentlyContinue
            if (-not $remaining) {
                Remove-Item -LiteralPath $appDataDir -Force -ErrorAction SilentlyContinue
            }
        }
    }

    if (Test-Path -LiteralPath $backupDir) {
        Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Restore previously visible desktop windows in reverse Z-order.
    if ($minimizedWindows.Count -gt 0) {
        for ($i = $minimizedWindows.Count - 1; $i -ge 0; $i--) {
            [void][QuillPromoNative]::ShowWindow($minimizedWindows[$i], [QuillPromoNative]::SW_RESTORE)
        }
    }
    if ($originalForeground -ne [IntPtr]::Zero) {
        [void][QuillPromoNative]::SetForegroundWindow($originalForeground)
    }
}
