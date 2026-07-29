import type { Locale } from '@/localization/messages';
import type { RiskLevel } from '@/models/scan';

export type AdvisorDataProfile = 'essential' | 'balanced' | 'detailed';

export interface AdvisorSettings {
  enabled: boolean;
  dataProfile: AdvisorDataProfile;
  model: string;
  baseUrl: string;
  temperature: number;
  maxTokens: number;
  timeoutMs: number;
  preferredLanguage: Locale;
  includePathShape: boolean;
  /**
   * Sends an anonymous inventory, so the advisor can point at a particular folder instead of at a
   * whole class of them. See {@link SanitizedInventoryEntry} for exactly what a row contains.
   */
  includeItemInventory: boolean;
  allowUnknownFolderAnalysis: boolean;
  allowReportGeneration: boolean;
  requireZeroDataRetention: boolean;
}

export interface SanitizedScanSummary {
  schemaVersion: 2;
  dataProfile: AdvisorDataProfile;
  privacy: {
    containsFileContent: false;
    containsFileNames: false;
    containsFolderNames: false;
    containsAbsolutePaths: false;
    containsRelativePaths: false;
    containsApiKeys: false;
    /**
     * True when {@link SanitizedScanSummary.inventory} is present.
     *
     * Every other flag here stays false with it. The inventory is one row per folder and carries
     * no name, no extension and no path — only a reference that means nothing outside this one
     * request, and figures the summary already reports in aggregate.
     */
    containsAnonymousInventory: boolean;
  };
  scan: {
    status: string;
    totalBytes: number;
    fileCount: number;
    folderCount: number;
    accessErrorCount: number;
    elapsedMilliseconds: number;
  };
  categories: Array<{
    category: string;
    bytes: number;
    count: number;
  }>;
  riskCounts: Record<RiskLevel, number>;
  ruleMatches?: Array<{
    rule: string;
    count: number;
  }>;
  sizeDistribution?: Array<{
    bucket: string;
    count: number;
    bytes: number;
  }>;
  ageDistribution?: Array<{
    bucket: string;
    count: number;
    bytes: number;
  }>;
  pathShape?: {
    maximumDepth: number;
    averageDepth: number;
    depthDistribution: Array<{
      bucket: string;
      count: number;
    }>;
  };
  categoryRiskMatrix?: Array<{
    category: string;
    riskCounts: Record<RiskLevel, number>;
  }>;
  ruleEvidence?: Array<{
    rule: string;
    count: number;
    bytes: number;
    categories: Array<{
      category: string;
      count: number;
      bytes: number;
    }>;
  }>;

  /**
   * One row per folder, anonymous, largest first. Present only when the user has turned it on.
   *
   * Without it the advisor can only speak about classes of folder, because everything else here is
   * an aggregate — which is why its advice used to arrive attached to a rule rather than to
   * anything on screen.
   */
  inventory?: SanitizedInventoryEntry[];
}

/**
 * One folder, described without saying which folder it is.
 *
 * This exists so the advisor can say something about a particular folder rather than about every
 * folder matching a rule. What it deliberately does not carry: the name, the extension, the path,
 * or any part of one. The reference is a sequence number minted for this request and thrown away
 * with it; only the page that built it can turn it back into a row on screen.
 */
export interface SanitizedInventoryEntry {
  /** Meaningless outside this request. The mapping back never leaves the browser. */
  ref: string;
  kind: 'file' | 'folder';
  category: string;
  bytes: number;
  depth: number;
  risk: RiskLevel;
  /** Which of the local rules matched, by rule id. Never a name or a path fragment. */
  rules: string[];
  /** How long since it was last written, in the same buckets the distribution uses. */
  ageBucket: string;
}

export type AdvisorRisk = 'low' | 'medium' | 'high';
export const advisorSignalIds = [
  'generated-folder',
  'large-file',
  'huge-file',
  'archive',
  'backup-copy',
  'stale-large-file',
] as const;
export type AdvisorSignalId = typeof advisorSignalIds[number];
export type AdvisorDisposition = 'cleanup-candidate' | 'archive-candidate' | 'investigate';

export interface AdvisorFinding {
  title: string;
  evidence: string;
  risk: AdvisorRisk;
  confidence: number;
}

export interface AdvisorPriority {
  title: string;
  reason: string;
  confidence: number;
}

export interface AdvisorReviewTarget {
  signal: AdvisorSignalId;
  disposition: AdvisorDisposition;
  rationale: string;
  confidence: number;
}

/**
 * What the advisor said about one folder in the inventory.
 *
 * Keyed by the reference it was given, which the page turns back into a row. A reference the page
 * does not recognise is dropped rather than guessed at.
 */
export interface AdvisorItemTarget {
  ref: string;
  disposition: AdvisorDisposition;
  rationale: string;
  confidence: number;

  /**
   * Which row this is about, filled in locally after the answer comes back.
   *
   * Never part of what the model returns — the response is checked for exactly the four fields
   * above and rejected otherwise. Stored with the result so that reopening a scan still shows the
   * advice against the right folder, long after the reference numbers have been forgotten.
   */
  itemId?: string;
}

export interface AdvisorResponse {
  title: string;
  executiveSummary: string;
  findings: AdvisorFinding[];
  priorities: AdvisorPriority[];
  reviewTargets: AdvisorReviewTarget[];
  /** Empty when no inventory was sent, which is the only time the advisor cannot name a folder. */
  itemTargets: AdvisorItemTarget[];
  cautions: string[];
  disclaimer: string;
  privacyNote: string;
}

export interface AdvisorResult extends AdvisorResponse {
  model: string;
  generatedAt: string;
}

export interface StoredAdvisorResult {
  sessionId: string;
  result: AdvisorResult;
}
