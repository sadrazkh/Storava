import type { BrowserCapabilities, FolderSelection } from '@/models/capabilities';

export class FolderSelectionCancelledError extends Error {}

export async function selectFolder(capabilities: BrowserCapabilities): Promise<FolderSelection> {
  if (capabilities.nativeDirectoryPicker && window.showDirectoryPicker) {
    try {
      const handle = await window.showDirectoryPicker({
        id: 'storava-scan-root',
        mode: 'read',
      });
      return { name: handle.name, method: 'native' };
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        throw new FolderSelectionCancelledError();
      }
      throw error;
    }
  }

  if (!capabilities.directoryInputFallback) {
    throw new Error('Directory selection is not supported.');
  }

  return new Promise<FolderSelection>((resolve, reject) => {
    const input = document.createElement('input');
    input.type = 'file';
    input.multiple = true;
    input.setAttribute('webkitdirectory', '');
    input.setAttribute('directory', '');
    input.hidden = true;

    input.addEventListener('change', () => {
      const files = input.files;
      input.remove();
      if (!files || files.length === 0) {
        reject(new FolderSelectionCancelledError());
        return;
      }

      const relativePath = files[0]?.webkitRelativePath ?? '';
      const name = relativePath.split('/')[0] || 'Selected folder';
      resolve({ name, method: 'fallback', itemCount: files.length });
    }, { once: true });

    window.addEventListener('focus', () => {
      setTimeout(() => {
        if (document.body.contains(input) && (!input.files || input.files.length === 0)) {
          input.remove();
          reject(new FolderSelectionCancelledError());
        }
      }, 500);
    }, { once: true });

    document.body.append(input);
    input.click();
  });
}
