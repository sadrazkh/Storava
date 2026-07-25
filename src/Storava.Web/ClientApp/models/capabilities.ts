export interface BrowserCapabilities {
  nativeDirectoryPicker: boolean;
  directoryInputFallback: boolean;
  indexedDb: boolean;
  serviceWorker: boolean;
  webWorker: boolean;
  secureContext: boolean;
  mode: 'native' | 'fallback' | 'unsupported';
}

export interface FolderSelection {
  name: string;
  method: 'native' | 'fallback';
  itemCount?: number;
}
