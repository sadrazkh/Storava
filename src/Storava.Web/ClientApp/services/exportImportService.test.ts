import { beforeEach, describe, expect, it } from 'vitest';
import type { ScanItem, ScanSession } from '@/models/scan';
import { clearAllLocalData, listSessions, putItemsBatch, putSession, queryItems } from '@/services/scanDatabase';
import { exportSession, importSession } from '@/services/exportImportService';

describe('versioned scan transfer', () => {
  beforeEach(async () => clearAllLocalData());

  it('exports relative metadata and validates integrity during chunked import', async () => {
    const item: ScanItem = {
      id: 'source:folder/file.txt', sessionId: 'source', parentPath: 'folder', relativePath: 'folder/file.txt',
      name: 'file.txt', kind: 'file', size: 42, modifiedAt: 10, extension: 'txt',
      category: 'documents', depth: 1, ruleIds: [], risk: 'none',
    };
    const session: ScanSession = {
      id: 'source', rootName: 'root', source: 'fallback', status: 'completed', createdAt: 1, updatedAt: 2,
      completedAt: 2, metrics: { bytes: 42, files: 1, folders: 0, errors: 0, elapsedMs: 1, itemsPerSecond: 1, currentPath: '' },
      categories: [{ category: 'documents', bytes: 42, count: 1 }], topItems: [item], schemaVersion: 1,
    };
    await putSession(session);
    await putItemsBatch([item]);
    const exported = await exportSession(session.id);
    const content = await exported.blob.text();
    expect(content).toContain('"privacy":"relative-paths-only"');
    expect(content).not.toMatch(/[A-Z]:\\\\Users/i);

    const imported = await importSession(new File([exported.blob], exported.fileName));
    expect(imported.source).toBe('import');
    expect(await listSessions()).toHaveLength(2);
    const importedItems = await queryItems(imported.id, { query: '', category: 'all', kind: 'all', risk: 'all', sort: 'size-desc', parentPath: null });
    expect(importedItems.items[0]?.relativePath).toBe('folder/file.txt');
  });

  it('rejects files without a supported manifest', async () => {
    await expect(importSession(new File(['{"type":"item"}\n'], 'bad.storava-web'))).rejects.toThrow(/corrupt|unsupported/i);
  });
});
