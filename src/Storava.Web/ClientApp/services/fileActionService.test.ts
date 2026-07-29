import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ScanItem, ScanSession } from '@/models/scan';

const databaseMocks = vi.hoisted(() => ({
  getDirectoryHandle: vi.fn(),
  removeItemTree: vi.fn(),
}));

vi.mock('@/services/scanDatabase', () => databaseMocks);

import { deleteLocalItem, deleteLocalItems, readLocalFile } from '@/services/fileActionService';

const session: ScanSession = {
  id: 'native-session',
  rootName: 'Storage',
  source: 'native',
  status: 'completed',
  createdAt: 1,
  updatedAt: 2,
  completedAt: 2,
  metrics: { bytes: 42, files: 1, folders: 1, errors: 0, elapsedMs: 1, itemsPerSecond: 1, currentPath: '' },
  categories: [{ category: 'archives', bytes: 42, count: 1 }],
  topItems: [],
  schemaVersion: 1,
};

const item: ScanItem = {
  id: 'native-session:cache/archive.zip',
  sessionId: 'native-session',
  parentPath: 'cache',
  relativePath: 'cache/archive.zip',
  name: 'archive.zip',
  kind: 'file',
  size: 42,
  modifiedAt: 1,
  extension: 'zip',
  category: 'archives',
  depth: 1,
  ruleIds: ['archive'],
  risk: 'low',
};

describe('local file actions', () => {
  beforeEach(() => vi.clearAllMocks());

  it('requests write permission and deletes only the explicitly selected relative entry', async () => {
    const parent = {
      removeEntry: vi.fn().mockResolvedValue(undefined),
    };
    const root = {
      queryPermission: vi.fn().mockResolvedValue('prompt'),
      requestPermission: vi.fn().mockResolvedValue('granted'),
      getDirectoryHandle: vi.fn().mockResolvedValue(parent),
    };
    databaseMocks.getDirectoryHandle.mockResolvedValue(root);
    databaseMocks.removeItemTree.mockResolvedValue({
      session: { ...session, metrics: { ...session.metrics, bytes: 0, files: 0 } },
      deletedFiles: 1,
      deletedFolders: 0,
      freedBytes: 42,
    });

    const result = await deleteLocalItem(session, item);

    expect(root.requestPermission).toHaveBeenCalledWith({ mode: 'readwrite' });
    expect(root.getDirectoryHandle).toHaveBeenCalledWith('cache');
    expect(parent.removeEntry).toHaveBeenCalledWith('archive.zip', { recursive: false });
    expect(databaseMocks.removeItemTree).toHaveBeenCalledWith(session.id, item.relativePath);
    expect(result.freedBytes).toBe(42);
  });

  it('opens a local file with read permission without sending it anywhere', async () => {
    const file = new File(['local'], 'archive.zip');
    const parent = {
      getFileHandle: vi.fn().mockResolvedValue({ getFile: vi.fn().mockResolvedValue(file) }),
    };
    const root = {
      queryPermission: vi.fn().mockResolvedValue('granted'),
      requestPermission: vi.fn(),
      getDirectoryHandle: vi.fn().mockResolvedValue(parent),
    };
    databaseMocks.getDirectoryHandle.mockResolvedValue(root);

    expect(await readLocalFile(session, item)).toBe(file);
    expect(root.requestPermission).not.toHaveBeenCalled();
  });

  it('keeps fallback and imported scans read-only', async () => {
    await expect(deleteLocalItem({ ...session, source: 'fallback' }, item))
      .rejects.toMatchObject({ reason: 'unsupported-source' });
    expect(databaseMocks.getDirectoryHandle).not.toHaveBeenCalled();
  });
});

/**
 * Removing several items under one approval.
 *
 * What matters is that a run is honest about itself: nothing stops halfway, every item reports its
 * own outcome, and a folder is removed after the things inside it rather than before — otherwise
 * its children disappear mid-run and come back as failures that never actually happened.
 */
describe('removing several items at once', () => {
  beforeEach(() => vi.clearAllMocks());

  /**
   * A directory handle that answers as any depth of folder.
   *
   * It returns itself from getDirectoryHandle, so walking down a path of any length lands on
   * something that can still remove an entry. A fake that only stood in for one level made a
   * nested path throw before it ever reached the removal.
   */
  function grantedRoot(removeEntry: ReturnType<typeof vi.fn>) {
    const handle: Record<string, unknown> = {
      queryPermission: vi.fn().mockResolvedValue('granted'),
      requestPermission: vi.fn().mockResolvedValue('granted'),
      removeEntry,
    };
    handle.getDirectoryHandle = vi.fn().mockResolvedValue(handle);
    return handle;
  }

  function at(relativePath: string, overrides: Partial<ScanItem> = {}): ScanItem {
    const parts = relativePath.split('/');
    return {
      ...item,
      id: `native-session:${relativePath}`,
      relativePath,
      parentPath: parts.slice(0, -1).join('/'),
      name: parts.at(-1)!,
      ...overrides,
    };
  }

  it('reports every item, and totals what was freed', async () => {
    const removeEntry = vi.fn().mockResolvedValue(undefined);
    databaseMocks.getDirectoryHandle.mockResolvedValue(grantedRoot(removeEntry));
    databaseMocks.removeItemTree.mockResolvedValue({
      session, deletedFiles: 1, deletedFolders: 0, freedBytes: 42,
    });

    const result = await deleteLocalItems(session, [at('cache/a.zip'), at('cache/b.zip')]);

    expect(result.succeededCount).toBe(2);
    expect(result.failedCount).toBe(0);
    expect(result.freedBytes).toBe(84);
    expect(result.outcomes.map((outcome) => outcome.relativePath).sort())
      .toEqual(['cache/a.zip', 'cache/b.zip']);
  });

  /** One that has already been deleted by hand must not take the others down with it. */
  it('carries on past a failure and names the one that failed', async () => {
    const removeEntry = vi.fn()
      .mockRejectedValueOnce(new DOMException('gone', 'NotFoundError'))
      .mockResolvedValue(undefined);

    databaseMocks.getDirectoryHandle.mockResolvedValue(grantedRoot(removeEntry));
    databaseMocks.removeItemTree.mockResolvedValue({
      session, deletedFiles: 1, deletedFolders: 0, freedBytes: 10,
    });

    const result = await deleteLocalItems(session, [at('cache/gone.zip'), at('cache/here.zip')]);

    expect(result.succeededCount).toBe(1);
    expect(result.failedCount).toBe(1);

    const failed = result.outcomes.find((outcome) => !outcome.succeeded)!;
    expect(failed.relativePath).toBe('cache/gone.zip');
    expect(failed.reason).toBe('not-found');
  });

  /**
   * Deepest first. A folder removed before its contents takes them with it, and every one of them
   * would then report as missing — failures the user never caused and cannot act on.
   */
  it('removes what is inside a folder before the folder itself', async () => {
    const order: string[] = [];
    const removeEntry = vi.fn((name: string) => {
      order.push(name);
      return Promise.resolve();
    });

    databaseMocks.getDirectoryHandle.mockResolvedValue(grantedRoot(removeEntry));
    databaseMocks.removeItemTree.mockResolvedValue({
      session, deletedFiles: 1, deletedFolders: 0, freedBytes: 1,
    });

    await deleteLocalItems(session, [
      at('cache', { kind: 'folder' }),
      at('cache/deep/inner.zip'),
    ]);

    expect(order).toEqual(['inner.zip', 'cache']);
  });

  it('reports progress as it goes', async () => {
    databaseMocks.getDirectoryHandle.mockResolvedValue(grantedRoot(vi.fn().mockResolvedValue(undefined)));
    databaseMocks.removeItemTree.mockResolvedValue({
      session, deletedFiles: 1, deletedFolders: 0, freedBytes: 1,
    });

    const seen: Array<[number, number]> = [];
    await deleteLocalItems(session, [at('a.zip'), at('b.zip'), at('c.zip')], (done, total) => {
      seen.push([done, total]);
    });

    expect(seen).toEqual([[1, 3], [2, 3], [3, 3]]);
  });

  it('does nothing at all when nothing was selected', async () => {
    const removeEntry = vi.fn();
    databaseMocks.getDirectoryHandle.mockResolvedValue(grantedRoot(removeEntry));

    const result = await deleteLocalItems(session, []);

    expect(result.outcomes).toEqual([]);
    expect(removeEntry).not.toHaveBeenCalled();
  });
});
