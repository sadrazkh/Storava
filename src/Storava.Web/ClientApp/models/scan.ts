import type { FolderSelection } from '@/models/capabilities';

export type ScanStatus = 'running' | 'paused' | 'completed' | 'cancelled' | 'failed' | 'imported';
export type ScanItemKind = 'file' | 'folder';
export type RiskLevel = 'none' | 'low' | 'medium' | 'high';

export interface ScanItem {
  id: string;
  sessionId: string;
  parentPath: string;
  relativePath: string;
  name: string;
  kind: ScanItemKind;
  size: number;
  modifiedAt: number | null;
  extension: string;
  category: string;
  depth: number;
  ruleIds: string[];
  risk: RiskLevel;
}

export interface CategoryAggregate {
  category: string;
  bytes: number;
  count: number;
}

export interface ScanError {
  path: string;
  name: string;
  message: string;
}

export interface ScanMetrics {
  bytes: number;
  files: number;
  folders: number;
  errors: number;
  elapsedMs: number;
  itemsPerSecond: number;
  currentPath: string;
}

export interface ScanSession {
  id: string;
  rootName: string;
  source: FolderSelection['method'] | 'import';
  status: ScanStatus;
  createdAt: number;
  updatedAt: number;
  completedAt: number | null;
  metrics: ScanMetrics;
  categories: CategoryAggregate[];
  topItems: ScanItem[];
  schemaVersion: 1;
}

export interface ScanFilters {
  query: string;
  category: string;
  kind: ScanItemKind | 'all';
  risk: RiskLevel | 'all';
  recommendation: 'all' | 'local-signals' | 'ai-targeted';
  aiRuleIds: string[];
  sort: 'size-desc' | 'size-asc' | 'name' | 'modified';
  parentPath: string | null;
}

export interface ItemPage {
  items: ScanItem[];
  hasMore: boolean;
}

export interface ItemRemovalResult {
  session: ScanSession;
  deletedFiles: number;
  deletedFolders: number;
  freedBytes: number;
}

export type WorkerCommand =
  | { type: 'start'; sessionId: string; selection: FolderSelection }
  | { type: 'pause' }
  | { type: 'resume' }
  | { type: 'cancel' };

export type WorkerEvent =
  | { type: 'batch'; items: ScanItem[]; metrics: ScanMetrics; categories: CategoryAggregate[]; topItems: ScanItem[] }
  | { type: 'state'; status: ScanStatus }
  | { type: 'error'; error: ScanError; metrics: ScanMetrics }
  | { type: 'complete'; status: 'completed' | 'cancelled'; metrics: ScanMetrics; categories: CategoryAggregate[]; topItems: ScanItem[] };
