/**
 * Reading and writing the shared `.storava` archive in the browser.
 *
 * This is what lets a scan cross between editions: a folder walked here can be opened in the
 * desktop app, and a whole-drive scan taken by the desktop or the Agent can be opened here.
 *
 * Two things stay true regardless of which direction it goes. Nothing is uploaded — the file is
 * read and written entirely in the page. And an archive whose entries do not match the hashes in
 * its manifest is refused outright rather than half-imported, so a truncated download cannot
 * quietly become a scan with missing folders.
 */
import { unzipSync, zipSync, strFromU8, strToU8 } from 'fflate';
import type { CategoryAggregate, ScanItem, ScanSession } from '@/models/scan';
import {
  ARCHIVE_ENTRIES,
  ARCHIVE_EXTENSION,
  ARCHIVE_SCHEMA_VERSION,
  fromSharedCategory,
  fromSharedRisk,
  hashEntry,
  isReadableVersion,
  pathKindOf,
  toSharedCategory,
  toSharedRisk,
  type ArchiveItem,
  type ArchiveManifest,
  type ArchivePathKind,
  type ArchiveRecommendation,
  type ArchiveScan,
} from '@/services/archiveFormat';
import {
  forEachSessionItem,
  getRecommendations,
  getSession,
  putItemsBatch,
  putRecommendations,
  putSession,
  type StoredRecommendation,
} from '@/services/scanDatabase';

/** What an archive turned out to hold, before any of it is written to the database. */
export interface ArchiveSummary {
  manifest: ArchiveManifest;
  scan: ArchiveScan;
  pathKind: ArchivePathKind;
  itemCount: number;
}

const IMPORT_BATCH_SIZE = 500;

/** Refused before anything is parsed. A scan of a large drive is nowhere near this. */
const MAXIMUM_ARCHIVE_BYTES = 2 * 1024 * 1024 * 1024;

export class ArchiveError extends Error {
  constructor(
    message: string,
    readonly reason:
      | 'too-large'
      | 'not-an-archive'
      | 'unsupported-version'
      | 'incomplete'
      | 'tampered',
  ) {
    super(message);
    this.name = 'ArchiveError';
  }
}

function requireEntry(files: Record<string, Uint8Array>, name: string): Uint8Array {
  const entry = files[name];
  if (!entry) {
    throw new ArchiveError(`The archive is missing ${name}.`, 'incomplete');
  }
  return entry;
}

function parseJson<T>(bytes: Uint8Array, name: string): T {
  try {
    return JSON.parse(strFromU8(bytes)) as T;
  } catch {
    throw new ArchiveError(`${name} could not be read.`, 'not-an-archive');
  }
}

/**
 * Verifies every entry the manifest claims a hash for, before anything is written.
 *
 * The check runs first rather than as items stream past, so a tampered archive leaves the database
 * untouched instead of partly filled.
 */
async function verify(files: Record<string, Uint8Array>, manifest: ArchiveManifest): Promise<void> {
  for (const [name, expected] of Object.entries(manifest.hashes ?? {})) {
    const entry = files[name];
    if (!entry) {
      throw new ArchiveError(`The archive is missing ${name}.`, 'incomplete');
    }

    const actual = await hashEntry(entry);
    if (actual.toUpperCase() !== expected.toUpperCase()) {
      throw new ArchiveError(
        'The archive failed its integrity check. It was truncated or edited after it was written.',
        'tampered',
      );
    }
  }
}

function unzip(bytes: Uint8Array): Record<string, Uint8Array> {
  try {
    return unzipSync(bytes);
  } catch {
    throw new ArchiveError('That file is not a Storava archive.', 'not-an-archive');
  }
}

/** Reads the manifest and scan without importing, so the page can describe the file first. */
export async function inspectArchive(file: File): Promise<ArchiveSummary> {
  if (file.size > MAXIMUM_ARCHIVE_BYTES) {
    throw new ArchiveError('That archive is larger than the supported 2 GB limit.', 'too-large');
  }

  const files = unzip(new Uint8Array(await file.arrayBuffer()));
  const manifest = parseJson<ArchiveManifest>(
    requireEntry(files, ARCHIVE_ENTRIES.manifest),
    ARCHIVE_ENTRIES.manifest,
  );

  if (!isReadableVersion(manifest)) {
    throw new ArchiveError(
      'That archive was written by a newer version of Storava than this page can read.',
      'unsupported-version',
    );
  }

  const scan = parseJson<ArchiveScan>(requireEntry(files, ARCHIVE_ENTRIES.scan), ARCHIVE_ENTRIES.scan);

  return {
    manifest,
    scan,
    pathKind: pathKindOf(manifest),
    itemCount: manifest.itemCount,
  };
}

/**
 * Turns an interchange item into one this edition can store.
 *
 * An archive from the desktop or the Agent carries absolute paths. They are kept as they are: they
 * describe a real machine and rewriting them would be inventing a folder structure. The Explorer
 * shows them as it received them and, because such a scan is marked imported, will not act on
 * them.
 */
function toScanItem(
  item: ArchiveItem,
  sessionId: string,
  pathKind: ArchivePathKind,
  rootName: string,
): ScanItem {
  const separator = item.path.includes('\\') ? '\\' : '/';
  const parentPath =
    item.parentPath ??
    (() => {
      const cut = item.path.lastIndexOf(separator);
      // The root itself has no parent; anything else keeps the segment above it.
      return cut > 0 ? item.path.slice(0, cut) : pathKind === 'RootRelative' ? '' : rootName;
    })();

  return {
    id: crypto.randomUUID(),
    sessionId,
    parentPath,
    relativePath: item.path,
    name: item.name,
    kind: item.kind === 'folder' ? 'folder' : 'file',
    size: item.size,
    modifiedAt: item.modifiedAt ? Date.parse(item.modifiedAt) : null,
    extension: (item.extension ?? '').replace(/^\./, '').toLowerCase(),
    category: fromSharedCategory(item.category, item.kind === 'folder' ? 'folder' : 'file'),
    depth: item.depth,
    ruleIds: item.ruleIds ?? [],
    risk: fromSharedRisk(item.risk),
  };
}

function toArchiveItem(item: ScanItem): ArchiveItem {
  return {
    id: item.id,
    parentPath: item.parentPath,
    path: item.relativePath,
    name: item.name,
    kind: item.kind,
    extension: item.extension || null,
    size: item.size,
    allocatedSize: null,
    fileCount: 0,
    folderCount: 0,
    depth: item.depth,
    createdAt: null,
    modifiedAt: item.modifiedAt ? new Date(item.modifiedAt).toISOString() : null,
    category: toSharedCategory(item.category),
    technology: null,
    ruleIds: item.ruleIds ?? [],
    risk: toSharedRisk(item.risk),
    isProtected: false,
    isReparsePoint: false,
    // This edition never decides what may be moved or deleted on another machine.
    canDelete: false,
    canMove: false,
  };
}

/** Reads an archive into local storage. Returns the session it created. */
export async function importArchive(
  file: File,
  onProgress?: (itemsImported: number, itemCount: number) => void,
): Promise<ScanSession> {
  const summary = await inspectArchive(file);
  const files = unzip(new Uint8Array(await file.arrayBuffer()));

  await verify(files, summary.manifest);

  const sessionId = crypto.randomUUID();
  const lines = strFromU8(requireEntry(files, ARCHIVE_ENTRIES.items)).split('\n');

  const session: ScanSession = {
    id: sessionId,
    rootName: summary.scan.root,
    source: 'import',
    status: 'imported',
    createdAt: Date.parse(summary.scan.startedAt) || Date.now(),
    updatedAt: Date.now(),
    completedAt: summary.scan.completedAt ? Date.parse(summary.scan.completedAt) : Date.now(),
    metrics: {
      bytes: summary.scan.totalBytes,
      files: summary.scan.totalFiles,
      folders: summary.scan.totalFolders,
      errors: summary.scan.errorCount,
      elapsedMs: 0,
      itemsPerSecond: 0,
      currentPath: '',
    },
    categories: [],
    topItems: [],
    schemaVersion: 1,
  };

  const categoryBytes = new Map<string, CategoryAggregate>();
  const largest: ScanItem[] = [];
  let batch: ScanItem[] = [];
  let imported = 0;

  // The ids inside an archive belong to the machine that wrote it, and this edition mints its own
  // as it reads. Without this map the advice below would arrive pointing at nothing.
  const idsFromArchive = new Map<string, string>();

  for (const line of lines) {
    if (!line.trim()) continue;

    let archived: ArchiveItem;
    try {
      archived = JSON.parse(line) as ArchiveItem;
    } catch {
      throw new ArchiveError('The archive contains an item that could not be read.', 'not-an-archive');
    }

    const item = toScanItem(archived, sessionId, summary.pathKind, summary.scan.root);
    if (archived.id) idsFromArchive.set(archived.id, item.id);
    batch.push(item);
    imported += 1;

    if (item.kind === 'file') {
      const aggregate = categoryBytes.get(item.category) ?? { category: item.category, bytes: 0, count: 0 };
      aggregate.bytes += item.size;
      aggregate.count += 1;
      categoryBytes.set(item.category, aggregate);
    }

    largest.push(item);
    if (largest.length > 200) {
      largest.sort((a, b) => b.size - a.size);
      largest.length = 100;
    }

    if (batch.length >= IMPORT_BATCH_SIZE) {
      await putItemsBatch(batch);
      batch = [];
      onProgress?.(imported, summary.itemCount);
    }
  }

  if (batch.length > 0) await putItemsBatch(batch);

  largest.sort((a, b) => b.size - a.size);
  session.categories = [...categoryBytes.values()].sort((a, b) => b.bytes - a.bytes);
  session.topItems = largest.slice(0, 100);

  await putRecommendations(readRecommendations(files, sessionId, idsFromArchive));

  await putSession(session);
  onProgress?.(imported, summary.itemCount);

  return session;
}

/**
 * Reads the advice an archive carries, rebound to this browser's own ids.
 *
 * Anything pointing at an item that is not in the archive is dropped rather than kept with a
 * dangling reference: advice attached to nothing would show up nowhere and confuse anyone reading
 * the count. A missing or unreadable entry is not an error — plenty of archives genuinely have no
 * advice in them, and refusing the whole import over it would be worse than importing the scan.
 */
function readRecommendations(
  files: Record<string, Uint8Array>,
  sessionId: string,
  idsFromArchive: Map<string, string>,
): StoredRecommendation[] {
  const entry = files[ARCHIVE_ENTRIES.recommendations];
  if (!entry) return [];

  let archived: ArchiveRecommendation[];
  try {
    const parsed: unknown = JSON.parse(strFromU8(entry));
    if (!Array.isArray(parsed)) return [];
    archived = parsed as ArchiveRecommendation[];
  } catch {
    return [];
  }

  const recommendations: StoredRecommendation[] = [];

  for (const item of archived) {
    const itemId = idsFromArchive.get(item?.itemId ?? '');
    if (!itemId) continue;

    recommendations.push({
      id: crypto.randomUUID(),
      sessionId,
      itemId,
      path: item.path ?? '',
      title: item.title ?? '',
      reason: item.reason ?? '',
      risk: item.risk ?? 'Unknown',
      estimatedBytes: Number.isFinite(item.estimatedBytes) ? item.estimatedBytes : 0,
      ruleId: item.ruleId ?? null,
      source: item.source ?? 'RuleEngine',
      // Recorded as read, and never acted on here: this edition applies its own rules before it
      // offers anything, and these describe what was permitted on another machine.
      canDelete: item.canDelete === true,
      canMove: item.canMove === true,
    });
  }

  return recommendations;
}

/**
 * Writes a local scan as a `.storava` archive the other editions can open.
 *
 * The manifest says the paths are root-relative, because that is all a browser ever knows. A
 * desktop reading this gets a browsable scan it will not act on — which is correct, since the
 * folder it describes may not be on that machine at all.
 */
export async function exportArchive(sessionId: string): Promise<{ blob: Blob; fileName: string }> {
  const session = await getSession(sessionId);
  if (!session) throw new Error('Scan session was not found.');

  const itemLines: string[] = [];
  let itemCount = 0;

  await forEachSessionItem(sessionId, (item) => {
    itemLines.push(JSON.stringify(toArchiveItem(item)));
    itemCount += 1;
  });

  const scan: ArchiveScan = {
    id: session.id,
    root: session.rootName,
    label: null,
    mode: 'quick',
    status: 'completed',
    startedAt: new Date(session.createdAt).toISOString(),
    completedAt: session.completedAt ? new Date(session.completedAt).toISOString() : null,
    totalBytes: session.metrics.bytes,
    totalFiles: session.metrics.files,
    totalFolders: session.metrics.folders,
    errorCount: session.metrics.errors,
  };

  // "\n" and not the platform newline: the .NET side hashes over "\n", and an archive has to read
  // the same wherever it was written.
  // Advice this scan arrived with travels back out, so a round trip through the browser no longer
  // costs an archive the part of itself this edition could not previously read. No remapping: the
  // item ids written above are this browser's own, which is exactly what these already reference.
  const exported: ArchiveRecommendation[] = (await getRecommendations(sessionId)).map((stored) => ({
    id: stored.id,
    itemId: stored.itemId,
    path: stored.path,
    title: stored.title,
    reason: stored.reason,
    risk: stored.risk,
    estimatedBytes: stored.estimatedBytes,
    ruleId: stored.ruleId,
    source: stored.source,
    canDelete: stored.canDelete,
    canMove: stored.canMove,
  }));

  const payload: Record<string, Uint8Array> = {
    [ARCHIVE_ENTRIES.scan]: strToU8(JSON.stringify(scan, null, 2)),
    [ARCHIVE_ENTRIES.items]: strToU8(itemLines.map((line) => `${line}\n`).join('')),
    [ARCHIVE_ENTRIES.categories]: strToU8(JSON.stringify(session.categories, null, 2)),
    [ARCHIVE_ENTRIES.recommendations]: strToU8(JSON.stringify(exported, null, 2)),
  };

  const hashes: Record<string, string> = {};
  for (const [name, bytes] of Object.entries(payload)) {
    hashes[name] = await hashEntry(bytes);
  }

  const manifest: ArchiveManifest = {
    schemaVersion: ARCHIVE_SCHEMA_VERSION,
    appVersion: '1.0.0.0',
    createdAt: new Date().toISOString(),
    scanDate: scan.startedAt,
    os: navigator.userAgent.includes('Windows') ? 'Windows' : 'Browser',
    culture: navigator.language,
    sessionId: session.id,
    rootPath: session.rootName,
    pathKind: 'RootRelative',
    producedBy: 'Browser',
    itemCount,
    recommendationCount: 0,
    hashes,
  };

  const zipped = zipSync({
    ...payload,
    [ARCHIVE_ENTRIES.manifest]: strToU8(JSON.stringify(manifest, null, 2)),
  });

  const safeName = session.rootName.replace(/[^\p{L}\p{N}._-]+/gu, '-').slice(0, 80) || 'scan';
  const stamp = new Date(session.createdAt).toISOString().slice(0, 10);

  return {
    blob: new Blob([zipped], { type: 'application/zip' }),
    fileName: `storava-${safeName}-${stamp}${ARCHIVE_EXTENSION}`,
  };
}
