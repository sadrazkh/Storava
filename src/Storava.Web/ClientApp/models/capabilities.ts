export interface BrowserCapabilities {
  nativeDirectoryPicker: boolean;
  directoryInputFallback: boolean;
  indexedDb: boolean;
  serviceWorker: boolean;
  webWorker: boolean;
  secureContext: boolean;
  mode: 'native' | 'fallback' | 'unsupported';
}

export interface NativeFolderSelection {
  name: string;
  method: 'native';
  handle: FileSystemDirectoryHandle;
}

export interface FallbackFolderSelection {
  name: string;
  method: 'fallback';
  files: File[];
  itemCount: number;
}

export type FolderSelection = NativeFolderSelection | FallbackFolderSelection;
