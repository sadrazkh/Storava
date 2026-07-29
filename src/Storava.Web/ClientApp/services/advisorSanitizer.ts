import type { AdvisorSettings, SanitizedInventoryEntry, SanitizedScanSummary } from '@/models/advisor';
import type { RiskLevel, ScanItem, ScanSession } from '@/models/scan';
import { forEachSessionItem } from '@/services/scanDatabase';

const permittedCategories = new Set(['documents', 'media', 'archives', 'code', 'applications', 'folders', 'other']);
const permittedRules = new Set(['generated-folder', 'large-file', 'huge-file', 'archive', 'stale-large-file', 'backup-copy']);

interface BucketState {
  bucket: string;
  count: number;
  bytes: number;
}

/**
 * The largest folders and files, kept while the walk streams past.
 *
 * Bounded on purpose. The point of the inventory is to let the advisor name something worth acting
 * on, and the things worth acting on are the large ones — sending a hundred thousand rows would
 * cost the user tokens to describe files nobody will ever look at.
 */
const inventoryLimit = 60;

interface InventoryCandidate {
  itemId: string;
  entry: Omit<SanitizedInventoryEntry, 'ref'>;
}

interface SummaryState {
  inventory: InventoryCandidate[];
  riskCounts: Record<RiskLevel, number>;
  ruleCounts: Map<string, number>;
  sizeDistribution: BucketState[];
  ageDistribution: BucketState[];
  depthDistribution: Array<{ bucket: string; count: number }>;
  maximumDepth: number;
  depthTotal: number;
  depthCount: number;
  categoryRiskCounts: Map<string, Record<RiskLevel, number>>;
  ruleEvidence: Map<string, {
    count: number;
    bytes: number;
    categories: Map<string, { count: number; bytes: number }>;
  }>;
}

function boundedInteger(value: number, maximum = Number.MAX_SAFE_INTEGER): number {
  if (!Number.isFinite(value) || value <= 0) return 0;
  return Math.min(maximum, Math.round(value));
}

function createState(): SummaryState {
  return {
    inventory: [],
    riskCounts: { none: 0, low: 0, medium: 0, high: 0 },
    ruleCounts: new Map<string, number>(),
    sizeDistribution: [
      { bucket: 'under-1-mib', count: 0, bytes: 0 },
      { bucket: '1-to-100-mib', count: 0, bytes: 0 },
      { bucket: '100-mib-to-1-gib', count: 0, bytes: 0 },
      { bucket: 'over-1-gib', count: 0, bytes: 0 },
    ],
    ageDistribution: [
      { bucket: 'under-30-days', count: 0, bytes: 0 },
      { bucket: '30-to-180-days', count: 0, bytes: 0 },
      { bucket: '180-to-365-days', count: 0, bytes: 0 },
      { bucket: 'over-365-days', count: 0, bytes: 0 },
      { bucket: 'unknown', count: 0, bytes: 0 },
    ],
    depthDistribution: [
      { bucket: '0-to-2', count: 0 },
      { bucket: '3-to-5', count: 0 },
      { bucket: '6-to-10', count: 0 },
      { bucket: 'over-10', count: 0 },
    ],
    maximumDepth: 0,
    depthTotal: 0,
    depthCount: 0,
    categoryRiskCounts: new Map(),
    ruleEvidence: new Map(),
  };
}

function addItem(state: SummaryState, item: ScanItem, settings: AdvisorSettings, now: number): void {
  if (!permittedCategories.has(item.category)) return;
  if (!settings.allowUnknownFolderAnalysis && item.category === 'other') return;

  if (item.risk in state.riskCounts) {
    state.riskCounts[item.risk] += 1;
    const categoryRisks = state.categoryRiskCounts.get(item.category)
      ?? { none: 0, low: 0, medium: 0, high: 0 };
    categoryRisks[item.risk] += 1;
    state.categoryRiskCounts.set(item.category, categoryRisks);
  }
  for (const rule of item.ruleIds) {
    if (!permittedRules.has(rule)) continue;
    state.ruleCounts.set(rule, (state.ruleCounts.get(rule) ?? 0) + 1);
    const evidence = state.ruleEvidence.get(rule) ?? {
      count: 0,
      bytes: 0,
      categories: new Map<string, { count: number; bytes: number }>(),
    };
    const bytes = item.kind === 'file' ? boundedInteger(item.size) : 0;
    evidence.count += 1;
    evidence.bytes += bytes;
    const category = evidence.categories.get(item.category) ?? { count: 0, bytes: 0 };
    category.count += 1;
    category.bytes += bytes;
    evidence.categories.set(item.category, category);
    state.ruleEvidence.set(rule, evidence);
  }

  if (settings.includeItemInventory)
    rememberForInventory(state, item, now);

  const depth = boundedInteger(item.depth, 10_000);
  state.maximumDepth = Math.max(state.maximumDepth, depth);
  state.depthTotal += depth;
  state.depthCount += 1;
  const depthIndex = depth <= 2 ? 0 : depth <= 5 ? 1 : depth <= 10 ? 2 : 3;
  const depthBucket = state.depthDistribution[depthIndex];
  if (depthBucket) depthBucket.count += 1;

  if (item.kind !== 'file') return;
  const bytes = boundedInteger(item.size);
  const sizeIndex = bytes < 1024 ** 2 ? 0 : bytes < 100 * 1024 ** 2 ? 1 : bytes < 1024 ** 3 ? 2 : 3;
  const sizeBucket = state.sizeDistribution[sizeIndex];
  if (sizeBucket) {
    sizeBucket.count += 1;
    sizeBucket.bytes += bytes;
  }

  let ageIndex = 4;
  if (typeof item.modifiedAt === 'number' && Number.isFinite(item.modifiedAt) && item.modifiedAt > 0) {
    const ageDays = Math.max(0, (now - item.modifiedAt) / 86_400_000);
    ageIndex = ageDays < 30 ? 0 : ageDays < 180 ? 1 : ageDays < 365 ? 2 : 3;
  }
  const ageBucket = state.ageDistribution[ageIndex];
  if (ageBucket) {
    ageBucket.count += 1;
    ageBucket.bytes += bytes;
  }
}

/**
 * Keeps one anonymous row for a folder, if it is among the largest seen so far.
 *
 * Nothing here reads the name, the extension or the path. Everything it does record is either a
 * classification the local rules already made or a figure the summary reports in aggregate anyway.
 */
function rememberForInventory(state: SummaryState, item: ScanItem, now: number): void {
  const bytes = boundedInteger(item.size);
  if (bytes <= 0) return;

  state.inventory.push({
    itemId: item.id,
    entry: {
      kind: item.kind,
      category: item.category,
      bytes,
      depth: boundedInteger(item.depth, 10_000),
      risk: item.risk,
      rules: item.ruleIds.filter((rule) => permittedRules.has(rule)),
      ageBucket: ageBucketFor(item.modifiedAt, now),
    },
  });

  // Trimmed as it grows rather than at the end, so a walk of a very large drive does not hold
  // every row it has ever seen in memory just to throw nearly all of them away.
  if (state.inventory.length > inventoryLimit * 4) {
    state.inventory.sort((left, right) => right.entry.bytes - left.entry.bytes);
    state.inventory.length = inventoryLimit;
  }
}

function ageBucketFor(modifiedAt: number | null, now: number): string {
  if (typeof modifiedAt !== 'number' || !Number.isFinite(modifiedAt) || modifiedAt <= 0) return 'unknown';

  const ageDays = Math.max(0, (now - modifiedAt) / 86_400_000);
  if (ageDays < 30) return 'under-30-days';
  if (ageDays < 180) return '30-to-180-days';
  if (ageDays < 365) return '180-to-365-days';
  return 'over-365-days';
}

function finishSummary(
  session: ScanSession,
  settings: AdvisorSettings,
  state: SummaryState,
): SanitizedScanSummary {
  const categories = session.categories
    .filter((category) => permittedCategories.has(category.category))
    .filter((category) => settings.allowUnknownFolderAnalysis || category.category !== 'other')
    .map((category) => ({
      category: category.category,
      bytes: boundedInteger(category.bytes),
      count: boundedInteger(category.count),
    }))
    .slice(0, 12);

  const summary: SanitizedScanSummary = {
    schemaVersion: 2,
    dataProfile: settings.dataProfile,
    privacy: {
      containsFileContent: false,
      containsFileNames: false,
      containsFolderNames: false,
      containsAbsolutePaths: false,
      containsRelativePaths: false,
      containsApiKeys: false,
      containsAnonymousInventory: settings.includeItemInventory,
    },
    scan: {
      status: session.status,
      totalBytes: boundedInteger(session.metrics.bytes),
      fileCount: boundedInteger(session.metrics.files),
      folderCount: boundedInteger(session.metrics.folders),
      accessErrorCount: boundedInteger(session.metrics.errors),
      elapsedMilliseconds: boundedInteger(session.metrics.elapsedMs),
    },
    categories,
    riskCounts: state.riskCounts,
  };

  if (settings.dataProfile !== 'essential') {
    summary.ruleMatches = [...state.ruleCounts.entries()]
      .map(([rule, count]) => ({ rule, count }))
      .sort((left, right) => right.count - left.count);
    summary.sizeDistribution = state.sizeDistribution;
    summary.ageDistribution = state.ageDistribution;
  }

  if (settings.dataProfile !== 'essential' && settings.includePathShape) {
    summary.pathShape = {
      maximumDepth: state.maximumDepth,
      averageDepth: state.depthCount > 0 ? Math.round(state.depthTotal / state.depthCount * 10) / 10 : 0,
      depthDistribution: state.depthDistribution,
    };
  }

  if (settings.dataProfile === 'detailed') {
    summary.categoryRiskMatrix = [...state.categoryRiskCounts.entries()]
      .map(([category, riskCounts]) => ({ category, riskCounts }))
      .sort((left, right) => {
        const leftRisk = left.riskCounts.high * 3 + left.riskCounts.medium * 2 + left.riskCounts.low;
        const rightRisk = right.riskCounts.high * 3 + right.riskCounts.medium * 2 + right.riskCounts.low;
        return rightRisk - leftRisk;
      });
    summary.ruleEvidence = [...state.ruleEvidence.entries()]
      .map(([rule, evidence]) => ({
        rule,
        count: evidence.count,
        bytes: evidence.bytes,
        categories: [...evidence.categories.entries()]
          .map(([category, aggregate]) => ({ category, ...aggregate }))
          .sort((left, right) => right.bytes - left.bytes),
      }))
      .sort((left, right) => right.bytes - left.bytes);
  }

  if (settings.includeItemInventory) {
    // Largest first, and only as many as the limit allows. The reference is minted here, so it is
    // a position in this list and nothing else — it cannot be traced to anything without the
    // mapping, which never leaves the browser.
    summary.inventory = [...state.inventory]
      .sort((left, right) => right.entry.bytes - left.entry.bytes)
      .slice(0, inventoryLimit)
      .map((candidate, index) => ({ ref: `f${index + 1}`, ...candidate.entry }));
  }

  return summary;
}

/**
 * Turns the advisor's references back into rows.
 *
 * Built the same way {@link finishSummary} mints them, from the same ordering, so the two cannot
 * drift apart. Kept out of the summary itself on purpose: this is the half that must not be sent.
 */
function buildReferenceMap(state: SummaryState, settings: AdvisorSettings): Map<string, string> {
  if (!settings.includeItemInventory) return new Map();

  return new Map([...state.inventory]
    .sort((left, right) => right.entry.bytes - left.entry.bytes)
    .slice(0, inventoryLimit)
    .map((candidate, index) => [`f${index + 1}`, candidate.itemId] as const));
}

/**
 * What was built: the summary that will be sent, and the mapping that will not.
 */
export interface SanitizedPayload {
  summary: SanitizedScanSummary;
  /** Reference to scan item id, for turning the advisor's answer back into rows on screen. */
  references: Map<string, string>;
}

export function createSanitizedSummaryForTest(
  session: ScanSession,
  settings: AdvisorSettings,
  items: Iterable<ScanItem>,
  now = Date.now(),
): SanitizedScanSummary {
  const state = createState();
  for (const item of items) addItem(state, item, settings, now);
  return finishSummary(session, settings, state);
}

export function createSanitizedPayloadForTest(
  session: ScanSession,
  settings: AdvisorSettings,
  items: Iterable<ScanItem>,
  now = Date.now(),
): SanitizedPayload {
  const state = createState();
  for (const item of items) addItem(state, item, settings, now);
  return { summary: finishSummary(session, settings, state), references: buildReferenceMap(state, settings) };
}

export async function buildSanitizedSummary(
  session: ScanSession,
  settings: AdvisorSettings,
  now = Date.now(),
): Promise<SanitizedPayload> {
  const state = createState();
  await forEachSessionItem(session.id, (item) => addItem(state, item, settings, now));
  return { summary: finishSummary(session, settings, state), references: buildReferenceMap(state, settings) };
}
