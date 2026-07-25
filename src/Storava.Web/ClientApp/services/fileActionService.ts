import type { ItemRemovalResult, ScanItem, ScanSession } from '@/models/scan';
import { getDirectoryHandle, removeItemTree } from '@/services/scanDatabase';

export type FileActionFailure =
  | 'unsupported-source'
  | 'missing-handle'
  | 'permission-denied'
  | 'invalid-path'
  | 'not-found';

export class FileActionError extends Error {
  public constructor(public readonly reason: FileActionFailure, message: string, options?: ErrorOptions) {
    super(message, options);
    this.name = 'FileActionError';
  }
}

function pathParts(item: ScanItem): string[] {
  const parts = item.relativePath.split('/').filter(Boolean);
  if (parts.length === 0 || parts.some((part) => part === '.' || part === '..')) {
    throw new FileActionError('invalid-path', 'The selected root cannot be opened or deleted.');
  }
  return parts;
}

async function authorizedRoot(session: ScanSession, mode: 'read' | 'readwrite'): Promise<FileSystemDirectoryHandle> {
  if (session.source !== 'native') {
    throw new FileActionError(
      'unsupported-source',
      'This scan does not include a reusable native folder permission.',
    );
  }
  const root = await getDirectoryHandle(session.id);
  if (!root) {
    throw new FileActionError('missing-handle', 'The saved folder permission is no longer available.');
  }
  const currentPermission = await root.queryPermission?.({ mode });
  const permission = currentPermission === 'granted'
    ? currentPermission
    : await root.requestPermission({ mode });
  if (permission !== 'granted') {
    throw new FileActionError('permission-denied', 'The browser did not grant the requested folder permission.');
  }
  return root;
}

async function resolveParent(
  root: FileSystemDirectoryHandle,
  parts: string[],
): Promise<{ parent: FileSystemDirectoryHandle; name: string }> {
  const name = parts.at(-1);
  if (!name) throw new FileActionError('invalid-path', 'The item path is invalid.');
  let parent = root;
  try {
    for (const segment of parts.slice(0, -1)) {
      parent = await parent.getDirectoryHandle(segment);
    }
  } catch (error) {
    throw new FileActionError('not-found', 'The item no longer exists in the selected folder.', { cause: error });
  }
  return { parent, name };
}

export async function readLocalFile(session: ScanSession, item: ScanItem): Promise<File> {
  if (item.kind !== 'file') throw new FileActionError('invalid-path', 'Only files can be opened.');
  const root = await authorizedRoot(session, 'read');
  const resolved = await resolveParent(root, pathParts(item));
  try {
    return await (await resolved.parent.getFileHandle(resolved.name)).getFile();
  } catch (error) {
    throw new FileActionError('not-found', 'The file no longer exists in the selected folder.', { cause: error });
  }
}

export async function deleteLocalItem(session: ScanSession, item: ScanItem): Promise<ItemRemovalResult> {
  const root = await authorizedRoot(session, 'readwrite');
  const resolved = await resolveParent(root, pathParts(item));
  try {
    await resolved.parent.removeEntry(resolved.name, { recursive: item.kind === 'folder' });
  } catch (error) {
    if (error instanceof DOMException && (error.name === 'NotFoundError' || error.name === 'TypeMismatchError')) {
      throw new FileActionError('not-found', 'The item no longer exists in the selected folder.', { cause: error });
    }
    throw error;
  }
  return removeItemTree(session.id, item.relativePath);
}
