import { beforeEach, describe, expect, it } from 'vitest';
import type { ScanItem, ScanSession } from '@/models/scan';
import type { ScanFilters } from '@/models/scan';
import {
  clearAllLocalData,
  deleteSession,
  getAdvisorResult,
  getSession,
  listSessions,
  putAdvisorResult,
  putItemsBatch,
  putSession,
  queryItems,
  removeItemTree,
} from '@/services/scanDatabase';

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
  { id: 'database-test:b', sessionId: 'database-test', parentPath: '', relativePath: 'b.bin', name: 'b.bin', kind: 'file', size: 20, modifiedAt: 2, extension: 'bin', category: 'other', depth: 0, ruleIds: ['large-file'], risk: 'medium' },
];

const allFilters: ScanFilters = {
  query: '',
  category: 'all',
  kind: 'all',
  risk: 'all',
  recommendation: 'all',
  aiRuleIds: [],
  sort: 'size-desc',
  parentPath: null,
};

describe('scan IndexedDB persistence', () => {
  beforeEach(async () => clearAllLocalData());

  it('stores sessions and queries item batches by session', async () => {
    await putSession(session);
    await putItemsBatch(items);
    expect((await getSession(session.id))?.rootName).toBe('test-root');
    expect(await listSessions()).toHaveLength(1);
    const page = await queryItems(session.id, allFilters);
    expect(page.items.map((item) => item.size)).toEqual([20, 10]);
    const recommended = await queryItems(session.id, {
      ...allFilters,
      recommendation: 'ai-targeted',
      aiRuleIds: ['large-file'],
    });
    expect(recommended.items.map((item) => item.name)).toEqual(['b.bin']);
  });

  it('removes session metadata and every related item', async () => {
    await putSession(session);
    await putItemsBatch(items);
    await deleteSession(session.id);
    expect(await getSession(session.id)).toBeUndefined();
    expect((await queryItems(session.id, allFilters)).items).toEqual([]);
  });

  it('persists advisor targets locally and updates scan aggregates after item removal', async () => {
    await putSession(session);
    await putItemsBatch(items);
    const advice = {
      title: 'Review',
      executiveSummary: 'Summary',
      findings: [],
      priorities: [],
      reviewTargets: [{
        signal: 'large-file' as const,
        disposition: 'cleanup-candidate' as const,
        rationale: 'Review large items',
        confidence: 0.8,
      }],
      itemTargets: [],
      cautions: [],
      disclaimer: 'Confirm every action.',
      privacyNote: 'Aggregates only.',
      model: 'openrouter/free',
      generatedAt: '2026-07-25T00:00:00.000Z',
    };
    await putAdvisorResult(session.id, advice);
    expect((await getAdvisorResult(session.id))?.reviewTargets[0]?.signal).toBe('large-file');

    const removal = await removeItemTree(session.id, 'b.bin');
    expect(removal.freedBytes).toBe(20);
    expect(removal.session.metrics).toMatchObject({ bytes: 10, files: 1 });
    expect((await queryItems(session.id, allFilters)).items.map((item) => item.name)).toEqual(['a.bin']);
  });
});
