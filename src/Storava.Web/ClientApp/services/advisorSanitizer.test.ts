import { describe, expect, it } from 'vitest';
import type { ScanItem, ScanSession } from '@/models/scan';
import { createSanitizedPayloadForTest, createSanitizedSummaryForTest } from '@/services/advisorSanitizer';
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

/**
 * The anonymous inventory.
 *
 * It exists so the advisor can point at one folder instead of at every folder matching a rule, and
 * the whole question is whether "anonymous" is true. The fixture above is built to make a failure
 * loud: the folder is called Secret Project, the file is a tax return, and the extension is
 * secret-extension. If any of that reaches the payload, these fail on the string.
 */
describe('the anonymous item inventory', () => {
  const withInventory = { ...createDefaultAdvisorSettings('en-US'), includeItemInventory: true };

  it('is absent unless it is asked for', () => {
    const result = createSanitizedSummaryForTest(session, createDefaultAdvisorSettings('en-US'), items);

    expect(result.inventory).toBeUndefined();
    expect(result.privacy.containsAnonymousInventory).toBe(false);
  });

  it('carries no name, extension or path when it is', () => {
    const result = createSanitizedSummaryForTest(session, withInventory, items, Date.UTC(2026, 0, 1));
    const serialized = JSON.stringify(result);

    expect(result.inventory).toBeDefined();
    expect(serialized).not.toContain('Private Person');
    expect(serialized).not.toContain('Secret Project');
    expect(serialized).not.toContain('private-client');
    expect(serialized).not.toContain('tax-return');
    expect(serialized).not.toContain('pdf');
    expect(serialized).not.toContain('secret-extension');
    // The scan item id embeds the relative path, so it must not travel either.
    expect(serialized).not.toContain('private-session:');
  });

  it('says so in the privacy block, and changes nothing else there', () => {
    const result = createSanitizedSummaryForTest(session, withInventory, items);

    expect(result.privacy).toEqual({
      containsFileContent: false,
      containsFileNames: false,
      containsFolderNames: false,
      containsAbsolutePaths: false,
      containsRelativePaths: false,
      containsApiKeys: false,
      containsAnonymousInventory: true,
    });
  });

  it('describes each item only by what the local rules already decided', () => {
    const result = createSanitizedSummaryForTest(session, withInventory, items, Date.UTC(2026, 0, 1));
    const largest = result.inventory?.[0];

    expect(largest).toEqual({
      ref: 'f1',
      kind: 'file',
      category: 'documents',
      bytes: 1_500_000,
      depth: 2,
      risk: 'medium',
      rules: ['large-file'],
      ageBucket: 'over-365-days',
    });
  });

  /** Largest first, because the point is to name something worth acting on. */
  it('is ordered by size', () => {
    const smaller = { ...items[0]!, id: 'private-session:private-client/small.pdf', size: 90_000 };

    const result = createSanitizedSummaryForTest(session, withInventory, [smaller, items[0]!]);

    expect(result.inventory?.map((entry) => entry.bytes)).toEqual([1_500_000, 90_000]);
    expect(result.inventory?.map((entry) => entry.ref)).toEqual(['f1', 'f2']);
  });

  /**
   * The inventory passes through the same gate as everything else in the payload, so a user who
   * has told the advisor to leave unrecognised folders alone does not find them listed here.
   * Without this, turning the inventory on would quietly widen a separate refusal.
   */
  it('leaves out what the aggregates would leave out', () => {
    const uncategorised = items[1]!; // category 'other'

    const excluded = createSanitizedSummaryForTest(session, withInventory, [uncategorised]);
    const included = createSanitizedSummaryForTest(
      session,
      { ...withInventory, allowUnknownFolderAnalysis: true },
      [uncategorised],
    );

    expect(excluded.inventory).toEqual([]);
    expect(included.inventory).toHaveLength(1);
  });

  /**
   * The mapping is what makes a reference meaningful, and it is the half that must not be sent.
   * Built separately from the payload for exactly that reason.
   */
  it('hands back a mapping that the payload itself does not contain', () => {
    const payload = createSanitizedPayloadForTest(session, withInventory, items);

    expect(payload.references.get('f1')).toBe('private-session:private-client/tax-return.pdf');
    expect(JSON.stringify(payload.summary)).not.toContain('tax-return');
  });

  it('hands back no mapping when no inventory was built', () => {
    const payload = createSanitizedPayloadForTest(session, createDefaultAdvisorSettings('en-US'), items);

    expect(payload.references.size).toBe(0);
  });

  /**
   * Bounded, so a walk of a whole drive does not turn into a payload nobody can read and the user
   * pays to have described.
   */
  it('stops at sixty items however many there are', () => {
    const many = Array.from({ length: 500 }, (_, index) => ({
      ...items[0]!,
      id: `private-session:generated/${index}.bin`,
      size: 1_000 + index,
    }));

    const result = createSanitizedSummaryForTest(session, withInventory, many);

    expect(result.inventory).toHaveLength(60);
    // The largest survive the trimming, not merely the first sixty encountered.
    expect(result.inventory?.[0]?.bytes).toBe(1_499);
  });

  /** A rule the summary would not report in aggregate has no business appearing per item either. */
  it('reports only rules the aggregate summary already permits', () => {
    const withPrivateRule = [{ ...items[0]!, ruleIds: ['large-file', 'internal-only-rule'] }];

    const result = createSanitizedSummaryForTest(session, withInventory, withPrivateRule);

    expect(result.inventory?.[0]?.rules).toEqual(['large-file']);
  });
});
