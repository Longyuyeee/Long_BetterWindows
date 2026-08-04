export type LongApiVersion = "1.1.0";
export type LongUiKitVersion = "1.1.0";

export interface LongUiStateOptions {
  kind?: "empty" | "loading" | "error";
  title?: string;
  detail?: string;
  actionLabel?: string;
  onAction?: () => void;
}

export interface LongUiKit {
  readonly version: LongUiKitVersion;
  setTheme(theme: "light" | "dark"): void;
  setHighContrast(enabled: boolean): void;
  setReducedMotion(reduced: boolean): void;
  setBusy(element: HTMLElement | null, busy: boolean): void;
  announce(message: string): void;
  clearState(container: HTMLElement | null): void;
  renderState(
    container: HTMLElement | null,
    options?: LongUiStateOptions
  ): HTMLElement | null;
  onCommand(handler: (command: unknown) => void | Promise<void>): () => void;
  commandText(command: unknown): string;
  commandPaths(command: unknown): string[];
}

export interface LongResult<T = never> {
  success: boolean;
  data?: T;
  error?: string | null;
}

export interface LongFileResult extends LongResult {
  filePath?: string;
  size?: number;
}

export interface LongShellFile {
  name: string;
  path: string;
}

export interface LongFileListResult extends LongResult {
  files?: LongShellFile[];
}

export interface LongScreenRect {
  X: number;
  Y: number;
  Width: number;
  Height: number;
}

export interface LongProcessInfo {
  Id: number;
  Name: string;
  MainWindowTitle: string;
}

export interface LongMemoryInfo {
  TotalPhysicalMemory: number;
  AvailablePhysicalMemory: number;
  UsedPhysicalMemory: number;
  UsagePercentage: number;
}

export interface LongDiskInfo {
  Name: string;
  DriveType: string;
  TotalSize: number;
  FreeSpace: number;
  UsedSpace: number;
  UsagePercentage: number;
}

export interface LongSystemInfo {
  OsName: string;
  OsVersion: string;
  MachineName: string;
  ProcessorName: string;
  ProcessorCount: number;
  TotalRam: number;
  UserName: string;
  Uptime: string;
}

export interface LongProcessResourceInfo {
  ProcessId: number;
  ProcessName: string;
  CpuUsage: number;
  MemoryUsage: number;
  ThreadCount: number;
}

export interface LongPortInfo {
  LocalPort: number;
  LocalAddress: string;
  RemotePort: number;
  RemoteAddress: string;
  Protocol: string;
  State: string;
  ProcessId: number;
  ProcessName: string;
  ProcessPath: string;
  ProcessIdentity: string;
}

export interface LongPortSummary {
  TotalTcpConnections: number;
  TotalTcpListeners: number;
  TotalUdpEndpoints: number;
  CommonPorts: number[];
  ProcessPortCount: Record<string, number>;
}

export interface LongNetworkStats {
  TotalBytesSent: number;
  TotalBytesReceived: number;
  Timestamp: string;
}

export interface LongNetworkSpeed {
  UploadSpeed: number;
  DownloadSpeed: number;
  Timestamp: string;
}

export interface LongNetworkInterface {
  Name: string;
  Description: string;
  Type: string;
  Status: string;
  Speed: number;
  MacAddress: string;
}

export interface LongFileItem {
  FullPath: string;
  Name: string;
  Size: number;
  CreatedTime: string;
  ModifiedTime: string;
  Extension: string;
}

export interface LongFileMetadata {
  FullPath: string;
  Size: number;
  CreatedTime: string;
  ModifiedTime: string;
  AccessedTime: string;
  Extension: string;
  IsReadOnly: boolean;
  IsHidden: boolean;
  Hash: string;
}

export interface LongDuplicateFileGroup {
  Hash: string;
  Size: number;
  FilePaths: string[];
}

export interface LongRenameOperation {
  OldPath: string;
  NewName: string;
}

export interface LongSearchResult {
  FilePath: string;
  LineNumber: number;
  MatchedLine: string;
  Context: string;
}

export interface LongFileOrganizationItem {
  SourcePath: string;
  DestinationPath: string;
  Category: string;
  Name: string;
  Size: number;
  HasConflict: boolean;
}

export interface LongFileOrganizationFailure {
  SourcePath: string;
  DestinationPath: string;
  Detail: string;
}

export interface LongFileOrganizationResult {
  PlannedCount: number;
  MovedCount: number;
  Failures: LongFileOrganizationFailure[];
  FailedCount: number;
}

export type LongClassifyMode = "ByExtension" | "ByDate" | "BySize";

export interface LongScheduleTask {
  Id?: string;
  Name: string;
  Description?: string;
  ActionType: "command" | "script" | "notification" | string;
  ActionData: string;
  TriggerType: "once" | "daily" | "weekly" | "interval" | string;
  TriggerTime?: string | null;
  IntervalMinutes?: number | null;
  Enabled: boolean;
  CreatedAt?: string;
  LastRunAt?: string | null;
  NextRunAt?: string | null;
}

export interface LongWindowInfo {
  Title: string;
  ProcessName: string;
  X: number;
  Y: number;
  Width: number;
  Height: number;
  IsTopmost: boolean;
  DisplayState: "Normal" | "Minimized" | "Maximized" | number;
}

export interface LongAudioDevice {
  Id: string;
  Name: string;
  Type: string;
  IsDefault: boolean;
}

export interface LongPowerStatus {
  ACLineStatus: number;
  BatteryFlag: number;
  BatteryLifePercent: number;
  BatteryLifeTime: number;
  BatteryFullLifeTime: number;
}

export type LongSystemTheme = "Light" | "Dark" | "Auto" | 0 | 1 | 2;
export type LongWallpaperStyle =
  | "Center" | "Stretch" | "Fit" | "Fill" | "Span" | "Tile"
  | 0 | 1 | 2 | 3 | 4 | 5;

export interface LongClipboardChangedEvent {
  type: "clipboard.changed";
  content_type: "text" | "image" | "files" | "unknown";
  text?: string | null;
  timestamp: string;
}

export interface LongLanguageChangedMessage {
  type: "long.language-changed";
  requested_language: string;
  resolved_language: string;
  resources: Record<string, string>;
}

export interface LongCommandInvocation {
  command_id: string;
  input?: string | null;
  arguments?: Record<string, string>;
}

export interface LongCommandMessage {
  type: "long.command";
  request_id: string;
  command: LongCommandInvocation;
}

export type LongHostSurface = "plugin" | "action-card" | "widget";
export type LongHostId = "long-assistant" | "long-grid" | string;

export interface LongHostInfo {
  protocol_version: "1.0";
  api_version: LongApiVersion;
  host: {
    id: LongHostId;
    version: string;
  };
  plugin_id: string;
  surface: LongHostSurface;
  widget_id?: string | null;
  instance_id?: string | null;
  surfaces: LongHostSurface[];
  features: string[];
  limits: {
    instance_state_bytes: number;
    bridge_message_bytes: number;
  };
}

export interface LongWidgetEventEnvelope<TPayload = Record<string, unknown>> {
  protocol_version: "1.0";
  plugin_id: string;
  widget_id: string;
  instance_id: string;
  sequence: number;
  payload: TPayload;
}

export type LongWidgetEventType =
  | "long.widget-mounted"
  | "long.widget-visibility-changed"
  | "long.widget-resized"
  | "long.widget-theme-changed"
  | "long.widget-locale-changed"
  | "long.widget-settings-changed"
  | "long.widget-suspend"
  | "long.widget-resume"
  | "long.widget-unmount";

export interface LongWidgetSizePayload {
  width: number;
  height: number;
  columns: number;
  rows: number;
  scale: number;
}

export interface LongWidgetVisibilityPayload {
  visible: boolean;
  reason?: string;
}

export interface LongWidgetHostMessage<TPayload = Record<string, unknown>> {
  type: LongWidgetEventType;
  detail: LongWidgetEventEnvelope<TPayload>;
}

export type LongHostMessage =
  | LongClipboardChangedEvent
  | LongLanguageChangedMessage
  | LongCommandMessage
  | LongWidgetHostMessage
  | { type: "hotkey"; hotkey: string };

export interface LongHostApi {
  getInfo(): Promise<LongHostInfo>;
}

export interface LongAppApi {
  openUrl(url: string): Promise<LongResult>;
  openFolder(path: string): Promise<LongResult>;
  openWithDefault(path: string): Promise<LongResult>;
  showNotification(title: string, body: string): Promise<LongResult>;
  getVersion(): Promise<LongResult<string>>;
  log(...values: unknown[]): Promise<LongResult>;
}

export interface LongClipboardApi {
  getText(): Promise<LongResult<string | null>>;
  setText(text: string): Promise<LongResult>;
  clear(): Promise<LongResult>;
  startMonitoring(
    callback: (event: LongClipboardChangedEvent) => void
  ): Promise<LongResult>;
  stopMonitoring(): Promise<LongResult>;
}

export interface LongShellApi {
  getActiveFolder(): Promise<LongResult<string>>;
  getSelectedItems(): Promise<LongResult<string[]>>;
  getItemScreenRect(): Promise<LongResult<LongScreenRect>>;
  listFiles(directory: string): Promise<LongFileListResult>;
  renameFile(oldPath: string, newName: string): Promise<LongResult>;
  renameBatch(operations: LongRenameOperation[]): Promise<LongResult<number>>;
  openUrl(url: string): Promise<LongResult>;
  openFolder(path: string): Promise<LongResult>;
  openWithDefault(path: string): Promise<LongResult>;
}

export interface LongAdsApi {
  read(path: string, streamName?: string): Promise<LongResult<string>>;
  write(path: string, content: string, streamName?: string): Promise<LongResult>;
  delete(path: string, streamName?: string): Promise<LongResult>;
  exists(path: string, streamName?: string): Promise<LongResult<boolean>>;
  isNTFS(path: string): Promise<LongResult<boolean>>;
}

export interface LongHotkeyApi {
  register(hotkey: string, callback: () => void): Promise<LongResult>;
  unregister(hotkey: string): Promise<LongResult>;
  isConflict(hotkey: string): Promise<LongResult<boolean>>;
}

export interface LongRegistryApi {
  read(key: string, valueName: string): Promise<LongResult<string | null>>;
  write(key: string, valueName: string, value: string): Promise<LongResult>;
  delete(key: string, valueName: string): Promise<LongResult>;
}

export interface LongStorageApi {
  get<T = string | null>(key: string): Promise<LongResult<T>>;
  set(key: string, value: string): Promise<LongResult>;
  compareExchange(
    key: string,
    expectedValue: string | null,
    value: string
  ): Promise<LongResult<boolean>>;
  delete(key: string): Promise<LongResult>;
  containsKey(key: string): Promise<LongResult<boolean>>;
}

export interface LongProcessApi {
  start(path: string, args?: string): Promise<LongResult>;
  getList(filter?: string): Promise<LongResult<LongProcessInfo[]>>;
  kill(processId: number): Promise<LongResult>;
  killVerified(
    processId: number,
    expectedName: string,
    expectedIdentity: string
  ): Promise<LongResult>;
}

export interface LongFileOpsApi {
  copy(source: string, destination: string): Promise<LongResult>;
  move(source: string, destination: string): Promise<LongResult>;
  delete(path: string): Promise<LongResult>;
  exists(path: string): Promise<LongResult<boolean>>;
}

export interface LongPerformanceApi {
  getCpuUsage(): Promise<LongResult<number>>;
  getMemoryInfo(): Promise<LongResult<LongMemoryInfo>>;
  getDiskInfo(): Promise<LongResult<LongDiskInfo[]>>;
  getSystemInfo(): Promise<LongResult<LongSystemInfo>>;
  getTopByCpu(count?: number): Promise<LongResult<LongProcessResourceInfo[]>>;
  getTopByMemory(count?: number): Promise<LongResult<LongProcessResourceInfo[]>>;
}

export interface LongNetworkPortApi {
  getTcpConnections(): Promise<LongResult<LongPortInfo[]>>;
  getTcpListeners(): Promise<LongResult<LongPortInfo[]>>;
  getUdpEndpoints(): Promise<LongResult<LongPortInfo[]>>;
  findPortOwner(
    port: number,
    protocol?: "tcp" | "udp"
  ): Promise<LongResult<LongPortInfo | null>>;
  isPortInUse(port: number, protocol?: "tcp" | "udp"): Promise<LongResult<boolean>>;
  getSummary(): Promise<LongResult<LongPortSummary>>;
}

export interface LongNetworkApi {
  getStats(): Promise<LongResult<LongNetworkStats>>;
  getSpeed(): Promise<LongResult<LongNetworkSpeed>>;
  getInterfaces(): Promise<LongResult<LongNetworkInterface[]>>;
}

export interface LongAudioApi {
  getVolume(): Promise<LongResult<number>>;
  setVolume(volume: number): Promise<LongResult>;
  getMute(): Promise<LongResult<boolean>>;
  setMute(mute: boolean): Promise<LongResult>;
  increase(step?: number): Promise<LongResult<number>>;
  decrease(step?: number): Promise<LongResult<number>>;
  getDevices(): Promise<LongResult<LongAudioDevice[]>>;
  setDefaultDevice(deviceId: string): Promise<LongResult>;
}

export interface LongPowerApi {
  getStatus(): Promise<LongResult<LongPowerStatus>>;
  getBatteryStatus(): Promise<LongResult<LongPowerStatus>>;
  lock(): Promise<LongResult>;
  sleep(): Promise<LongResult>;
  hibernate(): Promise<LongResult>;
  shutdown(delaySeconds?: number): Promise<LongResult>;
  reboot(delaySeconds?: number): Promise<LongResult>;
  preventSleep(prevent: boolean): Promise<LongResult>;
}

export interface LongThemeApi {
  get(): Promise<LongResult<LongSystemTheme>>;
  set(theme: LongSystemTheme): Promise<LongResult>;
  toggle(): Promise<LongResult>;
  getAccentColor(): Promise<LongResult<string>>;
  setAccentColor(color: string): Promise<LongResult>;
}

export interface LongWallpaperApi {
  get(): Promise<LongResult<string>>;
  set(path: string, style?: LongWallpaperStyle): Promise<LongResult>;
  getStyle(): Promise<LongResult<LongWallpaperStyle>>;
}

export interface LongBrightnessApi {
  get(): Promise<LongResult<number>>;
  set(value: number): Promise<LongResult>;
  increase(step?: number): Promise<LongResult<number>>;
  decrease(step?: number): Promise<LongResult<number>>;
}

export interface LongPinyinApi {
  get(text: string): Promise<LongResult<string>>;
  getInitials(text: string): Promise<LongResult<string>>;
  match(text: string, query: string): Promise<LongResult<boolean>>;
  filter(items: string[], query: string): Promise<LongResult<string[]>>;
}

export interface LongInputApi {
  keyPress(virtualKeyCode: number): Promise<LongResult>;
  mouseClick(x: number, y: number, rightButton?: boolean): Promise<LongResult>;
  moveCursor(x: number, y: number): Promise<LongResult>;
}

export interface LongFileSystemApi {
  enumerate(path: string, pattern?: string, recursive?: boolean): Promise<LongResult<LongFileItem[]>>;
  hash(path: string): Promise<LongResult<string>>;
  metadata(path: string): Promise<LongResult<LongFileMetadata>>;
  findDuplicates(path: string): Promise<LongResult<LongDuplicateFileGroup[]>>;
  batchRename(operations: LongRenameOperation[]): Promise<LongResult<number>>;
  classify(path: string, mode?: LongClassifyMode): Promise<LongResult<Record<string, string[]>>>;
  findLarge(path: string, minSizeBytes: number): Promise<LongResult<LongFileItem[]>>;
  searchContent(path: string, keyword: string, extensions?: string[]): Promise<LongResult<LongSearchResult[]>>;
  planOrganization(path: string, mode?: LongClassifyMode): Promise<LongResult<LongFileOrganizationItem[]>>;
  executeOrganization(
    path: string,
    mode: LongClassifyMode | undefined,
    items: LongFileOrganizationItem[]
  ): Promise<LongResult<LongFileOrganizationResult>>;
}

export interface LongCacheApi {
  cleanTemp(): Promise<LongResult>;
  cleanWindowsUpdate(): Promise<LongResult>;
  cleanBrowser(browser: string): Promise<LongResult>;
  emptyRecycleBin(): Promise<LongResult>;
  getStatistics(): Promise<LongResult<Record<string, unknown>>>;
  cleanAll(): Promise<LongResult>;
}

export interface LongScheduleApi {
  create(task: LongScheduleTask): Promise<LongResult<string>>;
  delete(taskId: string): Promise<LongResult>;
  getAll(): Promise<LongResult<LongScheduleTask[]>>;
  setEnabled(taskId: string, enabled: boolean): Promise<LongResult>;
  runNow(taskId: string): Promise<LongResult>;
}

export interface LongUiApi {
  showToast(message: string): Promise<LongResult>;
  createWindow(
    title: string,
    htmlContent: string,
    width?: number,
    height?: number,
    resizable?: boolean
  ): Promise<LongResult<string>>;
  confirm(message: string, title?: string): Promise<LongResult<boolean>>;
  prompt(message: string, title?: string, defaultValue?: string): Promise<LongResult<string | null>>;
  select(message: string, options: string[], title?: string): Promise<LongResult<number>>;
  closeWindow(windowId: string): Promise<LongResult>;
  sendMessage(windowId: string, message: string): Promise<LongResult>;
}

export interface LongScreenshotApi {
  captureFull(): Promise<LongResult<unknown>>;
  captureRegion(x: number, y: number, width: number, height: number): Promise<LongFileResult>;
}

export interface LongHttpApi {
  get(url: string, headers?: Record<string, string>): Promise<LongResult<string>>;
  post(
    url: string,
    body: string,
    contentType?: string,
    headers?: Record<string, string>
  ): Promise<LongResult<string>>;
  download(url: string): Promise<LongFileResult>;
}

export interface LongWindowApi {
  getForeground(): Promise<LongWindowInfo | LongResult>;
  getVisible(): Promise<LongResult<LongWindowInfo[]>>;
}

export interface LongWidgetApi {
  ready(contentVersion?: number): Promise<LongResult>;
  getInstanceState<T = unknown>(): Promise<LongResult<T | null>>;
  setInstanceState<T = unknown>(state: T): Promise<LongResult>;
  openSettings(): Promise<LongResult>;
  invalidate(reason?: string): Promise<LongResult>;
  setBadge(badge: {
    text?: string;
    status?: "normal" | "success" | "warning" | "danger" | "info" | string;
  }): Promise<LongResult>;
}

export interface LongApi {
  app: LongAppApi;
  host: LongHostApi;
  clipboard: LongClipboardApi;
  shell: LongShellApi;
  fs: { ads: LongAdsApi };
  hotkey: LongHotkeyApi;
  registry: LongRegistryApi;
  storage: LongStorageApi;
  process: LongProcessApi;
  fileOps: LongFileOpsApi;
  performance: LongPerformanceApi;
  networkPort: LongNetworkPortApi;
  network: LongNetworkApi;
  audio: LongAudioApi;
  power: LongPowerApi;
  theme: LongThemeApi;
  wallpaper: LongWallpaperApi;
  brightness: LongBrightnessApi;
  pinyin: LongPinyinApi;
  input: LongInputApi;
  fileSystem: LongFileSystemApi;
  cache: LongCacheApi;
  schedule: LongScheduleApi;
  ui: LongUiApi;
  screenshot: LongScreenshotApi;
  http: LongHttpApi;
  window: LongWindowApi;
  widget: LongWidgetApi;
}

export interface LongWebView {
  postMessage(message: unknown): void;
  addEventListener(
    type: "message",
    listener: (event: { data: LongHostMessage | unknown }) => void
  ): void;
  removeEventListener?(
    type: "message",
    listener: (event: { data: LongHostMessage | unknown }) => void
  ): void;
}

declare global {
  const long: LongApi;

  interface Window {
    long: LongApi;
    LongUI?: LongUiKit;
    chrome?: {
      webview?: LongWebView;
    };
  }

  interface WindowEventMap {
    "long.widget-mounted": CustomEvent<LongWidgetEventEnvelope>;
    "long.widget-visibility-changed": CustomEvent<LongWidgetEventEnvelope<LongWidgetVisibilityPayload>>;
    "long.widget-resized": CustomEvent<LongWidgetEventEnvelope<LongWidgetSizePayload>>;
    "long.widget-theme-changed": CustomEvent<LongWidgetEventEnvelope>;
    "long.widget-locale-changed": CustomEvent<LongWidgetEventEnvelope>;
    "long.widget-settings-changed": CustomEvent<LongWidgetEventEnvelope>;
    "long.widget-suspend": CustomEvent<LongWidgetEventEnvelope>;
    "long.widget-resume": CustomEvent<LongWidgetEventEnvelope>;
    "long.widget-unmount": CustomEvent<LongWidgetEventEnvelope>;
  }
}
