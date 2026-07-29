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

/**
 * One piece of advice about one item, as it travels between editions.
 *
 * The archive has always had a place for these, but the desktop used to write its own internal
 * record into it — different field names, different enums — which this edition had no way to read.
 * So it dropped the entry on import and wrote an empty one on export, and advice produced by the
 * Agent or the desktop vanished on the way here.
 *
 * `itemId` refers to an id in the same archive's item list. An edition that mints its own ids while
 * importing has to remap it, or the advice arrives pointing at nothing.
 */
export interface ArchiveRecommendation {
  id: string;
  itemId: string;
  path: string;
  title: string;
  reason: string;
  /** "Unknown", "Low", "Medium", "High" or "Protected". */
  risk: string;
  estimatedBytes: number;
  ruleId?: string | null;
  /** The shared storage-purpose vocabulary, such as "PackageCaches". */
  category?: string;
  technology?: string | null;
  /**
   * How the tool itself supports relocating this, and what to do when it does not.
   *
   * Facts about the technology rather than about the machine that wrote them, which is why they
   * travel: a reader that loses them moves a folder by a mechanism the tool never documented. This
   * edition cannot move anything, but it must not be the step that quietly drops them from an
   * archive on its way somewhere that can.
   */
  officialMethod?: string;
  fallbackMethod?: string;
  methodHint?: string | null;
  warning?: string | null;
  /** "RuleEngine" or "Ai", so a reader can tell a rule match from a model's suggestion. */
  source: string;
  /** What the rules permitted where it was scanned. Advice, never permission. */
  canDelete: boolean;
  canMove: boolean;
}
