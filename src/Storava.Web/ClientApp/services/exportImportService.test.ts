import { beforeEach, describe, expect, it } from 'vitest';
import type { ScanItem, ScanSession } from '@/models/scan';
import {
  clearAllLocalData,
  getAdvisorResult,
  listSessions,
  putAdvisorResult,
  putItemsBatch,
  putSession,
  queryItems,
} from '@/services/scanDatabase';
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
    await putAdvisorResult(session.id, {
      title: 'Archive review',
      executiveSummary: 'Review the archive aggregate.',
      findings: [],
      priorities: [],
      reviewTargets: [{
        signal: 'archive',
        disposition: 'archive-candidate',
        rationale: 'Confirm whether archives are still needed.',
        confidence: 0.8,
      }],
      cautions: ['Review locally.'],
      disclaimer: 'No automatic action.',
      privacyNote: 'Aggregates only.',
      model: 'openrouter/free',
      generatedAt: '2026-07-25T12:00:00.000Z',
    });
    const exported = await exportSession(session.id);
    const content = await exported.blob.text();
    expect(content).toContain('"privacy":"relative-paths-only"');
    expect(content).toContain('"version":2');
    expect(content).not.toMatch(/[A-Z]:\\\\Users/i);
    expect(content).not.toContain('sk-or-v1');

    const imported = await importSession(new File([exported.blob], exported.fileName));
    expect(imported.source).toBe('import');
    expect(await listSessions()).toHaveLength(2);
    const importedItems = await queryItems(imported.id, {
      query: '',
      category: 'all',
      kind: 'all',
      risk: 'all',
      recommendation: 'all',
      aiRuleIds: [],
      sort: 'size-desc',
      parentPath: null,
    });
    expect(importedItems.items[0]?.relativePath).toBe('folder/file.txt');
    expect((await getAdvisorResult(imported.id))?.reviewTargets[0]?.signal).toBe('archive');
  });

  it('rejects files without a supported manifest', async () => {
    await expect(importSession(new File(['{"type":"item"}\n'], 'bad.storava-web'))).rejects.toThrow(/corrupt|unsupported/i);
  });

  it('migrates a complete version 1 export without advisor data', async () => {
    const legacySession: ScanSession = {
      id: 'legacy',
      rootName: 'legacy-root',
      source: 'import',
      status: 'completed',
      createdAt: 1,
      updatedAt: 1,
      completedAt: 1,
      metrics: { bytes: 0, files: 0, folders: 0, errors: 0, elapsedMs: 1, itemsPerSecond: 0, currentPath: '' },
      categories: [],
      topItems: [],
      schemaVersion: 1,
    };
    const legacy = [
      JSON.stringify({ type: 'manifest', format: 'storava-web', version: 1, appVersion: '0.4.0', createdAt: '2026-01-01T00:00:00.000Z', privacy: 'relative-paths-only' }),
      JSON.stringify({ type: 'session', session: legacySession }),
      JSON.stringify({ type: 'integrity', itemCount: 0, totalBytes: 0, checksum: '811c9dc5' }),
      '',
    ].join('\n');

    const imported = await importSession(new File([legacy], 'legacy.storava-web'));

    expect(imported.rootName).toBe('legacy-root');
    expect(await getAdvisorResult(imported.id)).toBeUndefined();
  });
});
