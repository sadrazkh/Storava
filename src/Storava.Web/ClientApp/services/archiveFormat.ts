/**
 * The `.storava` archive, as all three editions read and write it.
 *
 * This mirrors `Storava.Contracts/Workspace/ArchiveInterchange.cs`. Two languages describing one
 * file format is a real hazard, so the names here are the wire names verbatim, and a round-trip
 * test exercises an archive produced by the desktop edition rather than by this file.
 *
 * The format is deliberately not either edition's internal shape. The desktop links its tree by
 * parent id and matches one rule per item; this edition links by parent path and matches several.
 * Choosing one as canonical would make the other's export lossy in a way nobody could see.
 */

export const ARCHIVE_EXTENSION = '.storava';

/** Bumped only when older readers could not cope. Version 1 was desktop-only. */
export const ARCHIVE_SCHEMA_VERSION = 2;
export const ARCHIVE_MINIMUM_SCHEMA_VERSION = 1;

export const ARCHIVE_ENTRIES = {
  manifest: 'manifest.json',
  scan: 'scan.json',
  /** One JSON object per line, so a large tree never has to be held whole. */
  items: 'items.dat',
  categories: 'categories.json',
  recommendations: 'recommendations.json',
} as const;

/**
 * Whether the paths inside name real locations or positions under the scanned folder. This is the
 * difference between the editions: a reader that assumes wrong either shows paths that do not
 * exist or refuses to act on ones that do.
 */
export type ArchivePathKind = 'Absolute' | 'RootRelative';

export type ArchiveProducer = 'Unknown' | 'Desktop' | 'Browser' | 'Agent';

export interface ArchiveManifest {
  schemaVersion: number;
  appVersion: string;
  createdAt: string;
  scanDate: string;
  os: string;
  culture: string;
  sessionId: string;
  rootPath: string;
  pathKind?: ArchivePathKind;
  producedBy?: ArchiveProducer;
  itemCount: number;
  recommendationCount: number;
  hashes: Record<string, string>;
  containsSecrets?: boolean;
  containsSettings?: boolean;
}

export interface ArchiveScan {
  id: string;
  /** An absolute root, or the folder's own name when the paths are relative. */
  root: string;
  label?: string | null;
  mode: string;
  status: string;
  startedAt: string;
  completedAt?: string | null;
  totalBytes: number;
  totalFiles: number;
  totalFolders: number;
  errorCount: number;
}

export interface ArchiveItem {
  id: string;
  /** How the desktop links a tree. */
  parentId?: string | null;
  /** How this edition links a tree. */
  parentPath?: string | null;
  path: string;
  name: string;
  kind: 'file' | 'folder';
  extension?: string | null;
  size: number;
  allocatedSize?: number | null;
  fileCount: number;
  folderCount: number;
  depth: number;
  createdAt?: string | null;
  modifiedAt?: string | null;
  /** The shared vocabulary: a storage purpose such as `PackageCaches`, not a file-type bucket. */
  category: string;
  technology?: string | null;
  ruleIds: string[];
  risk: string;
  isProtected: boolean;
  isReparsePoint: boolean;
  canDelete: boolean;
  canMove: boolean;
}

/** Absent `pathKind` means version 1, which only the desktop wrote, so it means absolute. */
export function pathKindOf(manifest: ArchiveManifest): ArchivePathKind {
  return manifest.pathKind ?? 'Absolute';
}

export function isReadableVersion(manifest: ArchiveManifest): boolean {
  return (
    manifest.schemaVersion >= ARCHIVE_MINIMUM_SCHEMA_VERSION &&
    manifest.schemaVersion <= ARCHIVE_SCHEMA_VERSION
  );
}

/**
 * SHA-256 over an entry's bytes, hex upper-case, matching what the .NET side writes.
 *
 * The manifest carries one of these per entry, so a truncated or edited archive is refused rather
 * than half-imported.
 */
export async function hashEntry(bytes: Uint8Array): Promise<string> {
  const buffer = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
  const digest = await crypto.subtle.digest('SHA-256', buffer as ArrayBuffer);
  return [...new Uint8Array(digest)]
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('')
    .toUpperCase();
}

/**
 * Maps this edition's extension buckets onto the shared vocabulary.
 *
 * The two answer different questions — "what kind of file is this" versus "what is this storage
 * for" — so the mapping is coarse on purpose and says `Unknown` rather than guessing where the
 * browser genuinely does not know.
 */
export function toSharedCategory(browserCategory: string): string {
  switch (browserCategory) {
    case 'documents': return 'PersonalFiles';
    case 'media': return 'Media';
    case 'archives': return 'Archives';
    case 'code': return 'DeveloperTools';
    case 'applications': return 'Applications';
    default: return 'Unknown';
  }
}

/** The reverse, for display only. Anything unrecognised falls back to this edition's "other". */
export function fromSharedCategory(shared: string, kind: 'file' | 'folder'): string {
  if (kind === 'folder') return 'folders';

  switch (shared) {
    case 'PersonalFiles': return 'documents';
    case 'Media': return 'media';
    case 'Archives': return 'archives';
    case 'DeveloperTools':
    case 'PackageCaches':
    case 'BuildArtifacts':
    case 'IdeCaches':
    case 'Sdks': return 'code';
    case 'Applications': return 'applications';
    default: return 'other';
  }
}

/** Risk names are shared verbatim; only the casing differs between the editions. */
export function toSharedRisk(risk: string): string {
  const known: Record<string, string> = {
    none: 'Unknown',
    low: 'Low',
    medium: 'Medium',
    high: 'High',
  };
  return known[risk] ?? 'Unknown';
}

export function fromSharedRisk(risk: string): 'none' | 'low' | 'medium' | 'high' {
  switch (risk.toLowerCase()) {
    case 'low': return 'low';
    case 'medium': return 'medium';
    // Protected is stricter than anything this edition can act on, and it has no separate level
    // here, so it reads as the highest one it does have.
    case 'high':
    case 'protected': return 'high';
    default: return 'none';
  }
}
