import type { AdvisorSettings, SanitizedScanSummary } from '@/models/advisor';
import type { RiskLevel, ScanItem, ScanSession } from '@/models/scan';
import { forEachSessionItem } from '@/services/scanDatabase';

const permittedCategories = new Set(['documents', 'media', 'archives', 'code', 'applications', 'folders', 'other']);
const permittedRules = new Set(['generated-folder', 'large-file', 'huge-file', 'archive', 'stale-large-file', 'backup-copy']);

interface BucketState {
  bucket: string;
  count: number;
  bytes: number;
}

interface SummaryState {
  riskCounts: Record<RiskLevel, number>;
  ruleCounts: Map<string, number>;
  sizeDistribution: BucketState[];
  ageDistribution: BucketState[];
  depthDistribution: Array<{ bucket: string; count: number }>;
  maximumDepth: number;
  depthTotal: number;
  depthCount: number;
}

function boundedInteger(value: number, maximum = Number.MAX_SAFE_INTEGER): number {
  if (!Number.isFinite(value) || value <= 0) return 0;
  return Math.min(maximum, Math.round(value));
}

function createState(): SummaryState {
  return {
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
  };
}

function addItem(state: SummaryState, item: ScanItem, settings: AdvisorSettings, now: number): void {
  if (!permittedCategories.has(item.category)) return;
  if (!settings.allowUnknownFolderAnalysis && item.category === 'other') return;

  if (item.risk in state.riskCounts) state.riskCounts[item.risk] += 1;
  for (const rule of item.ruleIds) {
    if (permittedRules.has(rule)) state.ruleCounts.set(rule, (state.ruleCounts.get(rule) ?? 0) + 1);
  }

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
    schemaVersion: 1,
    privacy: {
      containsFileContent: false,
      containsFileNames: false,
      containsFolderNames: false,
      containsAbsolutePaths: false,
      containsRelativePaths: false,
      containsApiKeys: false,
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
    ruleMatches: [...state.ruleCounts.entries()]
      .map(([rule, count]) => ({ rule, count }))
      .sort((left, right) => right.count - left.count),
    sizeDistribution: state.sizeDistribution,
    ageDistribution: state.ageDistribution,
  };

  if (settings.includePathShape) {
    summary.pathShape = {
      maximumDepth: state.maximumDepth,
      averageDepth: state.depthCount > 0 ? Math.round(state.depthTotal / state.depthCount * 10) / 10 : 0,
      depthDistribution: state.depthDistribution,
    };
  }
  return summary;
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

export async function buildSanitizedSummary(
  session: ScanSession,
  settings: AdvisorSettings,
  now = Date.now(),
): Promise<SanitizedScanSummary> {
  const state = createState();
  await forEachSessionItem(session.id, (item) => addItem(state, item, settings, now));
  return finishSummary(session, settings, state);
}
