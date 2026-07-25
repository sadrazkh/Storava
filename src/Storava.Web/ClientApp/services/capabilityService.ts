import type { BrowserCapabilities } from '@/models/capabilities';

export function detectCapabilities(): BrowserCapabilities {
  const input = document.createElement('input');
  const nativeDirectoryPicker = typeof window.showDirectoryPicker === 'function';
  const directoryInputFallback = 'webkitdirectory' in input;
  const indexedDb = 'indexedDB' in window;
  const serviceWorker = 'serviceWorker' in navigator;
  const webWorker = 'Worker' in window;
  const secureContext = window.isSecureContext || location.hostname === 'localhost' || location.hostname === '127.0.0.1';

  const essentials = indexedDb && webWorker && secureContext;
  const mode = essentials && nativeDirectoryPicker
    ? 'native'
    : essentials && directoryInputFallback
      ? 'fallback'
      : 'unsupported';

  return {
    nativeDirectoryPicker,
    directoryInputFallback,
    indexedDb,
    serviceWorker,
    webWorker,
    secureContext,
    mode,
  };
}
