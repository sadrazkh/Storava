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

export interface AdvisorResponse {
  title: string;
  executiveSummary: string;
  findings: AdvisorFinding[];
  priorities: AdvisorPriority[];
  reviewTargets: AdvisorReviewTarget[];
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
