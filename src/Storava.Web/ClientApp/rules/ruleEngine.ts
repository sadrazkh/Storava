import type { RiskLevel, ScanItem } from '@/models/scan';

export interface StorageRule {
  id: string;
  risk: Exclude<RiskLevel, 'none'>;
  matches(item: Pick<ScanItem, 'name' | 'kind' | 'size' | 'modifiedAt' | 'relativePath' | 'extension'>): boolean;
}

const generatedFolders = new Set([
  'node_modules', 'bin', 'obj', '.vs', '.git', 'dist', 'build', 'coverage', 'logs',
  'temp', 'tmp', 'cache', '.gradle', '.m2', '.nuget', '.venv', 'venv', '__pycache__',
  'library', 'intermediate', 'saved',
]);

export const storageRules: StorageRule[] = [
  {
    id: 'generated-folder',
    risk: 'medium',
    matches: (item) => item.kind === 'folder' && generatedFolders.has(item.name.toLowerCase()),
  },
  {
    id: 'large-file',
    risk: 'medium',
    matches: (item) => item.kind === 'file' && item.size >= 500 * 1024 * 1024,
  },
  {
    id: 'huge-file',
    risk: 'high',
    matches: (item) => item.kind === 'file' && item.size >= 2 * 1024 * 1024 * 1024,
  },
  {
    id: 'archive',
    risk: 'low',
    matches: (item) => ['zip', '7z', 'rar', 'tar', 'gz', 'iso'].includes(item.extension),
  },
  {
    id: 'backup-copy',
    risk: 'low',
    matches: (item) => /(?:backup|copy|old|bak)(?:[._ -]|$)/i.test(item.name),
  },
  {
    id: 'stale-large-file',
    risk: 'medium',
    matches: (item) =>
      item.kind === 'file'
      && item.size >= 100 * 1024 * 1024
      && item.modifiedAt !== null
      && item.modifiedAt < Date.now() - 365 * 24 * 60 * 60 * 1000,
  },
];

const riskWeight: Record<RiskLevel, number> = { none: 0, low: 1, medium: 2, high: 3 };

export function evaluateRules(item: Pick<ScanItem, 'name' | 'kind' | 'size' | 'modifiedAt' | 'relativePath' | 'extension'>): {
  ruleIds: string[];
  risk: RiskLevel;
} {
  const matches = storageRules.filter((rule) => rule.matches(item));
  const risk = matches.reduce<RiskLevel>(
    (highest, rule) => riskWeight[rule.risk] > riskWeight[highest] ? rule.risk : highest,
    'none',
  );
  return { ruleIds: matches.map((rule) => rule.id), risk };
}
