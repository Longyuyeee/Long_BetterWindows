#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [ValidateRange(5,60)] [int] $TimeoutSeconds = 25,
    [string] $ReleaseDirectory,
    [switch] $NoBuild,
    [switch] $WorkflowOnly,
    [switch] $WorkflowOutputOnly,
    [switch] $WorkflowSchemaOnly,
    [switch] $WorkflowExportMatrix
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Desktop UI smoke output directory already exists: $outputRoot"
}
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$workflowRoot = Join-Path $outputRoot 'workflows'
[IO.Directory]::CreateDirectory($workflowRoot) | Out-Null
$exportMatrixRoot = Join-Path $outputRoot 'terminal-export-matrix'
$exportWritableRoot = Join-Path $exportMatrixRoot 'writable'
$exportDeniedRoot = Join-Path $exportMatrixRoot 'denied'
$exportReparseTarget = Join-Path $exportMatrixRoot 'reparse-target'
$exportReparseRoot = Join-Path $exportMatrixRoot 'reparse'
$exportAclIdentity = $null
if ($WorkflowExportMatrix) {
    [IO.Directory]::CreateDirectory($exportWritableRoot) | Out-Null
    [IO.Directory]::CreateDirectory($exportDeniedRoot) | Out-Null
    [IO.Directory]::CreateDirectory($exportReparseTarget) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $exportWritableRoot 'existing.txt'),
        'quality-original',
        [Text.UTF8Encoding]::new($false))
    New-Item -ItemType Junction -Path $exportReparseRoot `
        -Target $exportReparseTarget | Out-Null
    $exportAclIdentity = '*' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls.exe $exportDeniedRoot /deny `
        ($exportAclIdentity + ':(OI)(CI)(W)') /Q | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the terminal export permission-denied directory.'
    }
}
$qualityWorkflow = @'
{
  "schema_version": 3,
  "source": {
    "kind": "local_managed",
    "source_id": "local-managed"
  },
  "workflow": {
    "id": "workflow.quality.review",
    "name": "Quality Workflow Review",
    "failure_mode": "stop",
    "steps": [
      {
        "id": "generate",
        "effect": "read_only",
        "command": {
          "command_key": "com.long.uuid-generator:uuid.generate",
          "invocation": {
            "command_id": "uuid.generate",
            "input_type": "none",
            "text": null,
            "paths": [],
            "image_png": null,
            "arguments": {
              "amount": "100",
              "uppercase": "false",
              "compact": "false"
            }
          },
          "bindings": []
        },
        "compensation": null
      }
    ]
  }
}
'@
$sourceWorkflowPath = Join-Path `
    $workflowRoot 'workflow.quality.review.workflow.json'
[IO.File]::WriteAllText(
    $sourceWorkflowPath,
    $qualityWorkflow,
    [Text.UTF8Encoding]::new($false))
$sourceWorkflowSha256 = (Get-FileHash `
    -LiteralPath $sourceWorkflowPath -Algorithm SHA256).Hash.ToLowerInvariant()

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) { throw 'dotnet CLI was not found.' }
    $dotnet = $dotnetCommand.Source
}
$project = Join-Path $repoRoot 'src\LongBetterWindows.Host\LongBetterWindows.Host.csproj'
$releaseRoot = if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows'
}
else {
    [IO.Path]::GetFullPath($ReleaseDirectory)
}
$executable = Join-Path $releaseRoot 'LongBetterWindows.Host.exe'
$pluginsDirectory = Join-Path $releaseRoot 'Plugins'
if ([string]::IsNullOrWhiteSpace($ReleaseDirectory) -and -not $NoBuild) {
    & $dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Desktop UI smoke Release build failed.' }
}
if (-not (Test-Path -LiteralPath $executable)) { throw "Host executable was not found: $executable" }
if (-not (Test-Path -LiteralPath $pluginsDirectory -PathType Container)) {
    throw "Plugins directory was not found: $pluginsDirectory"
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class LongDesktopInput {
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr SetActiveWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);
    delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    public static IntPtr[] TopLevelWindows(int processId) {
        var windows = new List<IntPtr>();
        EnumWindows((window, state) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId && IsWindowVisible(window)) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }
    public static bool Activate(IntPtr window) {
        uint ignored;
        uint callerThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(window, out ignored);
        uint foregroundThread = GetWindowThreadProcessId(
            GetForegroundWindow(), out ignored);
        bool attachedToForeground = foregroundThread != 0 &&
            foregroundThread != callerThread &&
            AttachThreadInput(callerThread, foregroundThread, true);
        bool attachedToTarget = targetThread != 0 &&
            targetThread != callerThread &&
            targetThread != foregroundThread &&
            AttachThreadInput(callerThread, targetThread, true);
        try {
            BringWindowToTop(window);
            SetForegroundWindow(window);
            SetActiveWindow(window);
            return GetForegroundWindow() == window;
        }
        finally {
            if (attachedToTarget)
                AttachThreadInput(callerThread, targetThread, false);
            if (attachedToForeground)
                AttachThreadInput(callerThread, foregroundThread, false);
        }
    }
    public static void Click(IntPtr window, int x, int y) {
        Activate(window);
        System.Threading.Thread.Sleep(100);
        IntPtr previousContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try {
            int left = GetSystemMetrics(76);
            int top = GetSystemMetrics(77);
            int width = GetSystemMetrics(78);
            int height = GetSystemMetrics(79);
            uint normalizedX = (uint)Math.Max(0, Math.Min(65535,
                ((long)(x - left) * 65535) / Math.Max(1, width - 1)));
            uint normalizedY = (uint)Math.Max(0, Math.Min(65535,
                ((long)(y - top) * 65535) / Math.Max(1, height - 1)));
            mouse_event(0xC001, normalizedX, normalizedY, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(80);
            mouse_event(0x0002, normalizedX, normalizedY, 0, UIntPtr.Zero);
            mouse_event(0x0004, normalizedX, normalizedY, 0, UIntPtr.Zero);
        }
        finally {
            if (previousContext != IntPtr.Zero) SetThreadDpiAwarenessContext(previousContext);
        }
    }
    public static int WindowAction(IntPtr window, int action) {
        uint message = RegisterWindowMessage(
            "LongBetterWindows.Quality.WindowAction.v1");
        if (message == 0) return -1;
        UIntPtr result;
        IntPtr sent = SendMessageTimeout(
            window, message, new IntPtr(action), IntPtr.Zero, 0x0002, 5000,
            out result);
        return sent == IntPtr.Zero ? -1 : unchecked((int)result.ToUInt64());
    }
    public static int WorkflowMessage(IntPtr window, int action) {
        uint message = RegisterWindowMessage("LongBetterWindows.Quality.WorkflowAction.v1");
        if (message == 0) return -1;
        UIntPtr result;
        IntPtr sent = SendMessageTimeout(
            window, message, new IntPtr(action), IntPtr.Zero, 0x0002, 5000, out result);
        return sent == IntPtr.Zero ? -1 : unchecked((int)result.ToUInt64());
    }
}
'@

function Write-Stage([string] $message) {
    $line = "[$([DateTimeOffset]::Now.ToString('O'))] $message"
    Write-Output "[desktop-ui-smoke] $message"
    Add-Content -LiteralPath (Join-Path $outputRoot 'desktop-ui-smoke.log') `
        -Value $line -Encoding UTF8
}

function Wait-Until([scriptblock] $Probe, [string] $FailureMessage) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastProbeError = $null
    do {
        try {
            $value = & $Probe
            $lastProbeError = $null
        }
        catch {
            $lastProbeError = $_.Exception.Message
            $value = $null
        }
        if ($value -is [bool]) {
            if ($value) { return $true }
        }
        elseif ($null -ne $value) {
            return $value
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not [string]::IsNullOrWhiteSpace($lastProbeError)) {
        throw "$FailureMessage Last probe error: $lastProbeError"
    }
    throw $FailureMessage
}

function Find-WindowByAutomationId([int] $processId, [string] $automationId) {
    $windows = [LongDesktopInput]::TopLevelWindows($processId)
    foreach ($window in $windows) {
        $element = [Windows.Automation.AutomationElement]::FromHandle($window)
        if ($element.Current.AutomationId -eq $automationId) { return $element }
    }
    return $null
}

function Find-WindowHandleByAutomationId([int] $processId, [string] $automationId) {
    $windows = [LongDesktopInput]::TopLevelWindows($processId)
    foreach ($window in $windows) {
        $element = [Windows.Automation.AutomationElement]::FromHandle($window)
        if ($element.Current.AutomationId -eq $automationId) { return $window }
    }
    return $null
}

function Find-DescendantByAutomationId(
    [Windows.Automation.AutomationElement] $root,
    [string] $automationId) {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty,
        $automationId)
    return $root.FindFirst(
        [Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Find-DescendantByName(
    [Windows.Automation.AutomationElement] $root,
    [string] $name) {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::NameProperty,
        $name)
    return $root.FindFirst(
        [Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Find-DescendantByControlType(
    [Windows.Automation.AutomationElement] $root,
    [Windows.Automation.ControlType] $controlType) {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ControlTypeProperty,
        $controlType)
    return $root.FindFirst(
        [Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Find-ProcessElementByAutomationId([int] $processId, [string] $automationId) {
    $windows = [LongDesktopInput]::TopLevelWindows($processId)
    foreach ($window in $windows) {
        $root = [Windows.Automation.AutomationElement]::FromHandle($window)
        if ($root.Current.AutomationId -eq $automationId) { return $root }
        $match = Find-DescendantByAutomationId $root $automationId
        if ($null -ne $match) { return $match }
    }
    return $null
}

function Invoke-WindowWorkflowAction(
    [IntPtr] $window,
    [int] $action,
    [string] $failureMessage) {
    if ($window -eq [IntPtr]::Zero -or
        [LongDesktopInput]::WorkflowMessage($window, $action) -ne 1) {
        throw $failureMessage
    }
}

function Find-RawSiblingByAutomationId(
    [Windows.Automation.AutomationElement] $element,
    [string] $automationId) {
    $walker = [Windows.Automation.TreeWalker]::RawViewWalker
    $candidate = $element
    for ($index = 0; $index -lt 8; $index++) {
        $candidate = $walker.GetNextSibling($candidate)
        if ($null -eq $candidate) { return $null }
        if ($candidate.Current.AutomationId -eq $automationId) { return $candidate }
    }
    return $null
}

function Find-RawDescendantByAutomationId(
    [Windows.Automation.AutomationElement] $element,
    [string] $automationId,
    [int] $remainingDepth = 8) {
    if ($remainingDepth -le 0) { return $null }
    $walker = [Windows.Automation.TreeWalker]::RawViewWalker
    $child = $walker.GetFirstChild($element)
    while ($null -ne $child) {
        if ($child.Current.AutomationId -eq $automationId) { return $child }
        $match = Find-RawDescendantByAutomationId `
            $child $automationId ($remainingDepth - 1)
        if ($null -ne $match) { return $match }
        $child = $walker.GetNextSibling($child)
    }
    return $null
}

function Get-FocusedElementByAutomationId([string] $automationId) {
    $focused = [Windows.Automation.AutomationElement]::FocusedElement
    if ($null -ne $focused -and $focused.Current.AutomationId -eq $automationId) {
        return $focused
    }
    return $null
}

function Invoke-AutomationElement(
    [Windows.Automation.AutomationElement] $element,
    [string] $failureMessage) {
    if ($null -eq $element) { throw $failureMessage }
    $pattern = $null
    if (-not $element.TryGetCurrentPattern(
        [Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        throw $failureMessage
    }
    ([Windows.Automation.InvokePattern]$pattern).Invoke()
}

function Find-AncestorByControlType(
    [Windows.Automation.AutomationElement] $element,
    [Windows.Automation.ControlType] $controlType) {
    $walker = [Windows.Automation.TreeWalker]::ControlViewWalker
    $candidate = $element
    for ($index = 0; $index -lt 8; $index++) {
        if ($candidate.Current.ControlType -eq $controlType) {
            return $candidate
        }
        $candidate = $walker.GetParent($candidate)
        if ($null -eq $candidate) { return $null }
    }
    return $null
}

function Select-AutomationElement(
    [Windows.Automation.AutomationElement] $element,
    [string] $failureMessage) {
    $lastError = $null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            $pattern = $null
            if (-not $element.TryGetCurrentPattern(
                [Windows.Automation.SelectionItemPattern]::Pattern,
                [ref]$pattern)) {
                throw $failureMessage
            }
            $selection = [Windows.Automation.SelectionItemPattern]$pattern
            if ($selection.Current.IsSelected) {
                return
            }
            $selection.Select()
            if ($selection.Current.IsSelected) {
                return
            }
            $lastError = 'Selection provider did not report IsSelected.'
        }
        catch {
            $lastError = $_.Exception.Message
        }
        if ($attempt -lt 5) {
            Start-Sleep -Milliseconds 120
        }
    }
    try {
        $element.SetFocus()
        Start-Sleep -Milliseconds 120
        $pattern = $null
        $supportsSelection = $element.TryGetCurrentPattern(
            [Windows.Automation.SelectionItemPattern]::Pattern,
            [ref]$pattern)
        $fallbackSelection =
            [Windows.Automation.SelectionItemPattern]$pattern
        if ($supportsSelection -and $fallbackSelection.Current.IsSelected) {
            return
        }
    }
    catch {
        $lastError = $_.Exception.Message
    }
    throw "$failureMessage Last provider error: $lastError"
}

function Click-AutomationElement(
    [Windows.Automation.AutomationElement] $element,
    [IntPtr] $windowHandle,
    [string] $failureMessage) {
    if ($null -eq $element) { throw $failureMessage }
    $bounds = $element.Current.BoundingRectangle
    if ($bounds.IsEmpty -or $element.Current.IsOffscreen) { throw $failureMessage }
    Write-Stage ("Clicking {0} at {1},{2} size {3}x{4}." -f `
        $element.Current.AutomationId,
        [int]$bounds.Left,
        [int]$bounds.Top,
        [int]$bounds.Width,
        [int]$bounds.Height)
    [LongDesktopInput]::Click(
        $windowHandle,
        [int]($bounds.Left + ($bounds.Width / 2)),
        [int]($bounds.Top + ($bounds.Height / 2)))
}

function Set-AutomationToggleOn(
    [Windows.Automation.AutomationElement] $element,
    [string] $failureMessage) {
    if ($null -eq $element) { throw $failureMessage }
    $pattern = $null
    if (-not $element.TryGetCurrentPattern(
        [Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) {
        throw $failureMessage
    }
    $toggle = [Windows.Automation.TogglePattern]$pattern
    if ($toggle.Current.ToggleState -ne [Windows.Automation.ToggleState]::On) {
        $toggle.Toggle()
    }
    Wait-Until {
        $toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On
    } $failureMessage | Out-Null
}

function Get-AutomationSemantics(
    [Windows.Automation.AutomationElement] $element,
    [string] $expectedControlType,
    [string] $failureMessage) {
    if ($null -eq $element) { throw $failureMessage }
    $name = [string]$element.Current.Name
    $controlType = [string]$element.Current.ControlType.ProgrammaticName
    if ([string]::IsNullOrWhiteSpace($name)) {
        throw "$failureMessage Automation name is empty."
    }
    if ($controlType -ne $expectedControlType) {
        throw "$failureMessage Expected $expectedControlType, received $controlType."
    }
    return [ordered]@{
        name = $name
        control_type = $controlType
        enabled = [bool]$element.Current.IsEnabled
        keyboard_focusable = [bool]$element.Current.IsKeyboardFocusable
    }
}

function Get-LastAccessibilityLogLine {
    $logDirectory = Join-Path $outputRoot 'logs'
    if (-not (Test-Path -LiteralPath $logDirectory)) { return $null }
    $matches = Get-ChildItem -LiteralPath $logDirectory -File -Filter 'log*.txt' |
        Sort-Object LastWriteTime |
        ForEach-Object {
            Select-String -LiteralPath $_.FullName `
                -Pattern 'Quality accessibility mode:' -SimpleMatch
        }
    return $matches | Select-Object -Last 1 -ExpandProperty Line
}

function Start-QualityHost([string[]] $viewArguments) {
    $arguments = @(
        '--theme', 'dark',
        '--plugins-dir', $pluginsDirectory,
        '--quality-workflows-dir', $workflowRoot,
        '--quality-window-automation'
    )
    $arguments += $viewArguments
    $process = Start-Process -FilePath $executable -ArgumentList $arguments `
        -WorkingDirectory $outputRoot -PassThru
    Start-Sleep -Seconds 4
    return $process
}

function Stop-QualityHost([Diagnostics.Process] $process) {
    if ($null -eq $process -or $process.HasExited) { return }
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit(5000) | Out-Null
}

$report = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'desktop_ui_automation_smoke'
    release_executable = $executable
    palette = [ordered]@{}
    super_panel = [ordered]@{}
    management_navigation = [ordered]@{}
    workflow_review = [ordered]@{}
    workflow_argument_schema = [ordered]@{}
    workflow_output = [ordered]@{}
    workflow_export = [ordered]@{}
    plugin_lifecycle = [ordered]@{}
    marketplace = [ordered]@{}
    automation_semantics = [ordered]@{}
    accessibility_modes = @()
    passed = $false
    error = $null
}
$paletteProcess = $null
$paletteMenuProcess = $null
$superPanelProcess = $null
$superPanelTransitionProcess = $null
$managementProcess = $null
$workflowPaletteProcess = $null
$workflowPanelProcess = $null
$workflowSchemaWideProcess = $null
$workflowSchemaCompactProcess = $null
$workflowOutputProcess = $null
$pluginProcess = $null
$marketProcess = $null
$accessibilityProcess = $null

try {
    if (-not $WorkflowOnly -and -not $WorkflowOutputOnly -and -not $WorkflowSchemaOnly) {
    Write-Stage 'Starting Command Palette host.'
    Set-Clipboard -Value 'long-ui-smoke-pending'
    $paletteProcess = Start-QualityHost '--quality-open-palette'
    $palette = Wait-Until {
        Find-WindowByAutomationId $paletteProcess.Id 'Long.CommandPalette'
    } 'Command Palette did not appear through Windows UI Automation.'
    $search = Wait-Until {
        Find-DescendantByAutomationId $palette 'Long.CommandPalette.Search'
    } 'Command Palette search box was not discoverable.'
    $results = Wait-Until {
        Find-DescendantByAutomationId $palette 'Long.CommandPalette.Results'
    } 'Command Palette result list was not discoverable.'
    $report.automation_semantics['palette'] = [ordered]@{
        window = Get-AutomationSemantics $palette 'ControlType.Window' 'Command Palette window semantics failed.'
        search = Get-AutomationSemantics $search 'ControlType.Edit' 'Command Palette search semantics failed.'
        results = Get-AutomationSemantics $results 'ControlType.List' 'Command Palette results semantics failed.'
    }

    Write-Stage 'Setting wifi through the standard UI Automation value pattern.'
    [LongDesktopInput]::Activate([IntPtr]$palette.Current.NativeWindowHandle) | Out-Null
    $search.SetFocus()
    $valuePattern = [Windows.Automation.ValuePattern]$search.GetCurrentPattern(
        [Windows.Automation.ValuePattern]::Pattern)
    $valuePattern.SetValue('wifi')
    Start-Sleep -Milliseconds 500
    Write-Stage "Search value after UI Automation input: '$($valuePattern.Current.Value)'."
    $wifi = Wait-Until {
        Find-DescendantByName $results 'Wi-Fi'
    } 'The Wi-Fi Windows setting result did not appear.'
    $focusConfirmed = Wait-Until { $search.Current.HasKeyboardFocus } `
        'The Command Palette search box did not receive keyboard focus.'

    Write-Stage 'Executing the selected secondary command through the quality window channel.'
    if ([LongDesktopInput]::WindowAction(
        [IntPtr]$palette.Current.NativeWindowHandle, 2) -ne 1) {
        throw 'The Command Palette rejected the selected secondary quality action.'
    }
    $clipboardConfirmed = Wait-Until {
        (Get-Clipboard -Raw).Trim() -eq 'ms-settings:network-wifi'
    } 'The secondary quality action did not copy the Wi-Fi URI.'
    Start-Sleep -Milliseconds 600
    $paletteStillVisible = $null -ne (Wait-Until {
        Find-WindowByAutomationId $paletteProcess.Id 'Long.CommandPalette'
    } 'The secondary quality action closed a keep-open Command Palette.')
    Write-Stage 'Dismissing the action Command Palette through the quality window channel.'
    if ([LongDesktopInput]::WindowAction(
        [IntPtr]$palette.Current.NativeWindowHandle, 3) -ne 1) {
        throw 'The Command Palette rejected the quality dismiss action.'
    }
    Wait-Until {
        $null -eq (Find-WindowByAutomationId $paletteProcess.Id 'Long.CommandPalette')
    } 'The quality dismiss action did not hide the Command Palette.' | Out-Null
    Stop-QualityHost $paletteProcess
    $paletteProcess = $null

    Write-Stage 'Starting an isolated Command Palette menu workflow.'
    Set-Clipboard -Value 'long-ui-menu-pending'
    $paletteMenuProcess = Start-QualityHost '--quality-open-palette'
    $menuPalette = Wait-Until {
        Find-WindowByAutomationId $paletteMenuProcess.Id 'Long.CommandPalette'
    } 'The menu-workflow Command Palette did not appear.'
    $menuSearch = Wait-Until {
        Find-DescendantByAutomationId $menuPalette 'Long.CommandPalette.Search'
    } 'The menu-workflow search box was not discoverable.'
    $menuResults = Wait-Until {
        Find-DescendantByAutomationId $menuPalette 'Long.CommandPalette.Results'
    } 'The menu-workflow result list was not discoverable.'
    $menuValuePattern = [Windows.Automation.ValuePattern]$menuSearch.GetCurrentPattern(
        [Windows.Automation.ValuePattern]::Pattern)
    $menuValuePattern.SetValue('wifi')
    Wait-Until {
        Find-DescendantByName $menuResults 'Wi-Fi'
    } 'The menu-workflow Wi-Fi result did not appear.' | Out-Null
    Write-Stage 'Opening the result secondary-action menu through UI Automation.'
    $moreActions = Wait-Until {
        $button = Find-DescendantByAutomationId $menuResults 'Long.Result.MoreActions'
        if ($null -eq $button) {
            $button = Find-DescendantByName $menuResults '更多结果操作'
        }
        $button
    } 'The result secondary-action button was not discoverable.'
    $report.automation_semantics.palette['more_actions'] = `
        Get-AutomationSemantics $moreActions 'ControlType.Button' `
            'Command Palette secondary-action semantics failed.'
    Invoke-AutomationElement $moreActions `
        'The result secondary-action button did not support InvokePattern.'
    $copyMenuItem = Wait-Until {
        Find-ProcessElementByAutomationId `
            $paletteMenuProcess.Id 'Long.Result.SecondaryAction.0'
    } 'The first secondary-action menu item did not appear.'
    Invoke-AutomationElement $copyMenuItem `
        'The first secondary-action menu item did not support InvokePattern.'
    $menuCopyConfirmed = Wait-Until {
        (Get-Clipboard -Raw).Trim() -eq 'ms-settings:network-wifi'
    } 'Invoking the secondary-action menu did not copy the Wi-Fi URI.'
    Start-Sleep -Milliseconds 600
    $menuKeptPaletteOpen = $null -ne (Wait-Until {
        Find-WindowByAutomationId $paletteMenuProcess.Id 'Long.CommandPalette'
    } 'The secondary-action menu closed the Command Palette after a keep-open copy action.')
    Write-Stage 'Dismissing the menu-workflow Command Palette.'
    [LongDesktopInput]::WindowAction(
        [IntPtr]$menuPalette.Current.NativeWindowHandle, 3) | Out-Null
    Wait-Until {
        $null -eq (Find-WindowByAutomationId $paletteMenuProcess.Id 'Long.CommandPalette')
    } 'Escape did not hide the menu-workflow Command Palette.' | Out-Null

    $report.palette = [ordered]@{
        window_discovered = $true
        search_discovered = $true
        results_discovered = $true
        search_keyboard_focus = [bool]$focusConfirmed
        wifi_result_discovered = $null -ne $wifi
        shift_enter_copied_uri = [bool]$clipboardConfirmed
        copy_kept_palette_open = $paletteStillVisible
        secondary_menu_opened = $null -ne $copyMenuItem
        secondary_menu_copied_uri = [bool]$menuCopyConfirmed
        secondary_menu_kept_palette_open = $menuKeptPaletteOpen
        escape_closed_palette = $true
        automation_transport = 'quality_window_message'
        physical_keyboard_validated = $false
    }
    Stop-QualityHost $paletteMenuProcess
    $paletteMenuProcess = $null

    Write-Stage 'Starting Super Panel host.'
    $superPanelProcess = Start-QualityHost '--quality-open-super-panel'
    $superPanel = Wait-Until {
        Find-WindowByAutomationId $superPanelProcess.Id 'Long.SuperPanel'
    } 'Super Panel did not appear through Windows UI Automation.'
    $panelResults = Wait-Until {
        Find-DescendantByAutomationId $superPanel 'Long.SuperPanel.Results'
    } 'Super Panel result list was not discoverable.'
    $report.automation_semantics['super_panel'] = [ordered]@{
        window = Get-AutomationSemantics $superPanel 'ControlType.Window' 'Super Panel window semantics failed.'
        results = Get-AutomationSemantics $panelResults 'ControlType.List' 'Super Panel results semantics failed.'
    }
    $superPanel.SetFocus()
    Write-Stage 'Dismissing Super Panel through the quality window channel.'
    [LongDesktopInput]::WindowAction(
        [IntPtr]$superPanel.Current.NativeWindowHandle, 3) | Out-Null
    Wait-Until {
        $null -eq (Find-WindowByAutomationId $superPanelProcess.Id 'Long.SuperPanel')
    } 'Escape did not hide the Super Panel.' | Out-Null

    $report.super_panel = [ordered]@{
        window_discovered = $true
        results_discovered = $null -ne $panelResults
        escape_closed_panel = $true
    }

    Stop-QualityHost $superPanelProcess
    $superPanelProcess = $null

    Write-Stage 'Starting Super Panel to Command Palette transition host.'
    $superPanelTransitionProcess = Start-QualityHost '--quality-open-super-panel'
    $transitionPanel = Wait-Until {
        Find-WindowByAutomationId $superPanelTransitionProcess.Id 'Long.SuperPanel'
    } 'Super Panel did not appear for the command-center transition.'
    $groups = Wait-Until {
        Find-DescendantByAutomationId $transitionPanel 'Long.SuperPanel.Groups'
    } 'Super Panel groups were not discoverable.'
    $openCommandCenter = Wait-Until {
        Find-DescendantByAutomationId `
            $transitionPanel 'Long.SuperPanel.OpenCommandCenter'
    } 'The Open Command Center button was not discoverable.'
    $report.automation_semantics.super_panel['groups'] = `
        Get-AutomationSemantics $groups 'ControlType.List' 'Super Panel group semantics failed.'
    $report.automation_semantics.super_panel['open_command_center'] = `
        Get-AutomationSemantics $openCommandCenter 'ControlType.Button' `
            'Super Panel command-center semantics failed.'
    Write-Stage 'Invoking the Super Panel to Command Palette transition.'
    Invoke-AutomationElement $openCommandCenter `
        'The Open Command Center button did not support InvokePattern.'
    Wait-Until {
        $null -eq (Find-WindowByAutomationId `
            $superPanelTransitionProcess.Id 'Long.SuperPanel')
    } 'Opening Command Center did not hide the Super Panel.' | Out-Null
    $transitionPalette = Wait-Until {
        Find-WindowByAutomationId `
            $superPanelTransitionProcess.Id 'Long.CommandPalette'
    } 'Opening Command Center did not show the Command Palette.'
    [LongDesktopInput]::WindowAction(
        [IntPtr]$transitionPalette.Current.NativeWindowHandle, 3) | Out-Null
    Wait-Until {
        $null -eq (Find-WindowByAutomationId `
            $superPanelTransitionProcess.Id 'Long.CommandPalette')
    } 'Escape did not close the transitioned Command Palette.' | Out-Null
    $report.super_panel['groups_discovered'] = $null -ne $groups
    $report.super_panel['open_command_center_invoked'] = $true
    $report.super_panel['panel_hidden_on_transition'] = $true
    $report.super_panel['palette_shown_on_transition'] = $true
    Stop-QualityHost $superPanelTransitionProcess
    $superPanelTransitionProcess = $null

    Write-Stage 'Starting Workspace management navigation workflow.'
    $managementProcess = Start-QualityHost @(
        '--language', 'en-US',
        '--quality-width', '1120',
        '--quality-height', '760')
    $managementMain = Wait-Until {
        Find-WindowByAutomationId $managementProcess.Id 'Long.MainWindow'
    } 'The Workspace management host did not appear.'
    $managementSearch = Wait-Until {
        Find-DescendantByAutomationId $managementMain 'Long.Workspace.Search'
    } 'The management-scoped search was not discoverable.'
    $managementSearchPattern =
        [Windows.Automation.ValuePattern]$managementSearch.GetCurrentPattern(
            [Windows.Automation.ValuePattern]::Pattern)
    $managementSearchPattern.SetValue('Market')
    $marketDestination = Wait-Until {
        Find-DescendantByAutomationId `
            $managementMain 'Long.Management.Destination.Market'
    } 'The filtered Plugin Market destination was not discoverable.'
    Wait-Until {
        $null -eq (Find-DescendantByAutomationId `
            $managementMain 'Long.Management.Destination.Settings')
    } 'Management search did not hide a nonmatching destination.' | Out-Null
    $managementSearchPattern.SetValue('')
    $settingsDestination = Wait-Until {
        Find-DescendantByAutomationId `
            $managementMain 'Long.Management.Destination.Settings'
    } 'Clearing management search did not restore the Settings destination.'
    $report.automation_semantics['management_navigation'] = [ordered]@{
        search = Get-AutomationSemantics $managementSearch 'ControlType.Edit' `
            'Management search semantics failed.'
        market_destination = Get-AutomationSemantics `
            $marketDestination 'ControlType.Button' `
            'Plugin Market destination semantics failed.'
        settings_destination = Get-AutomationSemantics `
            $settingsDestination 'ControlType.Button' `
            'Settings destination semantics failed.'
    }

    Write-Stage 'Opening Plugin Market from the management overview.'
    Invoke-AutomationElement $marketDestination `
        'The Plugin Market destination did not support InvokePattern.'
    $marketTab = Wait-Until {
        $candidate = Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleTab.marketplace:catalog'
        if ($null -ne $candidate -and
            $candidate.Current.ItemStatus -like 'active:true;*') {
            $candidate
        }
    } 'Opening Plugin Market did not create an active Workspace module tab.'
    $rootTab = Wait-Until {
        Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleTab.management:root'
    } 'The protected management root tab was not discoverable.'

    Write-Stage 'Returning to management and opening Settings.'
    Invoke-AutomationElement $rootTab `
        'The management root tab did not support InvokePattern.'
    Wait-Until {
        $rootTab.Current.ItemStatus -like 'active:true;*'
    } 'The management root tab did not become active.' | Out-Null
    $settingsDestination = Wait-Until {
        Find-DescendantByAutomationId `
            $managementMain 'Long.Management.Destination.Settings'
    } 'Settings was not restored after returning to management.'
    Invoke-AutomationElement $settingsDestination `
        'The Settings destination did not support InvokePattern.'
    $settingsTab = Wait-Until {
        $candidate = Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleTab.settings:root'
        if ($null -ne $candidate -and
            $candidate.Current.ItemStatus -like 'active:true;*') {
            $candidate
        }
    } 'Opening Settings did not create an active Workspace module tab.'
    $settingsClose = Wait-Until {
        Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleClose.settings:root'
    } 'The Settings module close action was not discoverable.'
    Invoke-AutomationElement $settingsClose `
        'The Settings module close action did not support InvokePattern.'
    Wait-Until {
        $null -eq (Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleTab.settings:root')
    } 'Closing Settings did not remove its Workspace tab.' | Out-Null
    Wait-Until {
        $rootTab.Current.ItemStatus -like 'active:true;*'
    } 'Closing Settings did not restore the management root.' | Out-Null

    Write-Stage 'Activating and closing Plugin Market through its module tab.'
    $marketTab = Wait-Until {
        Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleTab.marketplace:catalog'
    } 'The inactive Plugin Market module tab was not preserved.'
    Invoke-AutomationElement $marketTab `
        'The Plugin Market module tab did not support InvokePattern.'
    Wait-Until {
        $marketTab.Current.ItemStatus -like 'active:true;*'
    } 'The Plugin Market module tab did not become active.' | Out-Null
    $marketClose = Wait-Until {
        Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleClose.marketplace:catalog'
    } 'The Plugin Market module close action was not discoverable.'
    Invoke-AutomationElement $marketClose `
        'The Plugin Market module close action did not support InvokePattern.'
    Wait-Until {
        $null -eq (Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleTab.marketplace:catalog')
    } 'Closing Plugin Market did not remove its Workspace tab.' | Out-Null
    Wait-Until {
        $rootTab.Current.ItemStatus -like 'active:true;*'
    } 'Closing Plugin Market did not restore the management root.' | Out-Null
    $report.management_navigation = [ordered]@{
        stable_destination_ids = $true
        scoped_search_filtered = $true
        market_opened_as_module = $true
        settings_close_restored_root = $true
        market_tab_reactivated = $true
        market_close_restored_root = $true
        coordinate_clicks_used = $false
        physical_keyboard_validated = $false
    }
    Stop-QualityHost $managementProcess
    $managementProcess = $null
    }

    if (-not $WorkflowOutputOnly -and -not $WorkflowSchemaOnly) {
    Write-Stage 'Starting managed workflow review from Command Palette.'
    $workflowPaletteProcess = Start-QualityHost @(
        '--quality-open-palette',
        '--quality-width', '1440',
        '--quality-height', '800')
    $workflowPalette = Wait-Until {
        Find-WindowByAutomationId $workflowPaletteProcess.Id 'Long.CommandPalette'
    } 'Command Palette did not appear for the managed workflow review.'
    $workflowSearch = Wait-Until {
        Find-DescendantByAutomationId $workflowPalette 'Long.CommandPalette.Search'
    } 'Workflow review search was not discoverable.'
    $workflowResults = Wait-Until {
        Find-DescendantByAutomationId $workflowPalette 'Long.CommandPalette.Results'
    } 'Workflow review results were not discoverable.'
    [LongDesktopInput]::Activate(
        [IntPtr]$workflowPalette.Current.NativeWindowHandle) | Out-Null
    $workflowSearch.SetFocus()
    Wait-Until { $workflowSearch.Current.HasKeyboardFocus } `
        'Workflow review search did not receive keyboard focus before selection.' |
        Out-Null
    $workflowValuePattern = [Windows.Automation.ValuePattern]$workflowSearch.GetCurrentPattern(
        [Windows.Automation.ValuePattern]::Pattern)
    $workflowValuePattern.SetValue('Quality Workflow Review')
    $paletteWorkflowResult = Wait-Until {
        Find-DescendantByName $workflowResults 'Quality Workflow Review'
    } 'The managed workflow did not appear in Command Palette search.'
    $paletteWorkflowItem = Wait-Until {
        Find-AncestorByControlType $paletteWorkflowResult `
            ([Windows.Automation.ControlType]::ListItem)
    } 'The managed workflow Command Palette item was not selectable.'
    $paletteSelectionPattern = $null
    if (-not $paletteWorkflowItem.TryGetCurrentPattern(
        [Windows.Automation.SelectionItemPattern]::Pattern,
        [ref]$paletteSelectionPattern)) {
        throw 'The managed workflow Command Palette item did not expose SelectionItemPattern.'
    }
    if ([LongDesktopInput]::WindowAction(
        [IntPtr]$workflowPalette.Current.NativeWindowHandle, 4) -ne 1) {
        throw 'The Command Palette rejected the exact-title selection action.'
    }
    $workflowSearch.SetFocus()
    Write-Stage 'Opening the managed workflow review with Enter.'
    if ([LongDesktopInput]::WindowAction(
        [IntPtr]$workflowPalette.Current.NativeWindowHandle, 1) -ne 1) {
        throw 'The Command Palette rejected the selected workflow action.'
    }
    $paletteWorkflowMain = Wait-Until {
        Find-WindowByAutomationId $workflowPaletteProcess.Id 'Long.MainWindow'
    } 'The main window did not appear after opening a workflow from Command Palette.'
    $paletteWorkflowMainHandle = Wait-Until {
        Find-WindowHandleByAutomationId `
            $workflowPaletteProcess.Id 'Long.MainWindow'
    } 'The main workflow window handle was not discoverable.'
    $paletteWorkflowReview = Wait-Until {
        $paletteWorkflowMain.Current.ItemStatus -like `
            'workflow-review:workflow.quality.review;layout:*;width:*'
    } 'The workflow permission review did not appear from Command Palette.'
    $paletteWorkflowCancel = Wait-Until {
        Find-DescendantByAutomationId `
            $paletteWorkflowMain 'Long.Workflow.ReviewCancel'
    } 'The top-level workflow review cancel action was not discoverable.'
    $report.automation_semantics['workflow_review'] = [ordered]@{
        cancel = Get-AutomationSemantics $paletteWorkflowCancel 'ControlType.Button' `
            'Workflow review cancel semantics failed.'
    }
    Wait-Until {
        $null -eq (Find-WindowByAutomationId `
            $workflowPaletteProcess.Id 'Long.CommandPalette')
    } 'Opening a workflow did not hide Command Palette.' | Out-Null
    Write-Stage 'Validating the wide workflow review layout.'
    Start-Sleep -Milliseconds 500
    Write-Stage "Wide workflow probe: Width=$($paletteWorkflowMain.Current.BoundingRectangle.Width), Status=$($paletteWorkflowMain.Current.ItemStatus)"
    $wideLayoutAnnounced = Wait-Until {
        $paletteWorkflowMain.Current.ItemStatus -like `
            'workflow-review:workflow.quality.review;layout:wide;width:*'
    } 'The workflow wide layout was not announced by the main window.'
    Write-Stage 'Closing review and duplicating through the quality automation channel.'
    Invoke-WindowWorkflowAction $paletteWorkflowMainHandle 7 `
        'The workflow duplicate action was not accepted.'
    $duplicateStatus = Wait-Until {
        $status = [LongDesktopInput]::WorkflowMessage(
            $paletteWorkflowMainHandle,
            16)
        if ($status -eq 1) { return $true }
        if ($status -eq -1) { throw 'The workflow duplicate action failed.' }
        return $null
    } 'The workflow duplicate action did not complete.'
    $workflowFilesAfterDuplicate = @(
        Get-ChildItem -LiteralPath $workflowRoot -Filter '*.workflow.json' -File)
    $sourceHashAfterDuplicate = if (
        Test-Path -LiteralPath $sourceWorkflowPath -PathType Leaf) {
        (Get-FileHash -LiteralPath $sourceWorkflowPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $sourceFilePreserved = $sourceHashAfterDuplicate -eq $sourceWorkflowSha256
    $duplicateRemainedUnsaved = $workflowFilesAfterDuplicate.Count -eq 1 `
        -and $workflowFilesAfterDuplicate[0].FullName -eq $sourceWorkflowPath
    if (-not $sourceFilePreserved) {
        throw 'Duplicating the workflow removed or replaced the source definition.'
    }
    if (-not $duplicateRemainedUnsaved) {
        throw 'Duplicating the workflow wrote a definition before an explicit save.'
    }
    Stop-QualityHost $workflowPaletteProcess
    $workflowPaletteProcess = $null

    Write-Stage 'Starting managed workflow review from Super Panel.'
    $workflowPanelProcess = Start-QualityHost @(
        '--quality-open-super-panel',
        '--quality-empty-context',
        '--quality-width', '720',
        '--quality-height', '560')
    $workflowPanel = Wait-Until {
        Find-WindowByAutomationId $workflowPanelProcess.Id 'Long.SuperPanel'
    } 'Super Panel did not appear for the managed workflow review.'
    $workflowPanelResults = Wait-Until {
        Find-DescendantByAutomationId $workflowPanel 'Long.SuperPanel.Results'
    } 'Super Panel workflow results were not discoverable.'
    [LongDesktopInput]::Activate(
        [IntPtr]$workflowPanel.Current.NativeWindowHandle) | Out-Null
    $workflowPanelResults.SetFocus()
    $panelWorkflowResult = Wait-Until {
        Find-DescendantByName $workflowPanelResults 'Quality Workflow Review'
    } 'The managed workflow did not appear in Super Panel.'
    $panelWorkflowItem = Wait-Until {
        Find-AncestorByControlType $panelWorkflowResult `
            ([Windows.Automation.ControlType]::ListItem)
    } 'The managed workflow Super Panel item was not selectable.'
    $panelSelectionPattern = $null
    if (-not $panelWorkflowItem.TryGetCurrentPattern(
        [Windows.Automation.SelectionItemPattern]::Pattern,
        [ref]$panelSelectionPattern)) {
        throw 'The managed workflow Super Panel item did not expose SelectionItemPattern.'
    }
    if ([LongDesktopInput]::WindowAction(
        [IntPtr]$workflowPanel.Current.NativeWindowHandle, 4) -ne 1) {
        throw 'Super Panel rejected the deterministic first-result action.'
    }
    $workflowPanelResults.SetFocus()
    Write-Stage 'Opening the managed workflow review from Super Panel with Enter.'
    if ([LongDesktopInput]::WindowAction(
        [IntPtr]$workflowPanel.Current.NativeWindowHandle, 1) -ne 1) {
        throw 'Super Panel rejected the selected workflow action.'
    }
    $panelWorkflowMain = Wait-Until {
        Find-WindowByAutomationId $workflowPanelProcess.Id 'Long.MainWindow'
    } 'The main window did not appear after opening a workflow from Super Panel.'
    $panelWorkflowReview = Wait-Until {
        $panelWorkflowMain.Current.ItemStatus -like `
            'workflow-review:workflow.quality.review*'
    } 'The workflow permission review did not appear from Super Panel.'
    Write-Stage "Narrow workflow initial probe: Width=$($panelWorkflowMain.Current.BoundingRectangle.Width), Status=$($panelWorkflowMain.Current.ItemStatus)"
    $compactLayoutAnnounced = Wait-Until {
        $panelWorkflowMain.Current.ItemStatus -like `
            'workflow-review:workflow.quality.review;layout:compact;width:*'
    } 'The workflow compact layout was not announced by the narrow main window.'
    Write-Stage "Compact workflow probe: Width=$($panelWorkflowMain.Current.BoundingRectangle.Width), Status=$($panelWorkflowMain.Current.ItemStatus)"
    $panelWorkflowCancel = Wait-Until {
        Find-DescendantByAutomationId `
            $panelWorkflowMain 'Long.Workflow.ReviewCancel'
    } 'The Super Panel workflow review cancel action was not discoverable.'
    Wait-Until {
        $null -eq (Find-WindowByAutomationId `
            $workflowPanelProcess.Id 'Long.SuperPanel')
    } 'Opening a workflow did not hide Super Panel.' | Out-Null
    Invoke-AutomationElement $panelWorkflowCancel `
        'The Super Panel workflow review cancel action did not support InvokePattern.'
    Wait-Until {
        [string]::IsNullOrWhiteSpace($panelWorkflowMain.Current.ItemStatus)
    } 'The cancel action did not close the workflow review opened from Super Panel.' | Out-Null
    $report.workflow_review = [ordered]@{
        isolated_workflow_root = $workflowRoot
        palette_result_discovered = $null -ne $paletteWorkflowResult
        palette_enter_opened_review = $null -ne $paletteWorkflowReview
        palette_hidden_on_navigation = $true
        palette_review_closed_for_duplicate = $true
        duplicate_action_completed = [bool]$duplicateStatus
        duplicate_remained_unsaved = [bool]$duplicateRemainedUnsaved
        source_file_preserved = [bool]$sourceFilePreserved
        source_file_sha256 = $sourceWorkflowSha256
        wide_layout_announced = [bool]$wideLayoutAnnounced
        compact_layout_announced = [bool]$compactLayoutAnnounced
        super_panel_result_discovered = $null -ne $panelWorkflowResult
        super_panel_enter_opened_review = $null -ne $panelWorkflowReview
        super_panel_hidden_on_navigation = $true
        super_panel_cancel_closed_review = $true
        execution_was_not_confirmed = $true
        selection_transport = 'quality_window_message'
    }
    Stop-QualityHost $workflowPanelProcess
    $workflowPanelProcess = $null
    }

    if (-not $WorkflowOutputOnly) {
    Write-Stage 'Validating UUID argument schema in the wide workflow editor.'
    $workflowSchemaWideProcess = Start-QualityHost @(
        '--quality-edit-workflow',
        'workflow.quality.review',
        '--quality-width', '1280',
        '--quality-height', '800')
    $schemaWideMain = Wait-Until {
        Find-WindowByAutomationId $workflowSchemaWideProcess.Id 'Long.MainWindow'
    } 'The wide workflow schema host did not appear.'
    $schemaWideEditor = Wait-Until {
        Find-RawDescendantByAutomationId `
            $schemaWideMain 'Long.Workflow.Editor' 14
    } 'The wide workflow editor was not discoverable.'
    $schemaWideLayout = Wait-Until {
        if ($schemaWideEditor.Current.ItemStatus -like 'layout:wide;width:*') {
            $schemaWideEditor.Current.ItemStatus
        }
    } 'The wide workflow layout was not announced.'
    $schemaInvocation = Wait-Until {
        Find-RawDescendantByAutomationId `
            $schemaWideEditor 'Long.Workflow.Invocation.Primary' 12
    } 'The primary workflow invocation editor was not discoverable.'
    $schemaAmount = Wait-Until {
        Find-DescendantByAutomationId $schemaWideEditor 'amount'
    } 'The UUID integer schema editor was not discoverable.'
    $schemaUppercase = Wait-Until {
        Find-DescendantByAutomationId $schemaWideEditor 'uppercase'
    } 'The UUID uppercase boolean editor was not discoverable.'
    $schemaCompact = Wait-Until {
        Find-DescendantByAutomationId $schemaWideEditor 'compact'
    } 'The UUID compact boolean editor was not discoverable.'
    $schemaAmountValue = [Windows.Automation.ValuePattern]$schemaAmount.GetCurrentPattern(
        [Windows.Automation.ValuePattern]::Pattern)
    $schemaAmount.SetFocus()
    $schemaAmountValue.SetValue('25')
    Wait-Until { $schemaAmountValue.Current.Value -eq '25' } `
        'The UUID integer schema editor did not accept a valid value.' | Out-Null
    Set-AutomationToggleOn $schemaUppercase `
        'The UUID uppercase schema toggle did not accept input.'
    $schemaPreset = Wait-Until {
        Find-RawDescendantByAutomationId `
            $schemaInvocation 'Long.Workflow.ArgumentPreset' 8
    } 'The UUID argument preset selector was not discoverable.'
    $schemaPresetApply = Wait-Until {
        Find-RawDescendantByAutomationId `
            $schemaInvocation 'Long.Workflow.ArgumentPreset.Apply' 8
    } 'The UUID argument preset apply action was not discoverable.'
    $report.workflow_argument_schema['wide'] = [ordered]@{
        editor_discovered = $true
        layout_status = $schemaWideLayout
        amount = Get-AutomationSemantics $schemaAmount 'ControlType.Edit' `
            'UUID amount schema semantics failed.'
        uppercase = Get-AutomationSemantics $schemaUppercase 'ControlType.CheckBox' `
            'UUID uppercase schema semantics failed.'
        compact = Get-AutomationSemantics $schemaCompact 'ControlType.CheckBox' `
            'UUID compact schema semantics failed.'
        amount_value_changed = $schemaAmountValue.Current.Value -eq '25'
        uppercase_toggled = $true
        preset_selector_discovered = $null -ne $schemaPreset
        preset_apply_discovered = $null -ne $schemaPresetApply
    }
    Stop-QualityHost $workflowSchemaWideProcess
    $workflowSchemaWideProcess = $null

    Write-Stage 'Validating UUID argument schema in the compact workflow editor.'
    $workflowSchemaCompactProcess = Start-QualityHost @(
        '--quality-edit-workflow',
        'workflow.quality.review',
        '--quality-width', '720',
        '--quality-height', '560')
    $schemaCompactMain = Wait-Until {
        Find-WindowByAutomationId $workflowSchemaCompactProcess.Id 'Long.MainWindow'
    } 'The compact workflow schema host did not appear.'
    $schemaCompactEditor = Wait-Until {
        Find-RawDescendantByAutomationId `
            $schemaCompactMain 'Long.Workflow.Editor' 14
    } 'The compact workflow editor was not discoverable.'
    $schemaCompactLayout = Wait-Until {
        if ($schemaCompactEditor.Current.ItemStatus -like 'layout:compact;width:*') {
            $schemaCompactEditor.Current.ItemStatus
        }
    } 'The compact workflow layout was not announced.'
    $schemaCompactInvocation = Wait-Until {
        Find-RawDescendantByAutomationId `
            $schemaCompactEditor 'Long.Workflow.Invocation.Primary' 12
    } 'The compact primary workflow invocation editor was not discoverable.'
    $schemaCompactAmount = Wait-Until {
        Find-DescendantByAutomationId $schemaCompactEditor 'amount'
    } 'The compact UUID integer schema editor was not discoverable.'
    $schemaCompactAmountValue = [Windows.Automation.ValuePattern]`
        $schemaCompactAmount.GetCurrentPattern(
            [Windows.Automation.ValuePattern]::Pattern)
    $schemaCompactAmount.SetFocus()
    $schemaCompactAmountValue.SetValue('100')
    Wait-Until { $schemaCompactAmountValue.Current.Value -eq '100' } `
        'The compact UUID schema editor did not accept its maximum value.' | Out-Null
    $schemaCompactToggle = Wait-Until {
        Find-DescendantByAutomationId $schemaCompactEditor 'compact'
    } 'The compact UUID boolean schema editor was not discoverable.'
    Set-AutomationToggleOn $schemaCompactToggle `
        'The compact UUID schema toggle did not accept input.'
    $report.workflow_argument_schema['compact'] = [ordered]@{
        editor_discovered = $true
        layout_status = $schemaCompactLayout
        amount = Get-AutomationSemantics $schemaCompactAmount 'ControlType.Edit' `
            'Compact UUID amount schema semantics failed.'
        compact = Get-AutomationSemantics $schemaCompactToggle 'ControlType.CheckBox' `
            'Compact UUID boolean schema semantics failed.'
        maximum_value_changed = $schemaCompactAmountValue.Current.Value -eq '100'
        compact_toggled = $true
    }
    Stop-QualityHost $workflowSchemaCompactProcess
    $workflowSchemaCompactProcess = $null
    }

    if (-not $WorkflowSchemaOnly) {
    Write-Stage 'Starting approved long terminal-output workflow.'
    $workflowOutputArguments = @(
        '--quality-open-workflow',
        'workflow.quality.review')
    if ($WorkflowExportMatrix) {
        $workflowOutputArguments += @(
            '--quality-terminal-export-dir',
            $exportMatrixRoot)
    }
    $workflowOutputProcess = Start-QualityHost $workflowOutputArguments
    $outputMainHandle = Wait-Until {
        [LongDesktopInput]::TopLevelWindows($workflowOutputProcess.Id) |
            Select-Object -First 1
    } 'The main window did not appear for the long-output workflow.'
    Wait-Until {
        [LongDesktopInput]::WorkflowMessage($outputMainHandle, 10) -eq 1
    } 'The long-output workflow review did not appear.' | Out-Null
    Write-Stage 'Long-output workflow review opened.'
    Invoke-WindowWorkflowAction $outputMainHandle 2 `
        'The window-level terminal-output approval could not be invoked.'
    Start-Sleep -Milliseconds 500
    Write-Stage 'Long-output approval enabled with the visible review control.'
    Write-Stage 'Confirming the isolated read-only long-output workflow.'
    Invoke-WindowWorkflowAction $outputMainHandle 3 `
        'The window-level workflow confirmation could not be invoked.'
    Start-Sleep -Milliseconds 500
    $terminalOutputLength = Wait-Until {
        $length = [LongDesktopInput]::WorkflowMessage($outputMainHandle, 11)
        if ($length -ge 3600) { $length }
    } 'The isolated long-output workflow did not complete with bounded output.'
    if ($WorkflowExportMatrix) {
        Write-Stage 'Running the isolated terminal-output export refusal matrix.'
        Invoke-WindowWorkflowAction $outputMainHandle 6 `
            'The window-level terminal-output export matrix could not be invoked.'
        $exportMatrixStatus = Wait-Until {
            $status = [LongDesktopInput]::WorkflowMessage($outputMainHandle, 15)
            if ($status -eq 1 -or $status -eq -1) { $status }
        } 'The terminal-output export matrix did not complete.'
        if ($exportMatrixStatus -ne 1) {
            throw 'The terminal-output export matrix reported a failure.'
        }
        $exportMatrixEvidencePath = Join-Path $exportMatrixRoot 'host-export-matrix.json'
        $exportMatrixEvidence = Wait-Until {
            if (Test-Path -LiteralPath $exportMatrixEvidencePath) {
                Get-Content -LiteralPath $exportMatrixEvidencePath -Raw -Encoding utf8 |
                    ConvertFrom-Json
            }
        } 'The terminal-output export matrix evidence was not written.'
        $report.workflow_export = $exportMatrixEvidence
    }
    Invoke-WindowWorkflowAction $outputMainHandle 4 `
        'The window-level terminal output clear action could not be invoked.'
    Start-Sleep -Milliseconds 500
    Wait-Until {
        [LongDesktopInput]::WorkflowMessage($outputMainHandle, 12) -eq 1
    } 'Clearing terminal output did not clear the in-memory value.' | Out-Null
    $isolatedReportRoot = Join-Path $workflowRoot '.reports'
    $isolatedReport = Wait-Until {
        if (Test-Path -LiteralPath $isolatedReportRoot) {
            Get-ChildItem -LiteralPath $isolatedReportRoot -File -Filter '*.json' `
                -Recurse | Select-Object -First 1
        }
    } 'The long-output execution report was not written to the isolated report root.'
    $report.workflow_output = [ordered]@{
        isolated_report_root = $isolatedReportRoot
        read_only_execution_confirmed = $true
        review_state_cleared_after_confirmation = $true
        execution_completed = $true
        terminal_output_approved = $true
        terminal_output_length = $terminalOutputLength
        terminal_output_bounded_scroll = $true
        terminal_output_cleared = $true
        isolated_report_written = $null -ne $isolatedReport
    }
    Stop-QualityHost $workflowOutputProcess
    $workflowOutputProcess = $null
    }

    if (-not $WorkflowOnly -and -not $WorkflowOutputOnly -and -not $WorkflowSchemaOnly) {
    Write-Stage 'Starting Workspace Base64 plugin workflow.'
    $pluginProcess = Start-QualityHost @(
        '--quality-open-plugin-runtime', 'com.long.base64')
    $mainWindow = Wait-Until {
        Find-WindowByAutomationId $pluginProcess.Id 'Long.MainWindow'
    } 'The main window did not appear for the Workspace plugin workflow.'
    $workspaceRuntime = Wait-Until {
        Find-DescendantByAutomationId `
            $mainWindow 'Long.Workspace.PluginRuntime.Title'
    } 'The Base64 Workspace runtime did not appear.'
    $detach = Wait-Until {
        Find-DescendantByAutomationId `
            $mainWindow 'Long.Workspace.PluginRuntime.Detach'
    } 'The Workspace plugin detach button was not discoverable.'
    $report.automation_semantics['plugin_lifecycle'] = [ordered]@{
        main_window = Get-AutomationSemantics $mainWindow 'ControlType.Window' 'Main window semantics failed.'
        detach = Get-AutomationSemantics $detach 'ControlType.Button' 'Plugin detach semantics failed.'
    }
    Write-Stage 'Detaching the Workspace plugin through UI Automation.'
    Invoke-AutomationElement $detach `
        'The Workspace plugin detach button did not support InvokePattern.'
    $detachedWindow = Wait-Until {
        Find-WindowByAutomationId $pluginProcess.Id 'Long.Plugin.DetachedWindow'
    } 'The detached plugin window did not appear.'
    $detachedBack = Wait-Until {
        Find-DescendantByAutomationId $detachedWindow 'Long.Plugin.DetachedBack'
    } 'The detached plugin Back button was not discoverable.'
    $report.automation_semantics.plugin_lifecycle['detached_window'] = `
        Get-AutomationSemantics $detachedWindow 'ControlType.Window' `
            'Detached plugin window semantics failed.'
    $report.automation_semantics.plugin_lifecycle['back'] = `
        Get-AutomationSemantics $detachedBack 'ControlType.Button' `
            'Detached plugin Back semantics failed.'
    Write-Stage 'Returning from the detached plugin with Escape.'
    [LongDesktopInput]::WindowAction(
        [IntPtr]$detachedWindow.Current.NativeWindowHandle, 3) | Out-Null
    Wait-Until {
        $null -eq (Find-WindowByAutomationId `
            $pluginProcess.Id 'Long.Plugin.DetachedWindow')
    } 'Escape did not close the detached plugin window.' | Out-Null
    $restoredRuntime = Wait-Until {
        Find-DescendantByAutomationId `
            $mainWindow 'Long.Workspace.PluginRuntime.Title'
    } 'Returning from the detached plugin did not restore the Workspace runtime.'
    $report.plugin_lifecycle = [ordered]@{
        main_window_discovered = $true
        workspace_runtime_discovered = $null -ne $workspaceRuntime
        detach_invoked = $true
        detached_window_discovered = $true
        detached_back_discovered = $null -ne $detachedBack
        escape_closed_detached_window = $true
        workspace_runtime_restored = $null -ne $restoredRuntime
    }
    Stop-QualityHost $pluginProcess
    $pluginProcess = $null

    Write-Stage 'Starting Marketplace search and uninstall-preview workflow.'
    $marketProcess = Start-QualityHost '--quality-open-market'
    $marketMain = Wait-Until {
        Find-WindowByAutomationId $marketProcess.Id 'Long.MainWindow'
    } 'The main window did not appear for the Marketplace workflow.'
    $marketSearch = Wait-Until {
        Find-ProcessElementByAutomationId $marketProcess.Id 'Long.Workspace.Search'
    } 'The workspace-scoped Marketplace search box was not discoverable.'
    $marketResults = Wait-Until {
        Find-ProcessElementByAutomationId $marketProcess.Id 'Long.Marketplace.Results'
    } 'The Marketplace result list was not discoverable.'
    $report.automation_semantics['marketplace'] = [ordered]@{
        search = Get-AutomationSemantics $marketSearch 'ControlType.Edit' 'Marketplace search semantics failed.'
        results = Get-AutomationSemantics $marketResults 'ControlType.List' 'Marketplace results semantics failed.'
    }
    $marketCount = Wait-Until {
        $element = Find-ProcessElementByAutomationId `
            $marketProcess.Id 'Long.Marketplace.ResultCount'
        if ($null -ne $element -and $element.Current.Name -notmatch ' 0 ') { $element }
    } 'The trusted Marketplace catalog did not populate.'
    $marketListItem = Wait-Until {
        Find-DescendantByControlType `
            $marketResults ([Windows.Automation.ControlType]::ListItem)
    } 'The Marketplace list item was not exposed to UI Automation.'
    $report.automation_semantics.marketplace['result_item'] = `
        Get-AutomationSemantics $marketListItem 'ControlType.ListItem' `
            'Marketplace result item semantics failed.'
    $report.automation_semantics.marketplace.result_item['item_status'] = `
        [string]$marketListItem.Current.ItemStatus
    Select-AutomationElement $marketListItem `
        'The Marketplace result item did not support SelectionItemPattern.'
    if ([LongDesktopInput]::WindowAction(
        [IntPtr]$marketMain.Current.NativeWindowHandle, 1) -ne 1) {
        throw 'The Marketplace rejected the selected-detail quality action.'
    }
    $detailName = Wait-Until {
        Find-ProcessElementByAutomationId $marketProcess.Id 'Long.Marketplace.DetailName'
    } 'The selected Marketplace plugin detail did not appear.'
    $marketValuePattern = [Windows.Automation.ValuePattern]$marketSearch.GetCurrentPattern(
        [Windows.Automation.ValuePattern]::Pattern)
    Write-Stage 'Verifying the Marketplace zero-result state.'
    $marketValuePattern.SetValue('__long_no_matching_plugin__')
    $zeroCount = Wait-Until {
        $element = Find-ProcessElementByAutomationId `
            $marketProcess.Id 'Long.Marketplace.ResultCount'
        if ($null -ne $element -and $element.Current.Name -match ' 0 ') { $element }
    } 'Marketplace search did not reach the zero-result state.'
    $marketValuePattern.SetValue('')
    $restoredCount = Wait-Until {
        $element = Find-ProcessElementByAutomationId `
            $marketProcess.Id 'Long.Marketplace.ResultCount'
        if ($null -ne $element -and $element.Current.Name -notmatch ' 0 ') { $element }
    } 'Clearing Marketplace search did not restore the trusted catalog.'
    $restoredMarketItem = Wait-Until {
        Find-DescendantByControlType `
            $marketResults ([Windows.Automation.ControlType]::ListItem)
    } 'The restored Marketplace catalog did not expose an installed item.'
    Select-AutomationElement $restoredMarketItem `
        'The restored Marketplace item did not support selection.'
    if ([LongDesktopInput]::WindowAction(
        [IntPtr]$marketMain.Current.NativeWindowHandle, 1) -ne 1) {
        throw 'The Marketplace rejected the restored selected-detail action.'
    }
    $uninstall = Wait-Until {
        Find-ProcessElementByAutomationId $marketProcess.Id 'Long.Marketplace.Uninstall'
    } 'The installed plugin uninstall-preview button was not discoverable.'
    $report.automation_semantics.marketplace['uninstall'] = `
        Get-AutomationSemantics $uninstall 'ControlType.Button' `
            'Marketplace uninstall semantics failed.'
    Write-Stage 'Opening and cancelling the Marketplace uninstall confirmation.'
    $uninstall.SetFocus()
    Wait-Until {
        Get-FocusedElementByAutomationId 'Long.Marketplace.Uninstall'
    } 'Marketplace uninstall did not accept keyboard focus.' | Out-Null
    Invoke-AutomationElement $uninstall `
        'The Marketplace uninstall-preview button did not support InvokePattern.'
    $confirmTitle = Wait-Until {
        Find-ProcessElementByAutomationId `
            $marketProcess.Id 'Long.Marketplace.ConfirmTitle'
    } 'The Marketplace uninstall confirmation did not appear.'
    $confirmCancel = Wait-Until {
        Find-ProcessElementByAutomationId `
            $marketProcess.Id 'Long.Marketplace.ConfirmCancel'
    } 'The Marketplace confirmation cancel button was not discoverable.'
    $report.automation_semantics.marketplace['confirm_cancel'] = `
        Get-AutomationSemantics $confirmCancel 'ControlType.Button' `
            'Marketplace confirmation semantics failed.'
    $confirmAction = Wait-Until {
        Find-ProcessElementByAutomationId `
            $marketProcess.Id 'Long.Marketplace.ConfirmAction'
    } 'The Marketplace confirmation action was not discoverable.'
    $report.automation_semantics.marketplace['confirm_action'] = `
        Get-AutomationSemantics $confirmAction 'ControlType.Button' `
            'Marketplace confirmation action semantics failed.'
    Invoke-AutomationElement $confirmCancel `
        'The Marketplace confirmation cancel button did not support InvokePattern.'
    Wait-Until {
        $null -eq (Find-ProcessElementByAutomationId `
            $marketProcess.Id 'Long.Marketplace.ConfirmTitle')
    } 'Cancelling the Marketplace confirmation did not close the overlay.' | Out-Null
    $uninstallStillAvailable = Wait-Until {
        Find-ProcessElementByAutomationId $marketProcess.Id 'Long.Marketplace.Uninstall'
    } 'Cancelling uninstall changed the installed plugin state.'
    $uninstallFocusRestored = Wait-Until {
        Get-FocusedElementByAutomationId 'Long.Marketplace.Uninstall'
    } 'Cancelling uninstall did not restore focus to its trigger.'
    $report.marketplace = [ordered]@{
        main_window_discovered = $null -ne $marketMain
        search_discovered = $null -ne $marketSearch
        results_discovered = $null -ne $marketResults
        trusted_catalog_loaded = $null -ne $marketCount
        detail_discovered = $null -ne $detailName
        zero_result_state_verified = $null -ne $zeroCount
        catalog_restored_after_clear = $null -ne $restoredCount
        uninstall_confirmation_opened = $null -ne $confirmTitle
        uninstall_confirmation_cancelled = $true
        installed_state_preserved = $null -ne $uninstallStillAvailable
        uninstall_focus_restored = $null -ne $uninstallFocusRestored
    }
    Stop-QualityHost $marketProcess
    $marketProcess = $null

    $accessibilityModes = @(
        [ordered]@{ name = 'high_contrast'; arguments = @('--quality-open-palette', '--quality-high-contrast'); high = $true; reduced = $false },
        [ordered]@{ name = 'reduced_motion'; arguments = @('--quality-open-palette', '--quality-reduce-motion'); high = $false; reduced = $true },
        [ordered]@{ name = 'combined'; arguments = @('--quality-open-palette', '--quality-high-contrast', '--quality-reduce-motion'); high = $true; reduced = $true }
    )
    foreach ($mode in $accessibilityModes) {
        Write-Stage "Starting accessibility workflow: $($mode.name)."
        $accessibilityProcess = Start-QualityHost $mode.arguments
        $modePalette = Wait-Until {
            Find-WindowByAutomationId $accessibilityProcess.Id 'Long.CommandPalette'
        } "Command Palette did not appear for accessibility mode $($mode.name)."
        $modeSearch = Wait-Until {
            Find-DescendantByAutomationId $modePalette 'Long.CommandPalette.Search'
        } "Search was not discoverable for accessibility mode $($mode.name)."
        $modeSearch.SetFocus()
        $modeFocus = Wait-Until { $modeSearch.Current.HasKeyboardFocus } `
            "Search did not receive focus for accessibility mode $($mode.name)."
        $modeLog = Wait-Until { Get-LastAccessibilityLogLine } `
            "Accessibility state was not logged for mode $($mode.name)."
        if ($mode.high -and $modeLog -notmatch 'HighContrast=true') {
            throw "High contrast was not active for mode $($mode.name): $modeLog"
        }
        if ($mode.reduced -and $modeLog -notmatch 'ReducedMotion=true') {
            throw "Reduced motion was not active for mode $($mode.name): $modeLog"
        }
        [LongDesktopInput]::WindowAction(
            [IntPtr]$modePalette.Current.NativeWindowHandle, 3) | Out-Null
        Wait-Until {
            $null -eq (Find-WindowByAutomationId `
                $accessibilityProcess.Id 'Long.CommandPalette')
        } "Escape did not close accessibility mode $($mode.name)." | Out-Null
        $report.accessibility_modes += [ordered]@{
            name = $mode.name
            palette_discovered = $true
            search_keyboard_focus = [bool]$modeFocus
            search_accessible_name = [string]$modeSearch.Current.Name
            search_control_type = [string]$modeSearch.Current.ControlType.ProgrammaticName
            high_contrast_active = $modeLog -match 'HighContrast=true'
            reduced_motion_active = $modeLog -match 'ReducedMotion=true'
            requested_state_confirmed = `
                ((-not $mode.high) -or $modeLog -match 'HighContrast=true') -and `
                ((-not $mode.reduced) -or $modeLog -match 'ReducedMotion=true')
            escape_closed_palette = $true
        }
        Stop-QualityHost $accessibilityProcess
        $accessibilityProcess = $null
    }
    }
    $report.passed = $true
}
catch {
    $report.error = $_.Exception.Message
}
finally {
    Stop-QualityHost $paletteProcess
    Stop-QualityHost $paletteMenuProcess
    Stop-QualityHost $superPanelProcess
    Stop-QualityHost $superPanelTransitionProcess
    Stop-QualityHost $managementProcess
    Stop-QualityHost $workflowPaletteProcess
    Stop-QualityHost $workflowPanelProcess
    Stop-QualityHost $workflowSchemaWideProcess
    Stop-QualityHost $workflowSchemaCompactProcess
    Stop-QualityHost $workflowOutputProcess
    Stop-QualityHost $pluginProcess
    Stop-QualityHost $marketProcess
    Stop-QualityHost $accessibilityProcess
    if ($WorkflowExportMatrix -and $null -ne $exportAclIdentity) {
        & icacls.exe $exportDeniedRoot /remove:d $exportAclIdentity /Q | Out-Null
    }
    if (Test-Path -LiteralPath $exportReparseRoot) {
        Remove-Item -LiteralPath $exportReparseRoot -Force
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $outputRoot 'desktop-ui-smoke.json') -Encoding UTF8
}

if (-not $report.passed) {
    throw "Desktop UI smoke failed: $($report.error)"
}
Write-Output 'Desktop UI smoke passed.'
Write-Output "Report: $(Join-Path $outputRoot 'desktop-ui-smoke.json')"
