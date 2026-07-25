import { afterEach, describe, expect, it } from 'vitest';
import { detectCapabilities } from '@/services/capabilityService';

const originalPicker = window.showDirectoryPicker;
const originalIndexedDb = window.indexedDB;
const originalWorker = window.Worker;
const originalServiceWorker = navigator.serviceWorker;
const originalWebkitDirectory = Object.getOwnPropertyDescriptor(
  HTMLInputElement.prototype,
  'webkitdirectory',
);

afterEach(() => {
  Object.defineProperty(window, 'showDirectoryPicker', { configurable: true, value: originalPicker });
  Object.defineProperty(window, 'indexedDB', { configurable: true, value: originalIndexedDb });
  Object.defineProperty(window, 'Worker', { configurable: true, value: originalWorker });
  Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: originalServiceWorker });
  if (originalWebkitDirectory) {
    Object.defineProperty(HTMLInputElement.prototype, 'webkitdirectory', originalWebkitDirectory);
  } else {
    Reflect.deleteProperty(HTMLInputElement.prototype, 'webkitdirectory');
  }
});

describe('detectCapabilities', () => {
  it('selects native mode when the browser exposes the complete local surface', () => {
    Object.defineProperty(window, 'showDirectoryPicker', {
      configurable: true,
      value: () => Promise.resolve({}),
    });
    Object.defineProperty(window, 'indexedDB', { configurable: true, value: {} });
    Object.defineProperty(window, 'Worker', { configurable: true, value: class {} });

    const capabilities = detectCapabilities();

    expect(capabilities.mode).toBe('native');
    expect(capabilities.nativeDirectoryPicker).toBe(true);
    expect(capabilities.indexedDb).toBe(true);
  });

  it('selects fallback mode when directory input is available without native picker', () => {
    Object.defineProperty(window, 'showDirectoryPicker', { configurable: true, value: undefined });
    Object.defineProperty(window, 'indexedDB', { configurable: true, value: {} });
    Object.defineProperty(window, 'Worker', { configurable: true, value: class {} });
    Object.defineProperty(HTMLInputElement.prototype, 'webkitdirectory', {
      configurable: true,
      value: false,
    });

    const capabilities = detectCapabilities();

    expect(capabilities.mode).toBe('fallback');
    expect(capabilities.directoryInputFallback).toBe(true);
  });
});
