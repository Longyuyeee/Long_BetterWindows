import type {
  LongApi,
  LongClipboardChangedEvent,
  LongResult
} from "../index.js";

export type LongMockHandler =
  (...args: unknown[]) => unknown | Promise<unknown>;

export interface LongMockCall {
  method: string;
  args: readonly unknown[];
}

export interface LongMockOptions {
  version?: string;
  clipboardText?: string | null;
  storage?: Record<string, string>;
  handlers?: Record<string, LongMockHandler>;
}

export interface LongMockController {
  readonly long: LongApi;
  readonly calls: readonly LongMockCall[];
  getCalls(method?: string): LongMockCall[];
  setHandler(method: string, handler: LongMockHandler): void;
  clearHandler(method: string): void;
  reset(): void;
  emitHotkey(hotkey: string): boolean;
  emitClipboardChanged(
    event?: Partial<LongClipboardChangedEvent>
  ): boolean;
  install(target?: { long?: LongApi }): LongApi;
}

export const BRIDGE_METHODS: readonly string[];

export function createLongMock(options?: LongMockOptions): LongMockController;

export function installLongMock(
  options?: LongMockOptions,
  target?: { long?: LongApi }
): LongMockController;

export function ok<T>(data?: T): LongResult<T>;

export function fail(error: string): LongResult;
