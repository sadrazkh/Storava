import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ScanItem, ScanSession } from '@/models/scan';

const databaseMocks = vi.hoisted(() => ({
  getDirectoryHandle: vi.fn(),
  removeItemTree: vi.fn(),
}));

vi.mock('@/services/scanDatabase', () => databaseMocks);

import { deleteLocalItem, readLocalFile } from '@/services/fileActionService';

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
