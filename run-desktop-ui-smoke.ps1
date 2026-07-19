#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [ValidateRange(5,60)] [int] $TimeoutSeconds = 25,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Desktop UI smoke output directory already exists: $outputRoot"
}
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) { throw 'dotnet CLI was not found.' }
    $dotnet = $dotnetCommand.Source
}
$project = Join-Path $repoRoot 'src\LongBetterWindows.Host\LongBetterWindows.Host.csproj'
$executable = Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows\LongBetterWindows.Host.exe'
$pluginsDirectory = Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows\Plugins'
if (-not $NoBuild) {
    & $dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Desktop UI smoke Release build failed.' }
}
if (-not (Test-Path -LiteralPath $executable)) { throw "Host executable was not found: $executable" }

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
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    const uint KeyUp = 0x0002;
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
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
        bool attached = foregroundThread != 0 && foregroundThread != callerThread &&
            AttachThreadInput(callerThread, foregroundThread, true);
        try {
            BringWindowToTop(window);
            return SetForegroundWindow(window);
        }
        finally {
            if (attached) AttachThreadInput(callerThread, foregroundThread, false);
        }
    }
    public static void ShiftEnter(IntPtr window) {
        SetForegroundWindow(window);
        keybd_event(0x10, 0, 0, UIntPtr.Zero);
        keybd_event(0x0D, 0, 0, UIntPtr.Zero);
        keybd_event(0x0D, 0, KeyUp, UIntPtr.Zero);
        keybd_event(0x10, 0, KeyUp, UIntPtr.Zero);
    }
    public static void Escape(IntPtr window) {
        SetForegroundWindow(window);
        keybd_event(0x1B, 0, 0, UIntPtr.Zero);
        keybd_event(0x1B, 0, KeyUp, UIntPtr.Zero);
    }
    public static void TypeSearchText(IntPtr window) {
        SetForegroundWindow(window);
        foreach (byte key in new byte[] { 0x57, 0x49, 0x46, 0x49 }) {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KeyUp, UIntPtr.Zero);
        }
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
    do {
        $value = & $Probe
        if ($value -is [bool]) {
            if ($value) { return $true }
        }
        elseif ($null -ne $value) {
            return $value
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
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

function Find-DescendantByAutomationId(
    [Windows.Automation.AutomationElement] $root,
    [string] $automationId) {
    $elements = $root.FindAll(
        [Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.Condition]::TrueCondition)
    for ($index = 0; $index -lt $elements.Count; $index++) {
        $element = $elements.Item($index)
        if ($element.Current.AutomationId -eq $automationId) { return $element }
    }
    return $null
}

function Find-DescendantByName(
    [Windows.Automation.AutomationElement] $root,
    [string] $name) {
    $elements = $root.FindAll(
        [Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.Condition]::TrueCondition)
    for ($index = 0; $index -lt $elements.Count; $index++) {
        $element = $elements.Item($index)
        if ($element.Current.Name -eq $name) { return $element }
    }
    return $null
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
        '--plugins-dir', $pluginsDirectory
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
    plugin_lifecycle = [ordered]@{}
    marketplace = [ordered]@{}
    accessibility_modes = @()
    passed = $false
    error = $null
}
$paletteProcess = $null
$paletteMenuProcess = $null
$superPanelProcess = $null
$superPanelTransitionProcess = $null
$pluginProcess = $null
$marketProcess = $null
$accessibilityProcess = $null

try {
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

    Write-Stage 'Executing the first secondary result action with Shift+Enter.'
    [LongDesktopInput]::ShiftEnter([IntPtr]$palette.Current.NativeWindowHandle)
    $clipboardConfirmed = Wait-Until {
        (Get-Clipboard -Raw).Trim() -eq 'ms-settings:network-wifi'
    } 'Shift+Enter did not execute the Wi-Fi secondary copy action.'
    Start-Sleep -Milliseconds 600
    $paletteStillVisible = $null -ne (Wait-Until {
        Find-WindowByAutomationId $paletteProcess.Id 'Long.CommandPalette'
    } 'Shift+Enter closed the Command Palette after a keep-open copy action.')
    Write-Stage 'Closing keyboard-action Command Palette with Escape.'
    [LongDesktopInput]::Escape([IntPtr]$palette.Current.NativeWindowHandle)
    Wait-Until {
        $null -eq (Find-WindowByAutomationId $paletteProcess.Id 'Long.CommandPalette')
    } 'Escape did not hide the keyboard-action Command Palette.' | Out-Null
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
    Write-Stage 'Closing menu-workflow Command Palette with Escape.'
    [LongDesktopInput]::Escape([IntPtr]$menuPalette.Current.NativeWindowHandle)
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
    $superPanel.SetFocus()
    Write-Stage 'Closing Super Panel with Escape.'
    [LongDesktopInput]::Escape([IntPtr]$superPanel.Current.NativeWindowHandle)
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
    [LongDesktopInput]::Escape([IntPtr]$transitionPalette.Current.NativeWindowHandle)
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

    Write-Stage 'Starting embedded Base64 plugin workflow.'
    $pluginProcess = Start-QualityHost @(
        '--run-command', 'com.long.base64:base64.encode',
        '--command-text', 'Long-UI-smoke')
    $mainWindow = Wait-Until {
        Find-WindowByAutomationId $pluginProcess.Id 'Long.MainWindow'
    } 'The main window did not appear for the embedded plugin workflow.'
    $embeddedSurface = Wait-Until {
        Find-DescendantByAutomationId $mainWindow 'Long.Plugin.EmbeddedTitle'
    } 'The Base64 embedded plugin title did not appear.'
    $detach = Wait-Until {
        Find-DescendantByAutomationId $mainWindow 'Long.Plugin.Detach'
    } 'The embedded plugin detach button was not discoverable.'
    Write-Stage 'Detaching the embedded plugin through UI Automation.'
    Invoke-AutomationElement $detach `
        'The embedded plugin detach button did not support InvokePattern.'
    $detachedWindow = Wait-Until {
        Find-WindowByAutomationId $pluginProcess.Id 'Long.Plugin.DetachedWindow'
    } 'The detached plugin window did not appear.'
    $detachedBack = Wait-Until {
        Find-DescendantByAutomationId $detachedWindow 'Long.Plugin.DetachedBack'
    } 'The detached plugin Back button was not discoverable.'
    Write-Stage 'Returning from the detached plugin with Escape.'
    [LongDesktopInput]::Activate([IntPtr]$detachedWindow.Current.NativeWindowHandle) | Out-Null
    [LongDesktopInput]::Escape([IntPtr]$detachedWindow.Current.NativeWindowHandle)
    Wait-Until {
        $null -eq (Find-WindowByAutomationId `
            $pluginProcess.Id 'Long.Plugin.DetachedWindow')
    } 'Escape did not close the detached plugin window.' | Out-Null
    $toolCenter = Wait-Until {
        Find-DescendantByAutomationId $mainWindow 'ToolCenter'
    } 'Returning from the detached plugin did not restore Tool Center.'
    $report.plugin_lifecycle = [ordered]@{
        main_window_discovered = $true
        embedded_surface_discovered = $null -ne $embeddedSurface
        detach_invoked = $true
        detached_window_discovered = $true
        detached_back_discovered = $null -ne $detachedBack
        escape_closed_detached_window = $true
        tool_center_restored = $null -ne $toolCenter
    }
    Stop-QualityHost $pluginProcess
    $pluginProcess = $null

    Write-Stage 'Starting Marketplace search and uninstall-preview workflow.'
    $marketProcess = Start-QualityHost '--quality-open-market'
    $marketMain = Wait-Until {
        Find-WindowByAutomationId $marketProcess.Id 'Long.MainWindow'
    } 'The main window did not appear for the Marketplace workflow.'
    $marketSearch = Wait-Until {
        Find-ProcessElementByAutomationId $marketProcess.Id 'Long.Marketplace.Search'
    } 'The Marketplace search box was not discoverable.'
    $marketResults = Wait-Until {
        Find-ProcessElementByAutomationId $marketProcess.Id 'Long.Marketplace.Results'
    } 'The Marketplace result list was not discoverable.'
    $marketCount = Wait-Until {
        $element = Find-ProcessElementByAutomationId `
            $marketProcess.Id 'Long.Marketplace.ResultCount'
        if ($null -ne $element -and $element.Current.Name -notmatch ' 0 ') { $element }
    } 'The trusted Marketplace catalog did not populate.'
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
    $uninstall = Wait-Until {
        Find-ProcessElementByAutomationId $marketProcess.Id 'Long.Marketplace.Uninstall'
    } 'The installed plugin uninstall-preview button was not discoverable.'
    Write-Stage 'Opening and cancelling the Marketplace uninstall confirmation.'
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
    Invoke-AutomationElement $confirmCancel `
        'The Marketplace confirmation cancel button did not support InvokePattern.'
    Wait-Until {
        $null -eq (Find-ProcessElementByAutomationId `
            $marketProcess.Id 'Long.Marketplace.ConfirmTitle')
    } 'Cancelling the Marketplace confirmation did not close the overlay.' | Out-Null
    $uninstallStillAvailable = Wait-Until {
        Find-ProcessElementByAutomationId $marketProcess.Id 'Long.Marketplace.Uninstall'
    } 'Cancelling uninstall changed the installed plugin state.'
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
        [LongDesktopInput]::Escape([IntPtr]$modePalette.Current.NativeWindowHandle)
        Wait-Until {
            $null -eq (Find-WindowByAutomationId `
                $accessibilityProcess.Id 'Long.CommandPalette')
        } "Escape did not close accessibility mode $($mode.name)." | Out-Null
        $report.accessibility_modes += [ordered]@{
            name = $mode.name
            palette_discovered = $true
            search_keyboard_focus = [bool]$modeFocus
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
    Stop-QualityHost $pluginProcess
    Stop-QualityHost $marketProcess
    Stop-QualityHost $accessibilityProcess
    $report | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $outputRoot 'desktop-ui-smoke.json') -Encoding UTF8
}

if (-not $report.passed) {
    throw "Desktop UI smoke failed: $($report.error)"
}
Write-Output 'Desktop UI smoke passed.'
Write-Output "Report: $(Join-Path $outputRoot 'desktop-ui-smoke.json')"
