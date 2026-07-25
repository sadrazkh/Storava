/// <reference types="vite/client" />

declare module '*.vue' {
  import type { DefineComponent } from 'vue';
  const component: DefineComponent;
  export default component;
}

interface FileSystemDirectoryHandle {
  readonly kind: 'directory';
  readonly name: string;
  requestPermission(options?: { mode?: 'read' | 'readwrite' }): Promise<PermissionState>;
}

interface Window {
  showDirectoryPicker?: (options?: {
    id?: string;
    mode?: 'read' | 'readwrite';
    startIn?: string;
  }) => Promise<FileSystemDirectoryHandle>;
}
