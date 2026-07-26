import { describe, expect, it } from 'vitest';
import type { ScanItem, ScanSession } from '@/models/scan';
import { createSanitizedSummaryForTest } from '@/services/advisorSanitizer';
import { createDefaultAdvisorSettings } from '@/services/advisorSettings';

const session: ScanSession = {
  id: 'private-session',
  rootName: 'C:\\Users\\Private Person\\Secret Project',
  source: 'fallback',
  status: 'completed',
  createdAt: 1,
  updatedAt: 2,
  completedAt: 2,
  metrics: {
    bytes: 2_000_000,
    files: 2,
    folders: 1,
    errors: 0,
    elapsedMs: 500,
    itemsPerSecond: 4,
    currentPath: 'Secret Project/private-client',
  },
  categories: [
    { category: 'documents', bytes: 1_500_000, count: 1 },
    { category: 'other', bytes: 500_000, count: 1 },
  ],
  topItems: [],
  schemaVersion: 1,
};

const items: ScanItem[] = [
  {
    id: 'private-session:private-client/tax-return.pdf',
    sessionId: 'private-session',
    parentPath: 'private-client',
    relativePath: 'private-client/tax-return.pdf',
    name: 'tax-return.pdf',
    kind: 'file',
    size: 1_500_000,
    modifiedAt: Date.UTC(2025, 0, 1),
    extension: 'pdf',
    category: 'documents',
    depth: 2,
    ruleIds: ['large-file'],
    risk: 'medium',
  },
  {
    id: 'private-session:private-client/unknown.secret-extension',
    sessionId: 'private-session',
    parentPath: 'private-client',
    relativePath: 'private-client/unknown.secret-extension',
    name: 'unknown.secret-extension',
    kind: 'file',
    size: 500_000,
    modifiedAt: null,
    extension: 'secret-extension',
    category: 'other',
    depth: 3,
    ruleIds: [],
    risk: 'none',
  },
];

describe('advisor sanitizer', () => {
  it('emits bounded aggregates without identifiers, paths, names, or extensions', () => {
    const settings = createDefaultAdvisorSettings('en-US');
    const result = createSanitizedSummaryForTest(session, settings, items, Date.UTC(2026, 0, 1));
    const serialized = JSON.stringify(result);

    expect(serialized).not.toContain('Private Person');
    expect(serialized).not.toContain('Secret Project');
    expect(serialized).not.toContain('private-client');
    expect(serialized).not.toContain('tax-return');
    expect(serialized).not.toContain('pdf');
    expect(serialized).not.toContain('secret-extension');
    expect(result.riskCounts.medium).toBe(1);
    expect(result.ruleMatches).toEqual([{ rule: 'large-file', count: 1 }]);
    expect(result.categories).toEqual([{ category: 'documents', bytes: 1_500_000, count: 1 }]);
    expect(result.privacy.containsRelativePaths).toBe(false);
  });

  it('can remove even anonymous path-shape statistics', () => {
    const settings = { ...createDefaultAdvisorSettings('en-US'), includePathShape: false };
    const result = createSanitizedSummaryForTest(session, settings, items);
    expect(result.pathShape).toBeUndefined();
  });

  it('offers three explicit aggregate data depths without identifiers', () => {
    const defaults = createDefaultAdvisorSettings('en-US');
    const essential = createSanitizedSummaryForTest(
      session,
      { ...defaults, dataProfile: 'essential' },
      items,
    );
    const detailed = createSanitizedSummaryForTest(
      session,
      { ...defaults, dataProfile: 'detailed' },
      items,
    );

    expect(essential.ruleMatches).toBeUndefined();
    expect(essential.sizeDistribution).toBeUndefined();
    expect(essential.pathShape).toBeUndefined();
    expect(detailed.ruleEvidence).toEqual([{
      rule: 'large-file',
      count: 1,
      bytes: 1_500_000,
      categories: [{ category: 'documents', count: 1, bytes: 1_500_000 }],
    }]);
    expect(detailed.categoryRiskMatrix).toHaveLength(1);
    expect(JSON.stringify(detailed)).not.toContain('tax-return');
    expect(JSON.stringify(detailed)).not.toContain('private-client');
  });
});
