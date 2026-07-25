import { beforeEach, describe, expect, it } from 'vitest';
import type { ScanItem, ScanSession } from '@/models/scan';
import { clearAllLocalData, deleteSession, getSession, listSessions, putItemsBatch, putSession, queryItems } from '@/services/scanDatabase';

const session: ScanSession = {
  id: 'database-test',
  rootName: 'test-root',
  source: 'fallback',
  status: 'completed',
  createdAt: 1,
  updatedAt: 2,
  completedAt: 2,
  metrics: { bytes: 30, files: 2, folders: 0, errors: 0, elapsedMs: 10, itemsPerSecond: 200, currentPath: '' },
  categories: [{ category: 'other', bytes: 30, count: 2 }],
  topItems: [],
  schemaVersion: 1,
};

const items: ScanItem[] = [
  { id: 'database-test:a', sessionId: 'database-test', parentPath: '', relativePath: 'a.bin', name: 'a.bin', kind: 'file', size: 10, modifiedAt: 1, extension: 'bin', category: 'other', depth: 0, ruleIds: [], risk: 'none' },
  { id: 'database-test:b', sessionId: 'database-test', parentPath: '', relativePath: 'b.bin', name: 'b.bin', kind: 'file', size: 20, modifiedAt: 2, extension: 'bin', category: 'other', depth: 0, ruleIds: [], risk: 'none' },
];

describe('scan IndexedDB persistence', () => {
  beforeEach(async () => clearAllLocalData());

  it('stores sessions and queries item batches by session', async () => {
    await putSession(session);
    await putItemsBatch(items);
    expect((await getSession(session.id))?.rootName).toBe('test-root');
    expect(await listSessions()).toHaveLength(1);
    const page = await queryItems(session.id, { query: '', category: 'all', kind: 'all', risk: 'all', sort: 'size-desc', parentPath: null });
    expect(page.items.map((item) => item.size)).toEqual([20, 10]);
  });

  it('removes session metadata and every related item', async () => {
    await putSession(session);
    await putItemsBatch(items);
    await deleteSession(session.id);
    expect(await getSession(session.id)).toBeUndefined();
    expect((await queryItems(session.id, { query: '', category: 'all', kind: 'all', risk: 'all', sort: 'size-desc', parentPath: null })).items).toEqual([]);
  });
});
