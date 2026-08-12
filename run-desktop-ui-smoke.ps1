#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [ValidateRange(5,60)] [int] $TimeoutSeconds = 25,
    [string] $ReleaseDirectory,
    [switch] $NoBuild,
    [switch] $WorkflowOnly,
    [switch] $WorkflowOutputOnly,
    [switch] $WorkflowSchemaOnly,
    [switch] $WorkflowExportMatrix,
    [switch] $PluginCommandManagementOnly,
    [switch] $SettingsNavigationOnly,
    [ValidateRange(640,1920)] [int] $SettingsNavigationWidth = 1120,
    [ValidateSet('auto','sidebar','compact')]
    [string] $SettingsNavigationExpectedMode = 'auto'
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
$qualityStoragePath = Join-Path $outputRoot 'storage.json'
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
$focusProbeScript = Join-Path $outputRoot 'focus-probe.ps1'
$focusProbeSource = @'
param([Parameter(Mandatory=$true)] [string] $HandlePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$form = [Windows.Forms.Form]::new()
$form.Text = 'Long Focus Probe'
$form.Name = 'LongFocusProbe'
$form.Width = 420
$form.Height = 240
$form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
$form.Location = [Drawing.Point]::new(80, 80)
$form.Add_Shown({
    [IO.File]::WriteAllText(
        $HandlePath,
        $form.Handle.ToInt64().ToString([Globalization.CultureInfo]::InvariantCulture),
        [Text.UTF8Encoding]::new($false))
    $form.Activate()
})
[Windows.Forms.Application]::Run($form)
'@
[IO.File]::WriteAllText(
    $focusProbeScript,
    $focusProbeSource,
    [Text.UTF8Encoding]::new($false))

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
$uiAutomationAssemblies = @(
    [Windows.Automation.AutomationElement].Assembly.Location,
    [Windows.Automation.AutomationPattern].Assembly.Location
) | Sort-Object -Unique
Add-Type -ReferencedAssemblies $uiAutomationAssemblies -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Automation;
public sealed class LongAutomationEventSnapshot {
    public string Kind { get; set; }
    public string AutomationId { get; set; }
    public string Name { get; set; }
    public string ControlType { get; set; }
    public int ProcessId { get; set; }
    public string CapturedAt { get; set; }
}
public static class LongDesktopInput {
    static readonly object AutomationEventGate = new object();
    static readonly List<LongAutomationEventSnapshot> AutomationEvents =
        new List<LongAutomationEventSnapshot>();
    static AutomationEventHandler liveRegionHandler;
    static AutomationFocusChangedEventHandler focusChangedHandler;
    static int automationEventProcessId;
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
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT {
        public uint type;
        public INPUTUNION value;
    }
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    struct INPUTUNION {
        [FieldOffset(0)] public KEYBDINPUT keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT {
        public ushort virtualKey;
        public ushort scanCode;
        public uint flags;
        public uint time;
        public UIntPtr extra;
    }
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);
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
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    static extern IntPtr GetWindowLongPtr32(IntPtr window, int index);
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
    public static IntPtr OwnerWindow(IntPtr window) { return GetWindow(window, 4); }
    public static bool HasTaskbarAppStyle(IntPtr window) {
        IntPtr value = IntPtr.Size == 8
            ? GetWindowLongPtr64(window, -20)
            : GetWindowLongPtr32(window, -20);
        return (value.ToInt64() & 0x00040000L) != 0;
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
    public static IntPtr ForegroundWindow() { return GetForegroundWindow(); }
    public static bool KeyPress(ushort virtualKey) {
        var inputs = new[] {
            new INPUT {
                type = 1,
                value = new INPUTUNION {
                    keyboard = new KEYBDINPUT { virtualKey = virtualKey }
                }
            },
            new INPUT {
                type = 1,
                value = new INPUTUNION {
                    keyboard = new KEYBDINPUT {
                        virtualKey = virtualKey,
                        flags = 0x0002
                    }
                }
            }
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
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
    public static void StartAutomationEventCapture(int processId) {
        StopAutomationEventCapture();
        lock (AutomationEventGate) {
            AutomationEvents.Clear();
            automationEventProcessId = processId;
        }
        liveRegionHandler = CaptureAutomationEvent;
        focusChangedHandler = CaptureFocusChanged;
        Automation.AddAutomationEventHandler(
            AutomationElementIdentifiers.LiveRegionChangedEvent,
            AutomationElement.RootElement,
            TreeScope.Descendants,
            liveRegionHandler);
        Automation.AddAutomationFocusChangedEventHandler(focusChangedHandler);
    }
    public static LongAutomationEventSnapshot[] GetAutomationEventCapture() {
        lock (AutomationEventGate) {
            return AutomationEvents.ToArray();
        }
    }
    public static LongAutomationEventSnapshot[] StopAutomationEventCapture() {
        if (liveRegionHandler != null) {
            Automation.RemoveAutomationEventHandler(
                AutomationElementIdentifiers.LiveRegionChangedEvent,
                AutomationElement.RootElement,
                liveRegionHandler);
            liveRegionHandler = null;
        }
        if (focusChangedHandler != null) {
            Automation.RemoveAutomationFocusChangedEventHandler(
                focusChangedHandler);
            focusChangedHandler = null;
        }
        lock (AutomationEventGate) {
            automationEventProcessId = 0;
            return AutomationEvents.ToArray();
        }
    }
    static void CaptureAutomationEvent(object sender, AutomationEventArgs args) {
        CaptureEvent("live_region_changed", sender as AutomationElement);
    }
    static void CaptureFocusChanged(
        object sender,
        AutomationFocusChangedEventArgs args) {
        CaptureEvent("focus_changed", sender as AutomationElement);
    }
    static void CaptureEvent(string kind, AutomationElement element) {
        if (element == null) return;
        try {
            var current = element.Current;
            lock (AutomationEventGate) {
                if (automationEventProcessId == 0 ||
                    current.ProcessId != automationEventProcessId ||
                    AutomationEvents.Count >= 1024) return;
                AutomationEvents.Add(new LongAutomationEventSnapshot {
                    Kind = kind,
                    AutomationId = current.AutomationId ?? string.Empty,
                    Name = current.Name ?? string.Empty,
                    ControlType = current.ControlType == null
                        ? string.Empty
                        : current.ControlType.ProgrammaticName,
                    ProcessId = current.ProcessId,
                    CapturedAt = DateTimeOffset.UtcNow.ToString("O")
                });
            }
        }
        catch (ElementNotAvailableException) { }
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

function Get-SelectedAutomationItem(
    [Windows.Automation.AutomationElement] $list) {
    $pattern = $null
    if (-not $list.TryGetCurrentPattern(
        [Windows.Automation.SelectionPattern]::Pattern,
        [ref]$pattern)) {
        return $null
    }
    $selection = ([Windows.Automation.SelectionPattern]$pattern).Current.GetSelection()
    if ($selection.Count -eq 0) { return $null }
    return $selection[0]
}

function Get-AutomationIdentity(
    [Windows.Automation.AutomationElement] $element) {
    if ($null -eq $element) { return '' }
    return '{0}|{1}|{2}' -f `
        $element.Current.AutomationId,
        $element.Current.Name,
        (($element.GetRuntimeId() | ForEach-Object { $_.ToString() }) -join '.')
}

function Wait-LauncherLatency(
    [Windows.Automation.AutomationElement] $window,
    [bool] $requireQueryResult,
    [string] $failureMessage) {
    return Wait-Until {
        $status = [string]$window.Current.ItemStatus
        if ($status -notmatch 'first_frame_ms=([0-9.]+)') { return $null }
        if ($status -notmatch 'first_results_ms=([0-9.]+)') { return $null }
        if ($requireQueryResult -and
            $status -notmatch 'query_first_results_ms=([0-9.]+)') {
            return $null
        }
        return $status
    } $failureMessage
}

function Assert-LauncherLatencyBudget(
    [string] $status,
    [bool] $checkQuery,
    [string] $surface) {
    $frame = [regex]::Match($status, 'first_frame_ms=([0-9.]+)')
    $results = [regex]::Match($status, 'first_results_ms=([0-9.]+)')
    $query = [regex]::Match($status, 'query_first_results_ms=([0-9.]+)')
    $frameMs = [double]::Parse(
        $frame.Groups[1].Value,
        [Globalization.CultureInfo]::InvariantCulture)
    if ($frameMs -gt 250) {
        throw "$surface exceeded the 250ms first-frame budget: $status"
    }
    if ($checkQuery) {
        $queryMs = [double]::Parse(
            $query.Groups[1].Value,
            [Globalization.CultureInfo]::InvariantCulture)
        if ($queryMs -gt 100) {
            throw "$surface exceeded the 100ms query-result budget: $status"
        }
    }
}

function Find-VisibleDescendantByAutomationId(
    [Windows.Automation.AutomationElement] $root,
    [string] $automationId) {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty,
        $automationId)
    $matches = $root.FindAll(
        [Windows.Automation.TreeScope]::Descendants,
        $condition)
    foreach ($match in $matches) {
        $bounds = $match.Current.BoundingRectangle
        if ($match.Current.IsEnabled -and
            -not $match.Current.IsOffscreen -and
            -not $bounds.IsEmpty -and
            $bounds.Width -gt 0 -and
            $bounds.Height -gt 0) {
            return $match
        }
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

function Find-ProcessSelectionItemByAutomationId(
    [int] $processId,
    [string] $automationId) {
    $condition = [Windows.Automation.AndCondition]::new(@(
        [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::ProcessIdProperty,
            $processId),
        [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::AutomationIdProperty,
            $automationId)))
    $matches = [Windows.Automation.AutomationElement]::RootElement.FindAll(
        [Windows.Automation.TreeScope]::Descendants,
        $condition)
    foreach ($match in $matches) {
        $pattern = $null
        if ($match.Current.IsEnabled -and
            -not $match.Current.IsOffscreen -and
            $match.TryGetCurrentPattern(
                [Windows.Automation.SelectionItemPattern]::Pattern,
                [ref]$pattern)) {
            return $match
        }
    }
    return $null
}

function Set-AutomationFocus(
    [scriptblock] $ResolveElement,
    [string] $failureMessage) {
    return Wait-Until {
        $element = & $ResolveElement
        if ($null -eq $element -or
            -not $element.Current.IsEnabled -or
            -not $element.Current.IsKeyboardFocusable -or
            $element.Current.IsOffscreen) {
            return $null
        }
        $element.SetFocus()
        if ($element.Current.HasKeyboardFocus) { return $element }
        return $null
    } $failureMessage
}

function Wait-CommandFeedback(
    [Windows.Automation.AutomationElement] $root,
    [string] $previousRevision,
    [string] $expectedName,
    [string] $failureMessage) {
    return Wait-Until {
        $feedback = Find-DescendantByAutomationId `
            $root 'Long.Workspace.PluginSettings.CommandFeedback'
        if ($null -eq $feedback) { return $null }
        $revision = [string]$feedback.Current.ItemStatus
        if ($revision -ne $previousRevision -and
            [string]$feedback.Current.Name -eq $expectedName) {
            return $feedback
        }
        return $null
    } $failureMessage
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
                $supportedPatterns = $element.GetSupportedPatterns() |
                    ForEach-Object { $_.ProgrammaticName }
                throw ("SelectionItemPattern unavailable: " +
                    "type='$($element.Current.ControlType.ProgrammaticName)', " +
                    "class='$($element.Current.ClassName)', " +
                    "framework='$($element.Current.FrameworkId)', " +
                    "patterns='$($supportedPatterns -join ',')'.")
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
        help_text = [string]$element.Current.HelpText
        item_status = [string]$element.Current.ItemStatus
        position_in_set = [int]$element.Current.PositionInSet
        size_of_set = [int]$element.Current.SizeOfSet
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
        '--quality-storage-path', $qualityStoragePath,
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

function Start-FocusProbe {
    $handlePath = Join-Path $outputRoot 'focus-probe.handle'
    Remove-Item -LiteralPath $handlePath -Force -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $focusProbeScript,
        '-HandlePath', $handlePath) -WindowStyle Hidden -PassThru
    Wait-Until {
        if (-not (Test-Path -LiteralPath $handlePath -PathType Leaf)) { return $null }
        $value = [IO.File]::ReadAllText($handlePath).Trim()
        $parsed = 0L
        if ([long]::TryParse($value, [ref]$parsed) -and $parsed -gt 0) {
            [IntPtr]$parsed
        }
    } 'The independent foreground focus probe did not publish its window handle.' | Out-Null
    $handle = [IntPtr][long]([IO.File]::ReadAllText($handlePath).Trim())
    return [pscustomobject]@{ Process = $process; Handle = $handle }
}

function Wait-FocusProbe([IntPtr] $handle, [string] $failureMessage) {
    Wait-Until {
        if ([LongDesktopInput]::ForegroundWindow() -eq $handle) { $true }
    } $failureMessage | Out-Null
}

$report = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'desktop_ui_automation_smoke'
    release_executable = $executable
    palette = [ordered]@{}
    super_panel = [ordered]@{}
    launcher_workspace_continuity = [ordered]@{}
    command_palette_workspace_continuity = [ordered]@{}
    launcher_module_close_continuity = [ordered]@{}
    focus_restore = [ordered]@{}
    management_navigation = [ordered]@{}
    workflow_review = [ordered]@{}
    workflow_argument_schema = [ordered]@{}
    workflow_output = [ordered]@{}
    workflow_export = [ordered]@{}
    plugin_lifecycle = [ordered]@{}
    marketplace = [ordered]@{}
    automation_semantics = [ordered]@{}
    assistive_technology_events = [ordered]@{}
    accessibility_modes = @()
    passed = $false
    error = $null
}
$paletteProcess = $null
$paletteMenuProcess = $null
$superPanelProcess = $null
$contextMatrixProcess = $null
$focusProbeProcess = $null
$focusEscapeProcess = $null
$focusExpandProcess = $null
$focusExecuteProcess = $null
$superPanelTransitionProcess = $null
$launcherWorkspaceProcess = $null
$paletteWorkspaceProcess = $null
$panelModuleCloseProcess = $null
$paletteModuleCloseProcess = $null
$managementProcess = $null
$pluginSettingsProcess = $null
$workflowPaletteProcess = $null
$workflowPanelProcess = $null
$workflowSchemaWideProcess = $null
$workflowSchemaCompactProcess = $null
$workflowOutputProcess = $null
$pluginProcess = $null
$marketProcess = $null
$accessibilityProcess = $null
$automationEventCaptureActive = $false

try {
    if ($SettingsNavigationOnly -or
        (-not $PluginCommandManagementOnly -and
         -not $WorkflowOnly -and
         -not $WorkflowOutputOnly -and
         -not $WorkflowSchemaOnly)) {
    if (-not $SettingsNavigationOnly) {
    Write-Stage 'Starting Command Palette host.'
    Set-Clipboard -Value 'long-ui-smoke-pending'
    $paletteProcess = Start-QualityHost '--quality-open-palette'
    [LongDesktopInput]::StartAutomationEventCapture($paletteProcess.Id)
    $automationEventCaptureActive = $true
    $palette = Wait-Until {
        Find-WindowByAutomationId $paletteProcess.Id 'Long.CommandPalette'
    } 'Command Palette did not appear through Windows UI Automation.'
    $search = Wait-Until {
        Find-DescendantByAutomationId $palette 'Long.CommandPalette.Search'
    } 'Command Palette search box was not discoverable.'
    $results = Wait-Until {
        Find-DescendantByAutomationId $palette 'Long.CommandPalette.Results'
    } 'Command Palette result list was not discoverable.'
    $paletteLatency = Wait-LauncherLatency $palette $false `
        'Command Palette did not publish first-frame and first-result latency.'
    Assert-LauncherLatencyBudget $paletteLatency $false 'Command Palette'
    $report.automation_semantics['palette'] = [ordered]@{
        window = Get-AutomationSemantics $palette 'ControlType.Window' 'Command Palette window semantics failed.'
        search = Get-AutomationSemantics $search 'ControlType.Edit' 'Command Palette search semantics failed.'
        results = Get-AutomationSemantics $results 'ControlType.List' 'Command Palette results semantics failed.'
    }

    Write-Stage 'Setting wifi through the standard UI Automation value pattern.'
    $paletteHandle = [IntPtr]$palette.Current.NativeWindowHandle
    Wait-Until {
        [LongDesktopInput]::Activate($paletteHandle) -and
            [LongDesktopInput]::ForegroundWindow() -eq $paletteHandle
    } 'Command Palette could not become the foreground window.' | Out-Null
    $search.SetFocus()
    Wait-Until {
        if ($search.Current.HasKeyboardFocus) { $true }
    } 'Command Palette search did not receive focus before keyboard navigation.' | Out-Null
    $valuePattern = [Windows.Automation.ValuePattern]$search.GetCurrentPattern(
        [Windows.Automation.ValuePattern]::Pattern)
    $paletteSelectionBefore = Wait-Until {
        Get-SelectedAutomationItem $results
    } 'Command Palette did not expose its initial selected result.'
    $paletteSelectionBeforeId = Get-AutomationIdentity $paletteSelectionBefore
    $paletteSelectionBefore.SetFocus()
    Wait-Until {
        $paletteSelectionBefore.Current.HasKeyboardFocus
    } 'Command Palette result did not receive focus for the accessibility focus-return probe.' | Out-Null
    $search.SetFocus()
    Wait-Until {
        [LongDesktopInput]::Activate($paletteHandle) -and
            [LongDesktopInput]::ForegroundWindow() -eq $paletteHandle -and
            $search.Current.HasKeyboardFocus
    } 'Command Palette search did not regain focus for keyboard navigation.' | Out-Null
    if (-not [LongDesktopInput]::KeyPress(0x28)) {
        throw 'SendInput could not deliver Down Arrow to Command Palette.'
    }
    $paletteSelectionAfter = Wait-Until {
        $selected = Get-SelectedAutomationItem $results
        if ($null -ne $selected -and
            (Get-AutomationIdentity $selected) -ne $paletteSelectionBeforeId) {
            $selected
        }
    } 'Down Arrow did not move the Command Palette selection.'
    $paletteAnnouncement = Wait-Until {
        $announcement = Find-DescendantByAutomationId `
            $palette 'Long.CommandPalette.SelectionAnnouncement'
        if ($null -eq $announcement) {
            $announcement = Find-RawDescendantByAutomationId `
                $palette 'Long.CommandPalette.SelectionAnnouncement'
        }
        if ($null -ne $announcement -and
            -not [string]::IsNullOrWhiteSpace($announcement.Current.Name)) {
            $announcement.Current.Name
        }
    } 'Command Palette did not publish its keyboard selection announcement.'
    if (-not $search.Current.HasKeyboardFocus) {
        throw 'Down Arrow moved focus away from Command Palette search.'
    }
    $valuePattern.SetValue('wifi')
    Start-Sleep -Milliseconds 500
    Write-Stage "Search value after UI Automation input: '$($valuePattern.Current.Value)'."
    $wifi = Wait-Until {
        Find-DescendantByName $results 'Wi-Fi'
    } 'The Wi-Fi Windows setting result did not appear.'
    $paletteQueryLatency = Wait-LauncherLatency $palette $true `
        'Command Palette did not publish query-to-first-result latency.'
    Assert-LauncherLatencyBudget $paletteQueryLatency $true 'Command Palette'
    $focusConfirmed = Wait-Until { $search.Current.HasKeyboardFocus } `
        'The Command Palette search box did not receive keyboard focus.'
    Wait-Until {
        $events = @([LongDesktopInput]::GetAutomationEventCapture())
        $report.assistive_technology_events = [ordered]@{
            passed = $false
            transport = 'windows_ui_automation_events'
            source_process_id = $paletteProcess.Id
            observed_events = $events
        }
        $focusEvent = @($events | Where-Object {
            $_.ProcessId -eq $paletteProcess.Id -and
            $_.Kind -eq 'focus_changed' -and
            $_.AutomationId -eq 'Long.CommandPalette.Search'
        })
        $liveEvent = @($events | Where-Object {
            $_.ProcessId -eq $paletteProcess.Id -and
            $_.Kind -eq 'live_region_changed' -and
            $_.AutomationId -eq 'Long.CommandPalette.SelectionAnnouncement' -and
            $_.Name -eq [string]$paletteAnnouncement
        })
        $focusEvent.Count -gt 0 -and $liveEvent.Count -gt 0
    } 'Command Palette did not emit the expected UIA focus and live-region events.' | Out-Null
    $paletteAutomationEvents = @(
        [LongDesktopInput]::StopAutomationEventCapture())
    $automationEventCaptureActive = $false
    $paletteFocusEvents = @($paletteAutomationEvents | Where-Object {
        $_.ProcessId -eq $paletteProcess.Id -and
        $_.Kind -eq 'focus_changed' -and
        $_.AutomationId -eq 'Long.CommandPalette.Search'
    })
    $paletteLiveRegionEvents = @($paletteAutomationEvents | Where-Object {
        $_.ProcessId -eq $paletteProcess.Id -and
        $_.Kind -eq 'live_region_changed' -and
        $_.AutomationId -eq 'Long.CommandPalette.SelectionAnnouncement'
    })
    $report.assistive_technology_events = [ordered]@{
        passed = $true
        transport = 'windows_ui_automation_events'
        source_process_id = $paletteProcess.Id
        physical_keyboard_validated = $true
        focus_event_count = $paletteFocusEvents.Count
        live_region_event_count = $paletteLiveRegionEvents.Count
        expected_announcement = [string]$paletteAnnouncement
        focus_events = $paletteFocusEvents
        live_region_events = $paletteLiveRegionEvents
    }

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
    [LongDesktopInput]::Activate(
        [IntPtr]$menuPalette.Current.NativeWindowHandle) | Out-Null
    $moreActions = Wait-Until {
        $button = Find-VisibleDescendantByAutomationId `
            $menuResults 'Long.Result.MoreActions'
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
        physical_keyboard_validated = $true
        keyboard_selection_changed = $true
        keyboard_focus_preserved = $true
        selection_announcement = [string]$paletteAnnouncement
        invocation_latency = [string]$paletteLatency
        query_latency = [string]$paletteQueryLatency
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
    $superPanelLatency = Wait-LauncherLatency $superPanel $false `
        'Super Panel did not publish first-frame and first-result latency.'
    Assert-LauncherLatencyBudget $superPanelLatency $false 'Super Panel'
    $contextListMode = Wait-Until {
        if ($panelResults.Current.ItemStatus -like 'mode:context-list;page:1/*') {
            $panelResults.Current.ItemStatus
        }
    } 'Super Panel did not expose the contextual list presentation.'
    $superPanelHandle = [IntPtr]$superPanel.Current.NativeWindowHandle
    Wait-Until {
        [LongDesktopInput]::Activate($superPanelHandle) -and
            [LongDesktopInput]::ForegroundWindow() -eq $superPanelHandle
    } 'Super Panel could not become the foreground window.' | Out-Null
    $panelResults.SetFocus()
    Wait-Until {
        if ($panelResults.Current.HasKeyboardFocus) { $true }
    } 'Super Panel results did not receive focus before keyboard navigation.' | Out-Null
    $panelSelectionBefore = Wait-Until {
        Get-SelectedAutomationItem $panelResults
    } 'Super Panel did not expose its initial selected result.'
    $panelSelectionBeforeId = Get-AutomationIdentity $panelSelectionBefore
    if (-not [LongDesktopInput]::KeyPress(0x28)) {
        throw 'SendInput could not deliver Down Arrow to Super Panel.'
    }
    $panelSelectionAfter = Wait-Until {
        $selected = Get-SelectedAutomationItem $panelResults
        if ($null -ne $selected -and
            (Get-AutomationIdentity $selected) -ne $panelSelectionBeforeId) {
            $selected
        }
    } 'Down Arrow did not move the Super Panel selection.'
    $panelAnnouncement = Wait-Until {
        $announcement = Find-DescendantByAutomationId `
            $superPanel 'Long.SuperPanel.SelectionAnnouncement'
        if ($null -eq $announcement) {
            $announcement = Find-RawDescendantByAutomationId `
                $superPanel 'Long.SuperPanel.SelectionAnnouncement'
        }
        if ($null -ne $announcement -and
            -not [string]::IsNullOrWhiteSpace($announcement.Current.Name)) {
            $announcement.Current.Name
        }
    } 'Super Panel did not publish its keyboard selection announcement.'
    $nextPanelPage = Wait-Until {
        Find-DescendantByAutomationId $superPanel 'Long.SuperPanel.NextPage'
    } 'Super Panel next-page action was not discoverable.'
    Invoke-AutomationElement $nextPanelPage `
        'The Super Panel next-page action did not support InvokePattern.'
    $contextSecondPage = Wait-Until {
        if ($panelResults.Current.ItemStatus -like 'mode:context-list;page:2/*') {
            $panelResults.Current.ItemStatus
        }
    } 'Super Panel did not move to the second contextual page.'
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
        context_list_mode = [string]$contextListMode
        context_second_page = [string]$contextSecondPage
        escape_closed_panel = $true
        keyboard_selection_changed = $true
        selection_announcement = [string]$panelAnnouncement
        invocation_latency = [string]$superPanelLatency
        physical_keyboard_validated = $true
        context_matrix = @(
            [ordered]@{
                profile = 'url'
                expected_mode = 'context-list'
                expected_inputs = 'url,clipboard,text'
                item_status = [string]$contextListMode
            }
        )
    }

    Stop-QualityHost $superPanelProcess
    $superPanelProcess = $null

    $contextProfiles = @(
        [ordered]@{ profile = 'text'; mode = 'context-list'; items = 1; inputs = 'clipboard,text' },
        [ordered]@{ profile = 'image'; mode = 'context-list'; items = 1; inputs = 'image' },
        [ordered]@{ profile = 'file'; mode = 'context-list'; items = 1; inputs = 'file,explorerselection' },
        [ordered]@{ profile = 'files'; mode = 'context-list'; items = 1; inputs = 'files,explorerselection' },
        [ordered]@{ profile = 'empty'; mode = 'compact-grid'; items = 0; inputs = 'none' }
    )
    foreach ($profile in $contextProfiles) {
        Write-Stage "Validating Super Panel context profile: $($profile.profile)."
        try {
            $contextMatrixProcess = Start-QualityHost @(
                '--quality-open-super-panel',
                '--quality-context', [string]$profile.profile)
            $contextPanel = Wait-Until {
                Find-WindowByAutomationId $contextMatrixProcess.Id 'Long.SuperPanel'
            } "Super Panel did not appear for context profile $($profile.profile)."
            $contextResults = Wait-Until {
                Find-DescendantByAutomationId $contextPanel 'Long.SuperPanel.Results'
            } "Super Panel results were unavailable for context profile $($profile.profile)."
            $expectedSuffix = "context-items:$($profile.items);inputs:$($profile.inputs)"
            $contextStatus = Wait-Until {
                $status = [string]$contextResults.Current.ItemStatus
                if ($status -like "mode:$($profile.mode);page:1/*;$expectedSuffix") {
                    $status
                }
            } "Super Panel context profile $($profile.profile) did not expose $expectedSuffix."
            $report.super_panel.context_matrix += [ordered]@{
                profile = [string]$profile.profile
                expected_mode = [string]$profile.mode
                expected_inputs = [string]$profile.inputs
                item_status = [string]$contextStatus
            }
        }
        finally {
            Stop-QualityHost $contextMatrixProcess
            $contextMatrixProcess = $null
        }
    }

    Write-Stage 'Starting independent foreground focus probe.'
    $focusProbe = Start-FocusProbe
    $focusProbeProcess = $focusProbe.Process
    $focusProbeHandle = [IntPtr]$focusProbe.Handle

    [LongDesktopInput]::Activate($focusProbeHandle) | Out-Null
    Wait-FocusProbe $focusProbeHandle 'The focus probe could not become foreground before Escape validation.'
    $focusEscapeProcess = Start-QualityHost @(
        '--quality-open-super-panel',
        '--quality-context', 'empty',
        '--quality-origin-window', $focusProbeHandle.ToInt64().ToString())
    $focusEscapePanel = Wait-Until {
        Find-WindowByAutomationId $focusEscapeProcess.Id 'Long.SuperPanel'
    } 'Super Panel did not appear for Escape focus restoration.'
    if ([LongDesktopInput]::WindowAction(
        [IntPtr]$focusEscapePanel.Current.NativeWindowHandle, 3) -ne 1) {
        throw 'Super Panel rejected Escape focus restoration.'
    }
    Wait-Until {
        $null -eq (Find-WindowByAutomationId $focusEscapeProcess.Id 'Long.SuperPanel')
    } 'Super Panel did not hide during Escape focus restoration.' | Out-Null
    Wait-FocusProbe $focusProbeHandle 'Escape did not restore the independent foreground window.'
    Stop-QualityHost $focusEscapeProcess
    $focusEscapeProcess = $null

    [LongDesktopInput]::Activate($focusProbeHandle) | Out-Null
    Wait-FocusProbe $focusProbeHandle 'The focus probe could not become foreground before expansion validation.'
    $focusExpandProcess = Start-QualityHost @(
        '--quality-open-super-panel',
        '--quality-context', 'url',
        '--quality-origin-window', $focusProbeHandle.ToInt64().ToString())
    $focusExpandPanel = Wait-Until {
        Find-WindowByAutomationId $focusExpandProcess.Id 'Long.SuperPanel'
    } 'Super Panel did not appear for expansion focus restoration.'
    $focusOpenCommandCenter = Wait-Until {
        Find-DescendantByAutomationId $focusExpandPanel 'Long.SuperPanel.OpenCommandCenter'
    } 'Open Command Center was unavailable for expansion focus restoration.'
    Invoke-AutomationElement $focusOpenCommandCenter `
        'Open Command Center could not be invoked for focus restoration.'
    $focusPalette = Wait-Until {
        Find-WindowByAutomationId $focusExpandProcess.Id 'Long.CommandPalette'
    } 'Command Palette did not appear for expansion focus restoration.'
    if ([LongDesktopInput]::WindowAction(
        [IntPtr]$focusPalette.Current.NativeWindowHandle, 3) -ne 1) {
        throw 'Command Palette rejected expansion focus restoration.'
    }
    Wait-Until {
        $null -eq (Find-WindowByAutomationId $focusExpandProcess.Id 'Long.CommandPalette')
    } 'Command Palette did not hide during expansion focus restoration.' | Out-Null
    Wait-FocusProbe $focusProbeHandle 'Expanded Command Palette did not restore the original foreground window.'
    Stop-QualityHost $focusExpandProcess
    $focusExpandProcess = $null

    [LongDesktopInput]::Activate($focusProbeHandle) | Out-Null
    Wait-FocusProbe $focusProbeHandle 'The focus probe could not become foreground before command validation.'
    $focusExecuteProcess = Start-QualityHost @(
        '--quality-open-super-panel',
        '--quality-context', 'empty',
        '--quality-origin-window', $focusProbeHandle.ToInt64().ToString())
    $focusExecutePanel = Wait-Until {
        Find-WindowByAutomationId $focusExecuteProcess.Id 'Long.SuperPanel'
    } 'Super Panel did not appear for command focus restoration.'
    $focusExecuteHandle = [IntPtr]$focusExecutePanel.Current.NativeWindowHandle
    $focusCommandSelected = $false
    for ($pageAttempt = 0; $pageAttempt -lt 8; $pageAttempt++) {
        if ([LongDesktopInput]::WindowAction($focusExecuteHandle, 5) -eq 1) {
            $focusCommandSelected = $true
            break
        }
        $focusNextPage = Find-DescendantByAutomationId `
            $focusExecutePanel 'Long.SuperPanel.NextPage'
        if ($null -eq $focusNextPage -or -not $focusNextPage.Current.IsEnabled) {
            break
        }
        Invoke-AutomationElement $focusNextPage `
            'Super Panel could not page to the focus-sensitive window command.'
        Start-Sleep -Milliseconds 120
    }
    if (-not $focusCommandSelected) {
        throw 'The focus-sensitive window command was not available in Super Panel.'
    }
    if ([LongDesktopInput]::WindowAction($focusExecuteHandle, 1) -ne 1) {
        throw 'Super Panel rejected focus-sensitive command execution.'
    }
    Wait-Until {
        $null -eq (Find-WindowByAutomationId $focusExecuteProcess.Id 'Long.SuperPanel')
    } 'Super Panel did not hide before focus-sensitive command execution.' | Out-Null
    Wait-FocusProbe $focusProbeHandle 'Command execution did not restore the original foreground window.'
    Stop-QualityHost $focusExecuteProcess
    $focusExecuteProcess = $null

    $report.focus_restore = [ordered]@{
        probe_window = 'independent_winforms_process'
        escape_restored = $true
        expansion_dismiss_restored = $true
        command_execution_restored = $true
        command_key = 'com.long.window-manager:window.topmost'
    }
    Stop-QualityHost $focusProbeProcess
    $focusProbeProcess = $null

    Write-Stage 'Starting Super Panel to Command Palette transition host.'
    $superPanelTransitionProcess = Start-QualityHost @(
        '--quality-open-super-panel',
        '--language', 'en-US')
    $transitionPanel = Wait-Until {
        Find-WindowByAutomationId $superPanelTransitionProcess.Id 'Long.SuperPanel'
    } 'Super Panel did not appear for the command-center transition.'
    $groups = Wait-Until {
        Find-DescendantByAutomationId $transitionPanel 'Long.SuperPanel.Groups'
    } 'Super Panel groups were not discoverable.'
    $transitionPanelResults = Wait-Until {
        Find-DescendantByAutomationId $transitionPanel 'Long.SuperPanel.Results'
    } 'Super Panel results were not discoverable for the command-center transition.'
    $transitionPanelSelection = Wait-Until {
        $pattern = [Windows.Automation.SelectionPattern]`
            $transitionPanelResults.GetCurrentPattern(
                [Windows.Automation.SelectionPattern]::Pattern)
        $selection = $pattern.Current.GetSelection()
        if ($selection.Count -gt 0) { $selection[0] }
    } 'Super Panel did not expose a selected result before expansion.'
    $transitionSelectedId = [string]$transitionPanelSelection.Current.AutomationId
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
    $transitionPaletteResults = Wait-Until {
        Find-DescendantByAutomationId $transitionPalette 'Long.CommandPalette.Results'
    } 'The transitioned Command Palette results were not discoverable.'
    $preservedContext = Wait-Until {
        Find-DescendantByAutomationId $transitionPalette 'quality.url'
    } 'The transitioned Command Palette did not preserve the Super Panel context.'
    $preservedCandidate = Wait-Until {
        Find-DescendantByAutomationId `
            $transitionPaletteResults $transitionSelectedId
    } "The transitioned Command Palette did not contain selected result '$transitionSelectedId'."
    try {
        $preservedSelection = Wait-Until {
            $pattern = [Windows.Automation.SelectionItemPattern]`
                $preservedCandidate.GetCurrentPattern(
                    [Windows.Automation.SelectionItemPattern]::Pattern)
            if ($pattern.Current.IsSelected) { $preservedCandidate }
        } "The transitioned Command Palette did not select '$transitionSelectedId'."
    }
    catch {
        $actualSelection = Get-SelectedAutomationItem $transitionPaletteResults
        $actualIdentity = Get-AutomationIdentity $actualSelection
        throw "The transitioned Command Palette expected '$transitionSelectedId' but selected '$actualIdentity'."
    }
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
    $report.super_panel['context_preserved_on_transition'] = $null -ne $preservedContext
    $report.super_panel['selection_preserved_on_transition'] = $null -ne $preservedSelection
    $report.super_panel['preserved_selection_id'] = $transitionSelectedId
    Stop-QualityHost $superPanelTransitionProcess
    $superPanelTransitionProcess = $null

    Write-Stage 'Starting Super Panel to Workspace continuity workflow.'
    $launcherWorkspaceProcess = Start-QualityHost @(
        '--quality-open-super-panel',
        '--quality-context', 'url',
        '--language', 'en-US')
    $continuityTargets = @(
        [pscustomobject]@{
            name = 'management'
            select_action = 6
            module_tab = 'Long.Workspace.ModuleTab.management:root'
        },
        [pscustomobject]@{
            name = 'marketplace'
            select_action = 7
            module_tab = 'Long.Workspace.ModuleTab.marketplace:catalog'
        },
        [pscustomobject]@{
            name = 'settings'
            select_action = 8
            module_tab = 'Long.Workspace.ModuleTab.settings:root'
        })
    foreach ($continuityTarget in $continuityTargets) {
        $continuityPanel = Wait-Until {
            $currentPanel = Find-WindowByAutomationId `
                $launcherWorkspaceProcess.Id 'Long.SuperPanel'
            if ($null -eq $currentPanel) {
                return $null
            }
            $candidate = Find-DescendantByAutomationId `
                $currentPanel 'Long.SuperPanel.Results'
            if ($null -ne $candidate -and
                $candidate.Current.ItemStatus -like `
                    'mode:context-list;*context-items:1;inputs:url,clipboard,text') {
                $currentPanel
            }
        } "Super Panel lost context before opening $($continuityTarget.name)."
        $continuityPanelHandle = [IntPtr]$continuityPanel.Current.NativeWindowHandle
        if ([LongDesktopInput]::WindowAction(
            $continuityPanelHandle,
            $continuityTarget.select_action) -ne 1) {
            throw "Super Panel could not select $($continuityTarget.name)."
        }
        if ([LongDesktopInput]::WindowAction($continuityPanelHandle, 1) -ne 1) {
            throw "Super Panel rejected opening $($continuityTarget.name)."
        }
        Wait-Until {
            $null -eq (Find-WindowByAutomationId `
                $launcherWorkspaceProcess.Id 'Long.SuperPanel')
        } "Super Panel did not hide before opening $($continuityTarget.name)." | Out-Null
        $continuityMain = Wait-Until {
            Find-WindowByAutomationId `
                $launcherWorkspaceProcess.Id 'Long.MainWindow'
        } "Workspace did not appear for $($continuityTarget.name)."
        Wait-Until {
            $candidate = Find-ProcessElementByAutomationId `
                $launcherWorkspaceProcess.Id $continuityTarget.module_tab
            if ($null -ne $candidate -and
                $candidate.Current.ItemStatus -like 'active:true;*') {
                $candidate
            }
        } "Workspace did not activate $($continuityTarget.name)." | Out-Null
        if ([LongDesktopInput]::WindowAction(
            [IntPtr]$continuityMain.Current.NativeWindowHandle, 3) -ne 1) {
            throw "Workspace rejected Escape for $($continuityTarget.name)."
        }
        $restoredPanel = Wait-Until {
            $currentPanel = Find-WindowByAutomationId `
                $launcherWorkspaceProcess.Id 'Long.SuperPanel'
            if ($null -eq $currentPanel) {
                return $null
            }
            $candidate = Find-DescendantByAutomationId `
                $currentPanel 'Long.SuperPanel.Results'
            if ($null -ne $candidate -and
                $candidate.Current.ItemStatus -like `
                    'mode:context-list;*context-items:1;inputs:url,clipboard,text') {
                $currentPanel
            }
        } "Returning from $($continuityTarget.name) did not preserve Super Panel context."
        $report.launcher_workspace_continuity[$continuityTarget.name] = [ordered]@{
            module_activated = $true
            panel_restored = $true
            context_preserved = $true
        }
    }
    $report.launcher_workspace_continuity['coordinate_clicks_used'] = $false
    Stop-QualityHost $launcherWorkspaceProcess
    $launcherWorkspaceProcess = $null

    Write-Stage 'Starting Command Palette to Workspace continuity workflow.'
    $paletteWorkspaceProcess = Start-QualityHost @(
        '--quality-open-palette',
        '--quality-context', 'url',
        '--language', 'en-US')
    $paletteContinuityTargets = @(
        [pscustomobject]@{
            name = 'management'
            query = 'Overview'
            module_tab = 'Long.Workspace.ModuleTab.management:root'
        },
        [pscustomobject]@{
            name = 'marketplace'
            query = 'Plugin Market'
            module_tab = 'Long.Workspace.ModuleTab.marketplace:catalog'
        },
        [pscustomobject]@{
            name = 'settings'
            query = 'Settings'
            module_tab = 'Long.Workspace.ModuleTab.settings:root'
        })
    foreach ($continuityTarget in $paletteContinuityTargets) {
        $continuityPalette = Wait-Until {
            Find-WindowByAutomationId `
                $paletteWorkspaceProcess.Id 'Long.CommandPalette'
        } "Command Palette did not appear before opening $($continuityTarget.name)."
        $continuitySearch = Wait-Until {
            Find-DescendantByAutomationId `
                $continuityPalette 'Long.CommandPalette.Search'
        } "Command Palette search was unavailable before opening $($continuityTarget.name)."
        $continuityResults = Wait-Until {
            Find-DescendantByAutomationId `
                $continuityPalette 'Long.CommandPalette.Results'
        } "Command Palette results were unavailable before opening $($continuityTarget.name)."
        Wait-Until {
            Find-DescendantByAutomationId $continuityPalette 'quality.url'
        } "Command Palette lost URL context before opening $($continuityTarget.name)." | Out-Null
        $continuitySearchPattern =
            [Windows.Automation.ValuePattern]$continuitySearch.GetCurrentPattern(
                [Windows.Automation.ValuePattern]::Pattern)
        $continuitySearchPattern.SetValue($continuityTarget.query)
        Wait-Until {
            Find-DescendantByName $continuityResults $continuityTarget.query
        } "Command Palette did not find $($continuityTarget.name)." | Out-Null
        $continuityPaletteHandle =
            [IntPtr]$continuityPalette.Current.NativeWindowHandle
        if ([LongDesktopInput]::WindowAction($continuityPaletteHandle, 4) -ne 1) {
            throw "Command Palette could not select $($continuityTarget.name)."
        }
        if ([LongDesktopInput]::WindowAction($continuityPaletteHandle, 1) -ne 1) {
            throw "Command Palette rejected opening $($continuityTarget.name)."
        }
        Wait-Until {
            $null -eq (Find-WindowByAutomationId `
                $paletteWorkspaceProcess.Id 'Long.CommandPalette')
        } "Command Palette did not hide before opening $($continuityTarget.name)." | Out-Null
        $continuityMain = Wait-Until {
            Find-WindowByAutomationId `
                $paletteWorkspaceProcess.Id 'Long.MainWindow'
        } "Workspace did not appear for Command Palette $($continuityTarget.name)."
        Wait-Until {
            $candidate = Find-ProcessElementByAutomationId `
                $paletteWorkspaceProcess.Id $continuityTarget.module_tab
            if ($null -ne $candidate -and
                $candidate.Current.ItemStatus -like 'active:true;*') {
                $candidate
            }
        } "Workspace did not activate Command Palette $($continuityTarget.name)." | Out-Null
        if ([LongDesktopInput]::WindowAction(
            [IntPtr]$continuityMain.Current.NativeWindowHandle, 3) -ne 1) {
            throw "Workspace rejected Escape for Command Palette $($continuityTarget.name)."
        }
        $restoredPalette = Wait-Until {
            Find-WindowByAutomationId `
                $paletteWorkspaceProcess.Id 'Long.CommandPalette'
        } "Workspace Escape did not restore Command Palette from $($continuityTarget.name)."
        $restoredSearch = Wait-Until {
            $candidate = Find-DescendantByAutomationId `
                $restoredPalette 'Long.CommandPalette.Search'
            if ($null -eq $candidate) { return $null }
            $pattern = [Windows.Automation.ValuePattern]$candidate.GetCurrentPattern(
                [Windows.Automation.ValuePattern]::Pattern)
            if ($pattern.Current.Value -eq $continuityTarget.query) {
                $candidate
            }
        } "Returning from $($continuityTarget.name) did not preserve the query."
        Wait-Until {
            Find-DescendantByAutomationId $restoredPalette 'quality.url'
        } "Returning from $($continuityTarget.name) did not preserve URL context." | Out-Null
        Wait-Until {
            $restoredSearch.Current.HasKeyboardFocus
        } "Returning from $($continuityTarget.name) did not restore search focus." | Out-Null
        $report.command_palette_workspace_continuity[$continuityTarget.name] =
            [ordered]@{
                module_activated = $true
                palette_restored = $true
                query_preserved = $true
                context_preserved = $true
                focus_restored = $true
            }
    }
    $report.command_palette_workspace_continuity['coordinate_clicks_used'] = $false
    Stop-QualityHost $paletteWorkspaceProcess
    $paletteWorkspaceProcess = $null

    Write-Stage 'Starting Super Panel module-tab close continuity workflow.'
    $panelModuleCloseProcess = Start-QualityHost @(
        '--quality-open-super-panel',
        '--quality-context', 'url',
        '--language', 'en-US')
    for ($cycle = 1; $cycle -le 2; $cycle++) {
        $closePanel = Wait-Until {
            Find-WindowByAutomationId `
                $panelModuleCloseProcess.Id 'Long.SuperPanel'
        } "Super Panel did not appear for module-close cycle $cycle."
        $closePanelResults = Wait-Until {
            $candidate = Find-DescendantByAutomationId `
                $closePanel 'Long.SuperPanel.Results'
            if ($null -ne $candidate -and
                $candidate.Current.ItemStatus -like `
                    'mode:context-list;*context-items:1;inputs:url,clipboard,text') {
                $candidate
            }
        } "Super Panel context was not preserved before module-close cycle $cycle."
        $closePanelHandle = [IntPtr]$closePanel.Current.NativeWindowHandle
        if ([LongDesktopInput]::WindowAction($closePanelHandle, 7) -ne 1) {
            throw "Super Panel could not select Plugin Market in close cycle $cycle."
        }
        if ([LongDesktopInput]::WindowAction($closePanelHandle, 1) -ne 1) {
            throw "Super Panel could not open Plugin Market in close cycle $cycle."
        }
        $closeMain = Wait-Until {
            Find-WindowByAutomationId `
                $panelModuleCloseProcess.Id 'Long.MainWindow'
        } "Workspace did not appear for Super Panel close cycle $cycle."
        $marketClose = Wait-Until {
            Find-ProcessElementByAutomationId `
                $panelModuleCloseProcess.Id `
                'Long.Workspace.ModuleClose.marketplace:catalog'
        } "Plugin Market close action was unavailable in Super Panel cycle $cycle."
        Invoke-AutomationElement $marketClose `
            "Plugin Market close action failed in Super Panel cycle $cycle."
        Wait-Until {
            $null -eq (Find-ProcessElementByAutomationId `
                $panelModuleCloseProcess.Id `
                'Long.Workspace.ModuleTab.marketplace:catalog')
        } "Plugin Market tab remained after Super Panel close cycle $cycle." | Out-Null
        $restoredClosePanel = Wait-Until {
            Find-WindowByAutomationId `
                $panelModuleCloseProcess.Id 'Long.SuperPanel'
        } "Closing Plugin Market did not restore Super Panel in cycle $cycle."
        Wait-Until {
            $candidate = Find-DescendantByAutomationId `
                $restoredClosePanel 'Long.SuperPanel.Results'
            if ($null -ne $candidate -and
                $candidate.Current.ItemStatus -like `
                    'mode:context-list;*context-items:1;inputs:url,clipboard,text') {
                $candidate
            }
        } "Closing Plugin Market lost Super Panel context in cycle $cycle." | Out-Null
        $restoredPanelHandle =
            [IntPtr]$restoredClosePanel.Current.NativeWindowHandle
        Wait-Until {
            [LongDesktopInput]::ForegroundWindow() -eq $restoredPanelHandle
        } "Closing Plugin Market did not restore Super Panel focus in cycle $cycle." | Out-Null
    }
    $report.launcher_module_close_continuity['super_panel'] = [ordered]@{
        cycles = 2
        panel_restored = $true
        context_preserved = $true
        focus_restored = $true
        repeated_entry = $true
    }
    Stop-QualityHost $panelModuleCloseProcess
    $panelModuleCloseProcess = $null

    Write-Stage 'Starting Command Palette module-tab close continuity workflow.'
    $paletteModuleCloseProcess = Start-QualityHost @(
        '--quality-open-palette',
        '--quality-context', 'url',
        '--language', 'en-US')
    for ($cycle = 1; $cycle -le 2; $cycle++) {
        $closePalette = Wait-Until {
            Find-WindowByAutomationId `
                $paletteModuleCloseProcess.Id 'Long.CommandPalette'
        } "Command Palette did not appear for module-close cycle $cycle."
        $closeSearch = Wait-Until {
            Find-DescendantByAutomationId `
                $closePalette 'Long.CommandPalette.Search'
        } "Command Palette search was unavailable in close cycle $cycle."
        $closeResults = Wait-Until {
            Find-DescendantByAutomationId `
                $closePalette 'Long.CommandPalette.Results'
        } "Command Palette results were unavailable in close cycle $cycle."
        Wait-Until {
            Find-DescendantByAutomationId $closePalette 'quality.url'
        } "Command Palette lost URL context before close cycle $cycle." | Out-Null
        $closeSearchPattern =
            [Windows.Automation.ValuePattern]$closeSearch.GetCurrentPattern(
                [Windows.Automation.ValuePattern]::Pattern)
        $closeSearchPattern.SetValue('Plugin Market')
        Wait-Until {
            Find-DescendantByName $closeResults 'Plugin Market'
        } "Command Palette did not find Plugin Market in close cycle $cycle." | Out-Null
        $closePaletteHandle = [IntPtr]$closePalette.Current.NativeWindowHandle
        if ([LongDesktopInput]::WindowAction($closePaletteHandle, 4) -ne 1) {
            throw "Command Palette could not select Plugin Market in close cycle $cycle."
        }
        if ([LongDesktopInput]::WindowAction($closePaletteHandle, 1) -ne 1) {
            throw "Command Palette could not open Plugin Market in close cycle $cycle."
        }
        Wait-Until {
            Find-WindowByAutomationId `
                $paletteModuleCloseProcess.Id 'Long.MainWindow'
        } "Workspace did not appear for Command Palette close cycle $cycle." | Out-Null
        $marketClose = Wait-Until {
            Find-ProcessElementByAutomationId `
                $paletteModuleCloseProcess.Id `
                'Long.Workspace.ModuleClose.marketplace:catalog'
        } "Plugin Market close action was unavailable in Command Palette cycle $cycle."
        Invoke-AutomationElement $marketClose `
            "Plugin Market close action failed in Command Palette cycle $cycle."
        Wait-Until {
            $null -eq (Find-ProcessElementByAutomationId `
                $paletteModuleCloseProcess.Id `
                'Long.Workspace.ModuleTab.marketplace:catalog')
        } "Plugin Market tab remained after Command Palette close cycle $cycle." | Out-Null
        $restoredClosePalette = Wait-Until {
            Find-WindowByAutomationId `
                $paletteModuleCloseProcess.Id 'Long.CommandPalette'
        } "Closing Plugin Market did not restore Command Palette in cycle $cycle."
        $restoredCloseSearch = Wait-Until {
            $candidate = Find-DescendantByAutomationId `
                $restoredClosePalette 'Long.CommandPalette.Search'
            if ($null -eq $candidate) { return $null }
            $pattern = [Windows.Automation.ValuePattern]$candidate.GetCurrentPattern(
                [Windows.Automation.ValuePattern]::Pattern)
            if ($pattern.Current.Value -eq 'Plugin Market') {
                $candidate
            }
        } "Closing Plugin Market did not preserve Command Palette query in cycle $cycle."
        Wait-Until {
            Find-DescendantByAutomationId $restoredClosePalette 'quality.url'
        } "Closing Plugin Market lost Command Palette context in cycle $cycle." | Out-Null
        Wait-Until {
            $restoredCloseSearch.Current.HasKeyboardFocus
        } "Closing Plugin Market did not restore Command Palette focus in cycle $cycle." | Out-Null
    }
    $report.launcher_module_close_continuity['command_palette'] = [ordered]@{
        cycles = 2
        palette_restored = $true
        query_preserved = $true
        context_preserved = $true
        focus_restored = $true
        repeated_entry = $true
    }
    $report.launcher_module_close_continuity['coordinate_clicks_used'] = $false
    Stop-QualityHost $paletteModuleCloseProcess
    $paletteModuleCloseProcess = $null
    }

    Write-Stage 'Starting Workspace management navigation workflow.'
    $managementProcess = Start-QualityHost @(
        '--language', 'en-US',
        '--quality-width', [string]$SettingsNavigationWidth,
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
    $destinationFocusOrder = @()
    $destinationIds = @(
        'Long.Management.Destination.Plugins',
        'Long.Management.Destination.Market',
        'Long.Management.Destination.Workflows',
        'Long.Management.Destination.Widgets',
        'Long.Management.Destination.System',
        'Long.Management.Destination.Diagnostics',
        'Long.Management.Destination.Developer',
        'Long.Management.Destination.Settings')
    foreach ($destinationId in $destinationIds) {
        $destination = Set-AutomationFocus {
            Find-DescendantByAutomationId $managementMain $destinationId
        } "Management destination $destinationId could not receive keyboard focus."
        $destinationFocusOrder += $destination.Current.AutomationId
    }
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
    $marketPluginRail = Wait-Until {
        Find-DescendantByAutomationId `
            $managementMain 'Long.Workspace.PluginRail'
    } 'The installed plugin rail was not visible in Plugin Market.'
    $report.automation_semantics.management_navigation[
        'market_plugin_rail_visible'] = $null -ne $marketPluginRail
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
    Wait-Until {
        $null -eq (Find-DescendantByAutomationId `
            $managementMain 'Long.Workspace.PluginRail')
    } 'The installed plugin rail remained visible in Settings.' | Out-Null
    $report.automation_semantics.management_navigation[
        'settings_plugin_rail_hidden'] = $true
    Wait-Until {
        $null -ne (Find-DescendantByAutomationId `
            $managementMain 'Long.Settings.CategoryItem.appearance') -or
        $null -ne (Find-DescendantByAutomationId `
            $managementMain 'Long.Settings.CategorySelector')
    } 'The Settings category navigation was not discoverable.' | Out-Null
    $settingsCategoryButton = Find-DescendantByAutomationId `
        $managementMain 'Long.Settings.CategoryItem.appearance'
    $settingsCompactCategories = Find-DescendantByAutomationId `
        $managementMain 'Long.Settings.CategorySelector'
    $settingsCategoryMode = if ($null -ne $settingsCategoryButton) {
        'sidebar'
    } else {
        'compact'
    }
    if ($SettingsNavigationExpectedMode -ne 'auto' -and
        $settingsCategoryMode -ne $SettingsNavigationExpectedMode) {
        throw ("Expected Settings category navigation mode " +
            "'$SettingsNavigationExpectedMode', received '$settingsCategoryMode'.")
    }
    $settingsCategoryNames = [ordered]@{
        appearance = 'Personalization'
        interaction = 'Interaction and panel'
        connections = 'Connections and privacy'
        updates = 'Updates'
    }
    $settingsCategoryPositions = @{
        appearance = 1
        interaction = 2
        connections = 3
        updates = 4
    }
    $settingsCategorySemantics = [ordered]@{}
    if ($settingsCategoryMode -eq 'compact') {
        $selectorSemantics = Get-AutomationSemantics `
            $settingsCompactCategories 'ControlType.ComboBox' `
            'The compact Settings category selector semantics failed.'
        if ($selectorSemantics.name -ne 'Settings category') {
            throw "Unexpected compact Settings category name '$($selectorSemantics.name)'."
        }
        $settingsCategorySemantics['selector'] = $selectorSemantics
    }
    foreach ($category in @('interaction', 'connections', 'updates', 'appearance')) {
        if ($settingsCategoryMode -eq 'sidebar') {
            $categoryItem = Wait-Until {
                Find-DescendantByAutomationId `
                    $managementMain "Long.Settings.CategoryItem.$category"
            } "The Settings category '$category' was not discoverable."
            Invoke-AutomationElement $categoryItem `
                "The Settings category '$category' could not be invoked."
        } else {
            $expandPattern = $null
            if (-not $settingsCompactCategories.TryGetCurrentPattern(
                [Windows.Automation.ExpandCollapsePattern]::Pattern,
                [ref]$expandPattern)) {
                throw 'The compact Settings category selector could not expand.'
            }
            ([Windows.Automation.ExpandCollapsePattern]$expandPattern).Expand()
            $categoryItem = Wait-Until {
                Find-ProcessSelectionItemByAutomationId `
                    $managementProcess.Id `
                    "Long.Settings.CompactCategoryItem.$category"
            } "The compact Settings category '$category' was not discoverable."
            Select-AutomationElement $categoryItem `
                "The compact Settings category '$category' could not be selected."
            $compactItemSemantics = Get-AutomationSemantics `
                $categoryItem 'ControlType.ListItem' `
                "The compact Settings category '$category' semantics failed."
            if ($compactItemSemantics.name -ne $settingsCategoryNames[$category]) {
                throw "The compact Settings category '$category' exposed an inconsistent name."
            }
            $selectionPattern = [Windows.Automation.SelectionItemPattern]`
                $categoryItem.GetCurrentPattern(
                    [Windows.Automation.SelectionItemPattern]::Pattern)
            if (-not $selectionPattern.Current.IsSelected) {
                throw "The compact Settings category '$category' did not remain selected."
            }
            $compactItemSemantics['selected'] = $true
        }
        $categoryContent = Wait-Until {
            Find-DescendantByAutomationId `
                $managementMain "Long.Settings.CategoryContent.$category"
        } "The Settings category '$category' content did not become visible."
        $contentSemantics = Get-AutomationSemantics `
            $categoryContent 'ControlType.Text' `
            "The Settings category '$category' heading semantics failed."
        if ($contentSemantics.name -ne $settingsCategoryNames[$category]) {
            throw "Unexpected Settings category heading '$($contentSemantics.name)'."
        }
        if ($settingsCategoryMode -eq 'sidebar') {
            $categoryItem = Wait-Until {
                $candidate = Find-DescendantByAutomationId `
                    $managementMain "Long.Settings.CategoryItem.$category"
                if ($null -ne $candidate -and
                    $candidate.Current.ItemStatus -eq (
                        "Selected, $($settingsCategoryPositions[$category]) of 4")) {
                    $candidate
                }
            } "The Settings category '$category' did not expose its selected state."
            $itemSemantics = Get-AutomationSemantics `
                $categoryItem 'ControlType.Button' `
                "The Settings category '$category' semantics failed."
            if ($itemSemantics.name -ne $settingsCategoryNames[$category]) {
                throw "The Settings category '$category' exposed an inconsistent name."
            }
            $settingsCategorySemantics[$category] = [ordered]@{
                item = $itemSemantics
                heading = $contentSemantics
            }
        } else {
            $settingsCategorySemantics[$category] = [ordered]@{
                item = $compactItemSemantics
                heading = $contentSemantics
            }
        }
    }
    if ($settingsCategoryMode -eq 'sidebar') {
        foreach ($category in $settingsCategoryNames.Keys) {
            $categoryItem = Find-DescendantByAutomationId `
                $managementMain "Long.Settings.CategoryItem.$category"
            $expectedStatus = if ($category -eq 'appearance') {
                "Selected, $($settingsCategoryPositions[$category]) of 4"
            } else {
                "Not selected, $($settingsCategoryPositions[$category]) of 4"
            }
            if ($categoryItem.Current.ItemStatus -ne $expectedStatus) {
                throw "The Settings category '$category' did not expose '$expectedStatus'."
            }
        }
    }
    $report.automation_semantics.management_navigation[
        'settings_categories_selectable'] = $true
    $report.automation_semantics.management_navigation[
        'settings_category_navigation_mode'] = $settingsCategoryMode
    $report.automation_semantics.management_navigation[
        'settings_category_semantics'] = $settingsCategorySemantics
    $settingsClose = Wait-Until {
        Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleClose.settings:root'
    } 'The Settings module close action was not discoverable.'
    $settingsCloseSemantics = Get-AutomationSemantics `
        $settingsClose 'ControlType.Button' `
        'Settings close-action semantics failed.'
    if ($settingsCloseSemantics.name -ne 'Close Settings') {
        throw "Unexpected Settings close name: $($settingsCloseSemantics.name)"
    }
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

    Write-Stage 'Opening Developer resources and validating document keyboard access.'
    $developerDestination = Wait-Until {
        Find-DescendantByAutomationId `
            $managementMain 'Long.Management.Destination.Developer'
    } 'Developer was not restored after closing Settings.'
    Invoke-AutomationElement $developerDestination `
        'The Developer destination did not support InvokePattern.'
    $developerTab = Wait-Until {
        $candidate = Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleTab.developer:root'
        if ($null -ne $candidate -and
            $candidate.Current.ItemStatus -like 'active:true;*') {
            $candidate
        }
    } 'Opening Developer did not create an active Workspace module tab.'
    $developerDocuments = Wait-Until {
        Find-DescendantByAutomationId `
            $managementMain 'Long.Developer.Documents'
    } 'The Developer document collection was not discoverable.'
    $developerDocument = Set-AutomationFocus {
        Find-DescendantByAutomationId `
            $developerDocuments 'Long.Developer.Document.1'
    } 'The first Developer document did not receive keyboard focus.'
    $developerDocumentSemantics = Get-AutomationSemantics `
        $developerDocument 'ControlType.Button' `
        'The Developer document button semantics failed.'

    Write-Stage 'Validating owned Developer secondary windows and keyboard close.'
    $managementHandle = [IntPtr]$managementMain.Current.NativeWindowHandle
    Invoke-AutomationElement $developerDocument `
        'The Developer document did not support InvokePattern.'
    $docViewer = Wait-Until {
        Find-ProcessElementByAutomationId `
            $managementProcess.Id 'Long.Developer.DocViewer.Window'
    } 'The Developer document viewer window was not discoverable.'
    $docViewerHandle = [IntPtr]$docViewer.Current.NativeWindowHandle
    if ([LongDesktopInput]::OwnerWindow($docViewerHandle) -ne $managementHandle -or
        [LongDesktopInput]::HasTaskbarAppStyle($docViewerHandle)) {
        throw 'The Developer document viewer did not preserve owned tool-window taskbar semantics.'
    }
    [LongDesktopInput]::Activate($docViewerHandle) | Out-Null
    Start-Sleep -Milliseconds 700
    [LongDesktopInput]::KeyPress(0x1B) | Out-Null
    Wait-Until {
        $null -eq (Find-ProcessElementByAutomationId `
            $managementProcess.Id 'Long.Developer.DocViewer.Window')
    } 'Escape did not close the Developer document viewer.' | Out-Null

    $designOpen = Wait-Until {
        Find-DescendantByAutomationId `
            $managementMain 'Long.Developer.DesignPreview.Open'
    } 'The Design System preview action was not discoverable.'
    Invoke-AutomationElement $designOpen `
        'The Design System preview action did not support InvokePattern.'
    $designWindow = Wait-Until {
        Find-ProcessElementByAutomationId `
            $managementProcess.Id 'Long.Developer.DesignPreview.Window'
    } 'The Design System preview window was not discoverable.'
    $designHandle = [IntPtr]$designWindow.Current.NativeWindowHandle
    if ([LongDesktopInput]::OwnerWindow($designHandle) -ne $managementHandle -or
        [LongDesktopInput]::HasTaskbarAppStyle($designHandle)) {
        throw 'The Design System preview did not preserve owned tool-window taskbar semantics.'
    }
    [LongDesktopInput]::Activate($designHandle) | Out-Null
    [LongDesktopInput]::KeyPress(0x1B) | Out-Null
    Wait-Until {
        $null -eq (Find-ProcessElementByAutomationId `
            $managementProcess.Id 'Long.Developer.DesignPreview.Window')
    } 'Escape did not close the Design System preview.' | Out-Null

    $workbenchOpen = Wait-Until {
        Find-DescendantByAutomationId `
            $managementMain 'Long.Developer.Workbench.Open'
    } 'The plugin workbench action was not discoverable.'
    Invoke-AutomationElement $workbenchOpen `
        'The plugin workbench action did not support InvokePattern.'
    $workbenchWindow = Wait-Until {
        Find-ProcessElementByAutomationId `
            $managementProcess.Id 'Long.Developer.Workbench.Window'
    } 'The plugin workbench window was not discoverable.'
    $workbenchHandle = [IntPtr]$workbenchWindow.Current.NativeWindowHandle
    if ([LongDesktopInput]::OwnerWindow($workbenchHandle) -ne $managementHandle -or
        [LongDesktopInput]::HasTaskbarAppStyle($workbenchHandle)) {
        throw 'The plugin workbench did not preserve owned tool-window taskbar semantics.'
    }
    [LongDesktopInput]::Activate($workbenchHandle) | Out-Null
    Start-Sleep -Milliseconds 900
    [LongDesktopInput]::KeyPress(0x1B) | Out-Null
    Wait-Until {
        $null -eq (Find-ProcessElementByAutomationId `
            $managementProcess.Id 'Long.Developer.Workbench.Window')
    } 'Escape did not close the plugin workbench.' | Out-Null
    $developerClose = Wait-Until {
        Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleClose.developer:root'
    } 'The Developer module close action was not discoverable.'
    Invoke-AutomationElement $developerClose `
        'The Developer module close action did not support InvokePattern.'
    Wait-Until {
        $null -eq (Find-ProcessElementByAutomationId `
            $managementProcess.Id `
            'Long.Workspace.ModuleTab.developer:root')
    } 'Closing Developer did not remove its Workspace tab.' | Out-Null
    Wait-Until {
        $rootTab.Current.ItemStatus -like 'active:true;*'
    } 'Closing Developer did not restore the management root.' | Out-Null
    $report.automation_semantics.management_navigation[
        'developer_document'] = $developerDocumentSemantics

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
    $marketCloseSemantics = Get-AutomationSemantics `
        $marketClose 'ControlType.Button' `
        'Plugin Market close-action semantics failed.'
    if ($marketCloseSemantics.name -ne 'Close Plugin Market') {
        throw "Unexpected Plugin Market close name: $($marketCloseSemantics.name)"
    }
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
        destination_focus_order = $destinationFocusOrder
        destination_focus_verified = $destinationFocusOrder.Count -eq 8
        scoped_search_filtered = $true
        market_opened_as_module = $true
        settings_close_restored_root = $true
        developer_document_keyboard_access = $true
        developer_secondary_windows_owned = $true
        developer_secondary_windows_escape_close = $true
        developer_close_restored_root = $true
        market_tab_reactivated = $true
        market_close_restored_root = $true
        coordinate_clicks_used = $false
        physical_keyboard_validated = $false
        physical_narrator_validated = $false
        settings_close_name = $settingsCloseSemantics.name
        market_close_name = $marketCloseSemantics.name
    }
    Stop-QualityHost $managementProcess
    $managementProcess = $null
    }

    if (-not $SettingsNavigationOnly -and
        ($PluginCommandManagementOnly -or
         (-not $WorkflowOnly -and
          -not $WorkflowOutputOnly -and
          -not $WorkflowSchemaOnly))) {
    Write-Stage 'Starting plugin command-management workflow.'
    $pluginSettingsProcess = Start-QualityHost @(
        '--language', 'en-US',
        '--quality-open-plugin-settings', 'com.long.base64',
        '--quality-width', '1120',
        '--quality-height', '760')
    $pluginSettingsMain = Wait-Until {
        Find-WindowByAutomationId $pluginSettingsProcess.Id 'Long.MainWindow'
    } 'The plugin command-management host did not appear.'
    $commandsTab = Wait-Until {
        Find-DescendantByAutomationId `
            $pluginSettingsMain 'Long.Workspace.PluginSettings.Tab.Commands'
    } 'The plugin Commands tab was not discoverable.'
    $commandButtonId = `
        'Long.Workspace.PluginSettings.Command.com.long.base64:base64.encode'
    $commandPin = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandButtonId
    } 'The Base64 encode command-management action was not discoverable.'
    $initialPinState = [string]$commandPin.Current.ItemStatus
    Invoke-AutomationElement $commandPin `
        'The Base64 encode pin action did not support InvokePattern.'
    $commandPin = Wait-Until {
        $candidate = Find-DescendantByAutomationId `
            $pluginSettingsMain $commandButtonId
        if ($null -ne $candidate -and
            [string]$candidate.Current.ItemStatus -ne $initialPinState) {
            $candidate
        }
    } 'The Base64 encode pin state did not change.'
    Invoke-AutomationElement $commandPin `
        'The Base64 encode restore-pin action did not support InvokePattern.'
    Wait-Until {
        $candidate = Find-DescendantByAutomationId `
            $pluginSettingsMain $commandButtonId
        $null -ne $candidate -and
            [string]$candidate.Current.ItemStatus -eq $initialPinState
    } 'The Base64 encode pin state was not restored.' | Out-Null
    $commandEnabledId = `
        'Long.Workspace.PluginSettings.CommandEnabled.com.long.base64:base64.encode'
    $commandEnabled = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandEnabledId
    } 'The Base64 encode enabled-state control was not discoverable.'
    $enabledPattern = $null
    if (-not $commandEnabled.TryGetCurrentPattern(
        [Windows.Automation.TogglePattern]::Pattern,
        [ref]$enabledPattern)) {
        throw 'The Base64 encode enabled-state control did not expose TogglePattern.'
    }
    $initialEnabledState = `
        ([Windows.Automation.TogglePattern]$enabledPattern).Current.ToggleState
    ([Windows.Automation.TogglePattern]$enabledPattern).Toggle()
    $commandEnabled = Wait-Until {
        $candidate = Find-DescendantByAutomationId `
            $pluginSettingsMain $commandEnabledId
        if ($null -eq $candidate) { return $null }
        $candidatePattern = $null
        if (-not $candidate.TryGetCurrentPattern(
            [Windows.Automation.TogglePattern]::Pattern,
            [ref]$candidatePattern)) { return $null }
        if (([Windows.Automation.TogglePattern]$candidatePattern).Current.ToggleState `
            -ne $initialEnabledState) { return $candidate }
        return $null
    } 'The Base64 encode enabled state did not change.'
    $enabledPattern = $null
    $commandEnabled.TryGetCurrentPattern(
        [Windows.Automation.TogglePattern]::Pattern,
        [ref]$enabledPattern) | Out-Null
    ([Windows.Automation.TogglePattern]$enabledPattern).Toggle()
    Wait-Until {
        $candidate = Find-DescendantByAutomationId `
            $pluginSettingsMain $commandEnabledId
        if ($null -eq $candidate) { return $false }
        $candidatePattern = $null
        $candidate.TryGetCurrentPattern(
            [Windows.Automation.TogglePattern]::Pattern,
            [ref]$candidatePattern) | Out-Null
        $null -ne $candidatePattern -and
            ([Windows.Automation.TogglePattern]$candidatePattern).Current.ToggleState `
                -eq $initialEnabledState
    } 'The Base64 encode enabled state was not restored.' | Out-Null
    $commandAliasesId = `
        'Long.Workspace.PluginSettings.CommandAliases.com.long.base64:base64.encode'
    $commandAliasesSaveId = `
        'Long.Workspace.PluginSettings.CommandAliasesSave.com.long.base64:base64.encode'
    $commandAliases = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandAliasesId
    } 'The Base64 encode custom-alias editor was not discoverable.'
    $aliasValue = [Windows.Automation.ValuePattern]$commandAliases.GetCurrentPattern(
        [Windows.Automation.ValuePattern]::Pattern)
    $initialAliases = [string]$aliasValue.Current.Value
    $qualityAlias = 'quality-base64-alias'
    $aliasValue.SetValue($qualityAlias)
    $commandAliasesSave = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandAliasesSaveId
    } 'The Base64 encode custom-alias save action was not discoverable.'
    Invoke-AutomationElement $commandAliasesSave `
        'The Base64 encode custom-alias save action did not support InvokePattern.'
    Wait-CommandFeedback `
        $pluginSettingsMain 'before-alias-save' 'Custom aliases saved.' `
        'The Base64 encode custom-alias save did not complete.' | Out-Null
    $commandAliases = Wait-Until {
        $candidate = Find-DescendantByAutomationId `
            $pluginSettingsMain $commandAliasesId
        if ($null -eq $candidate) { return $null }
        $candidateValue = [Windows.Automation.ValuePattern]$candidate.GetCurrentPattern(
            [Windows.Automation.ValuePattern]::Pattern)
        $candidateSave = Find-DescendantByAutomationId `
            $pluginSettingsMain $commandAliasesSaveId
        if ([string]$candidateValue.Current.Value -eq $qualityAlias -and
            $null -ne $candidateSave -and $candidateSave.Current.IsEnabled) {
            return $candidate
        }
        return $null
    } 'The Base64 encode custom alias was not persisted.'
    $aliasValue = [Windows.Automation.ValuePattern]$commandAliases.GetCurrentPattern(
        [Windows.Automation.ValuePattern]::Pattern)
    $aliasValue.SetValue($initialAliases)
    $commandFeedback = Wait-Until {
        Find-DescendantByAutomationId `
            $pluginSettingsMain 'Long.Workspace.PluginSettings.CommandFeedback'
    } 'The plugin command feedback surface was not refreshed.'
    $feedbackRevision = [string]$commandFeedback.Current.ItemStatus
    $commandAliasesSave = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandAliasesSaveId
    } 'The Base64 encode custom-alias restore action was not discoverable.'
    Invoke-AutomationElement $commandAliasesSave `
        'The Base64 encode custom-alias restore action did not support InvokePattern.'
    Wait-CommandFeedback `
        $pluginSettingsMain $feedbackRevision 'Custom aliases saved.' `
        'The Base64 encode custom-alias restore did not complete.' | Out-Null
    Wait-Until {
        $candidate = Find-DescendantByAutomationId `
            $pluginSettingsMain $commandAliasesId
        if ($null -eq $candidate) { return $false }
        $candidateValue = [Windows.Automation.ValuePattern]$candidate.GetCurrentPattern(
            [Windows.Automation.ValuePattern]::Pattern)
        $candidateSave = Find-DescendantByAutomationId `
            $pluginSettingsMain $commandAliasesSaveId
        [string]$candidateValue.Current.Value -eq $initialAliases -and
            $null -ne $candidateSave -and $candidateSave.Current.IsEnabled
    } 'The Base64 encode custom alias was not restored.' | Out-Null
    $commandHotkeyId = `
        'Long.Workspace.PluginSettings.CommandHotkey.com.long.base64:base64.encode'
    $commandHotkeySaveId = `
        'Long.Workspace.PluginSettings.CommandHotkeySave.com.long.base64:base64.encode'
    $commandHotkeyClearId = `
        'Long.Workspace.PluginSettings.CommandHotkeyClear.com.long.base64:base64.encode'
    $commandHotkey = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandHotkeyId
    } 'The Base64 encode command-shortcut editor was not discoverable.'
    $hotkeyValue = [Windows.Automation.ValuePattern]$commandHotkey.GetCurrentPattern(
        [Windows.Automation.ValuePattern]::Pattern)
    $initialHotkey = [string]$hotkeyValue.Current.Value
    $qualityHotkey = 'Ctrl+Alt+Shift+F12'
    $hotkeyValue.SetValue($qualityHotkey)
    $commandHotkeySave = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandHotkeySaveId
    } 'The Base64 encode command-shortcut save action was not discoverable.'
    Invoke-AutomationElement $commandHotkeySave `
        'The Base64 encode command-shortcut save action did not support InvokePattern.'
    Wait-Until {
        $candidate = Find-DescendantByAutomationId `
            $pluginSettingsMain $commandHotkeyId
        if ($null -eq $candidate) { return $false }
        $candidateValue = [Windows.Automation.ValuePattern]$candidate.GetCurrentPattern(
            [Windows.Automation.ValuePattern]::Pattern)
        [string]$candidateValue.Current.Value -eq $qualityHotkey
    } 'The Base64 encode command shortcut was not persisted.' | Out-Null
    $commandHotkeyClear = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandHotkeyClearId
    } 'The Base64 encode command-shortcut clear action was not discoverable.'
    Invoke-AutomationElement $commandHotkeyClear `
        'The Base64 encode command-shortcut clear action did not support InvokePattern.'
    Wait-Until {
        $candidate = Find-DescendantByAutomationId `
            $pluginSettingsMain $commandHotkeyId
        if ($null -eq $candidate) { return $false }
        $candidateValue = [Windows.Automation.ValuePattern]$candidate.GetCurrentPattern(
            [Windows.Automation.ValuePattern]::Pattern)
        [string]$candidateValue.Current.Value -eq ''
    } 'The Base64 encode command shortcut was not cleared.' | Out-Null
    if (-not [string]::IsNullOrWhiteSpace($initialHotkey)) {
        $commandHotkey = Wait-Until {
            Find-DescendantByAutomationId $pluginSettingsMain $commandHotkeyId
        } 'The Base64 encode command-shortcut editor was not restored.'
        $hotkeyValue = [Windows.Automation.ValuePattern]$commandHotkey.GetCurrentPattern(
            [Windows.Automation.ValuePattern]::Pattern)
        $hotkeyValue.SetValue($initialHotkey)
        $commandHotkeySave = Wait-Until {
            Find-DescendantByAutomationId $pluginSettingsMain $commandHotkeySaveId
        } 'The Base64 encode command-shortcut restore action was not discoverable.'
        Invoke-AutomationElement $commandHotkeySave `
            'The Base64 encode command-shortcut restore action did not support InvokePattern.'
        Wait-Until {
            $candidate = Find-DescendantByAutomationId `
                $pluginSettingsMain $commandHotkeyId
            if ($null -eq $candidate) { return $false }
            $candidateValue = [Windows.Automation.ValuePattern]$candidate.GetCurrentPattern(
                [Windows.Automation.ValuePattern]::Pattern)
            [string]$candidateValue.Current.Value -eq $initialHotkey
        } 'The Base64 encode command shortcut was not restored.' | Out-Null
    }
    $commandsTab = Wait-Until {
        Find-DescendantByAutomationId `
            $pluginSettingsMain 'Long.Workspace.PluginSettings.Tab.Commands'
    } 'The plugin Commands tab was not available for semantic validation.'
    $commandPin = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandButtonId
    } 'The Base64 encode pin action was not available for semantic validation.'
    $commandEnabled = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandEnabledId
    } 'The Base64 encode toggle was not available for semantic validation.'
    $decodeEnabled = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain `
            'Long.Workspace.PluginSettings.CommandEnabled.com.long.base64:base64.decode'
    } 'The Base64 decode toggle was not available for semantic validation.'
    $commandAliases = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandAliasesId
    } 'The Base64 encode aliases editor was not available for semantic validation.'
    $commandAliasesSave = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandAliasesSaveId
    } 'The Base64 encode aliases save action was not available for semantic validation.'
    $commandHotkey = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandHotkeyId
    } 'The Base64 encode shortcut editor was not available for semantic validation.'
    $commandHotkeySave = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandHotkeySaveId
    } 'The Base64 encode shortcut save action was not available for semantic validation.'
    $commandHotkeyClear = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain $commandHotkeyClearId
    } 'The Base64 encode shortcut clear action was not available for semantic validation.'
    $commandHotkeyStatus = Wait-Until {
        Find-DescendantByAutomationId $pluginSettingsMain `
            'Long.Workspace.PluginSettings.CommandHotkeyStatus.com.long.base64:base64.encode'
    } 'The Base64 encode shortcut status was not available for semantic validation.'
    $commandFeedback = Wait-Until {
        Find-DescendantByAutomationId `
            $pluginSettingsMain 'Long.Workspace.PluginSettings.CommandFeedback'
    } 'The plugin command feedback was not available for semantic validation.'
    $commandSemanticSnapshot = [ordered]@{
        tab = Get-AutomationSemantics $commandsTab 'ControlType.TabItem' `
            'Plugin Commands tab semantics failed.'
        pin = Get-AutomationSemantics $commandPin 'ControlType.Button' `
            'Plugin command pin semantics failed.'
        enabled = Get-AutomationSemantics $commandEnabled 'ControlType.CheckBox' `
            'Plugin command enabled-state semantics failed.'
        decode_enabled = Get-AutomationSemantics $decodeEnabled 'ControlType.CheckBox' `
            'Second plugin command enabled-state semantics failed.'
        aliases = Get-AutomationSemantics $commandAliases 'ControlType.Edit' `
            'Plugin command aliases semantics failed.'
        aliases_save = Get-AutomationSemantics $commandAliasesSave 'ControlType.Button' `
            'Plugin command aliases save semantics failed.'
        hotkey = Get-AutomationSemantics $commandHotkey 'ControlType.Edit' `
            'Plugin command shortcut semantics failed.'
        hotkey_save = Get-AutomationSemantics $commandHotkeySave 'ControlType.Button' `
            'Plugin command shortcut save semantics failed.'
        hotkey_clear = Get-AutomationSemantics $commandHotkeyClear 'ControlType.Button' `
            'Plugin command shortcut clear semantics failed.'
        hotkey_status = Get-AutomationSemantics $commandHotkeyStatus 'ControlType.Text' `
            'Plugin command shortcut status semantics failed.'
        feedback = Get-AutomationSemantics $commandFeedback 'ControlType.Text' `
            'Plugin command feedback semantics failed.'
    }
    if ($commandSemanticSnapshot.enabled.name -eq
        $commandSemanticSnapshot.decode_enabled.name) {
        throw 'Plugin command controls did not include unique command context.'
    }
    foreach ($semanticKey in @(
        'pin', 'enabled', 'aliases', 'aliases_save',
        'hotkey', 'hotkey_save', 'hotkey_clear', 'hotkey_status')) {
        if ($commandSemanticSnapshot[$semanticKey].name -notlike '*:*') {
            throw "Plugin command semantic '$semanticKey' omitted command context."
        }
    }
    if ([string]::IsNullOrWhiteSpace($commandSemanticSnapshot.aliases.help_text) -or
        [string]::IsNullOrWhiteSpace($commandSemanticSnapshot.hotkey.help_text)) {
        throw 'Plugin command editors did not expose usage guidance.'
    }
    $report.automation_semantics['plugin_command_management'] = [ordered]@{
        commands_tab_discoverable = $null -ne $commandsTab
        stable_command_identity = $commandButtonId
        pin_state_changed = $true
        pin_state_restored = $true
        enabled_state_changed = $true
        enabled_state_restored = $true
        custom_alias_persisted = $true
        custom_alias_restored = $true
        command_hotkey_persisted = $true
        command_hotkey_cleared = $true
        command_hotkey_restored = $true
        controls = $commandSemanticSnapshot
    }
    Stop-QualityHost $pluginSettingsProcess
    $pluginSettingsProcess = $null
    }

    if (-not $SettingsNavigationOnly -and
        -not $PluginCommandManagementOnly -and
        -not $WorkflowOutputOnly -and
        -not $WorkflowSchemaOnly) {
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
    $compactGridMode = Wait-Until {
        if ($workflowPanelResults.Current.ItemStatus -like 'mode:compact-grid;page:1/*') {
            $workflowPanelResults.Current.ItemStatus
        }
    } 'The empty-context Super Panel did not expose the compact grid presentation.'
    $report.super_panel['compact_grid_mode'] = [string]$compactGridMode
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
    $panelSelectionPattern.Select()
    Wait-Until {
        $panelSelectionPattern.Current.IsSelected
    } 'The managed workflow Super Panel item could not be selected.' | Out-Null
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

    if (-not $SettingsNavigationOnly -and
        -not $PluginCommandManagementOnly -and
        -not $WorkflowOutputOnly) {
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

    if (-not $SettingsNavigationOnly -and
        -not $PluginCommandManagementOnly -and
        -not $WorkflowSchemaOnly) {
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

    if (-not $SettingsNavigationOnly -and
        -not $PluginCommandManagementOnly -and
        -not $WorkflowOnly -and
        -not $WorkflowOutputOnly -and
        -not $WorkflowSchemaOnly) {
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
    Wait-Until {
        $null -eq (Find-DescendantByAutomationId `
            $mainWindow 'Long.Workspace.PluginRail')
    } 'The installed plugin rail remained visible in plugin runtime context.' |
        Out-Null
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
    Wait-Until {
        $null -eq (Find-DescendantByAutomationId `
            $mainWindow 'Long.Workspace.PluginRail')
    } 'The installed plugin rail reappeared after returning to plugin runtime.' |
        Out-Null
    $report.plugin_lifecycle = [ordered]@{
        main_window_discovered = $true
        workspace_runtime_discovered = $null -ne $workspaceRuntime
        plugin_rail_hidden_in_runtime = $true
        detach_invoked = $true
        detached_window_discovered = $true
        detached_back_discovered = $null -ne $detachedBack
        escape_closed_detached_window = $true
        workspace_runtime_restored = $null -ne $restoredRuntime
        plugin_rail_hidden_after_restore = $true
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
    Stop-QualityHost $contextMatrixProcess
    Stop-QualityHost $focusEscapeProcess
    Stop-QualityHost $focusExpandProcess
    Stop-QualityHost $focusExecuteProcess
    Stop-QualityHost $focusProbeProcess
    Stop-QualityHost $superPanelTransitionProcess
    Stop-QualityHost $launcherWorkspaceProcess
    Stop-QualityHost $paletteWorkspaceProcess
    Stop-QualityHost $panelModuleCloseProcess
    Stop-QualityHost $paletteModuleCloseProcess
    Stop-QualityHost $managementProcess
    Stop-QualityHost $pluginSettingsProcess
    Stop-QualityHost $workflowPaletteProcess
    Stop-QualityHost $workflowPanelProcess
    Stop-QualityHost $workflowSchemaWideProcess
    Stop-QualityHost $workflowSchemaCompactProcess
    Stop-QualityHost $workflowOutputProcess
    Stop-QualityHost $pluginProcess
    Stop-QualityHost $marketProcess
    Stop-QualityHost $accessibilityProcess
    if ($automationEventCaptureActive) {
        [LongDesktopInput]::StopAutomationEventCapture() | Out-Null
        $automationEventCaptureActive = $false
    }
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
