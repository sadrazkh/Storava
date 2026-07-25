import type { Locale } from '@/localization/messages';
import type { RiskLevel } from '@/models/scan';

export interface AdvisorSettings {
  enabled: boolean;
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
  schemaVersion: 1;
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
  ruleMatches: Array<{
    rule: string;
    count: number;
  }>;
  sizeDistribution: Array<{
    bucket: string;
    count: number;
    bytes: number;
  }>;
  ageDistribution: Array<{
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
}

export type AdvisorRisk = 'low' | 'medium' | 'high';

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

export interface AdvisorResponse {
  title: string;
  executiveSummary: string;
  findings: AdvisorFinding[];
  priorities: AdvisorPriority[];
  cautions: string[];
  disclaimer: string;
  privacyNote: string;
}

export interface AdvisorResult extends AdvisorResponse {
  model: string;
  generatedAt: string;
}
