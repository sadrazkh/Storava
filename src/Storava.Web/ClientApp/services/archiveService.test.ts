import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { unzipSync, strFromU8 } from 'fflate';
import { beforeEach, describe, expect, it } from 'vitest';
import { exportArchive, importArchive, inspectArchive, ArchiveError } from '@/services/archiveService';
import { ARCHIVE_ENTRIES, type ArchiveItem, type ArchiveManifest } from '@/services/archiveFormat';
import { clearAllLocalData, putItemsBatch, putSession } from '@/services/scanDatabase';
import type { ScanItem, ScanSession } from '@/models/scan';

/**
 * The fixture is produced by the desktop edition's own test suite, not by this file. Two
 * implementations of one file format is the hazard the shared archive exists to avoid, and a test
 * that read something it wrote itself would not notice the two drifting apart.
 *
 * Refresh it with STORAVA_REFRESH_FIXTURES=1 dotnet test tests/Storava.Infrastructure.Tests.
 */
async function desktopArchive(): Promise<File> {
  // Resolved from the working directory rather than import.meta.url: under the browser-like test
  // environment that is not a file: URL, and converting it throws.
  const path = resolve(process.cwd(), 'ClientApp/test/fixtures/desktop-v2.storava');
  const bytes = await readFile(path);
  return new File([bytes], 'desktop-v2.storava', { type: 'application/zip' });
}

/**
 * The same arrangement for the companion Agent, whose fixture its own test suite writes by
 * downloading one over the wire. Refresh it with
 * STORAVA_REFRESH_FIXTURES=1 dotnet test tests/Storava.Agent.Tests.
 */
async function agentArchive(): Promise<File> {
  const path = resolve(process.cwd(), 'ClientApp/test/fixtures/agent-v2.storava');
  const bytes = await readFile(path);
  return new File([bytes], 'agent-v2.storava', { type: 'application/zip' });
}

function session(overrides: Partial<ScanSession> = {}): ScanSession {
  return {
    id: 'session-1',
    rootName: 'projects',
    source: 'import',
    status: 'completed',
    createdAt: Date.parse('2026-07-01T10:00:00Z'),
    updatedAt: Date.parse('2026-07-01T10:05:00Z'),
    completedAt: Date.parse('2026-07-01T10:05:00Z'),
    metrics: {
      bytes: 4096, files: 2, folders: 1, errors: 0,
      elapsedMs: 1200, itemsPerSecond: 10, currentPath: '',
    },
    categories: [],
    topItems: [],
    schemaVersion: 1,
    ...overrides,
  };
}

function item(overrides: Partial<ScanItem> = {}): ScanItem {
  return {
    id: crypto.randomUUID(),
    sessionId: 'session-1',
    parentPath: 'projects',
    relativePath: 'projects/app.ts',
    name: 'app.ts',
    kind: 'file',
    size: 2048,
    modifiedAt: Date.parse('2026-06-30T09:00:00Z'),
    extension: 'ts',
    category: 'code',
    depth: 1,
    ruleIds: [],
    risk: 'none',
    ...overrides,
  };
}

/**
 * A walk of a real machine that this page cannot perform and did not write. The Agent is the only
 * producer whose archives arrive over HTTP rather than from a file the user picked, so a reader
 * that quietly assumed the desktop was the only other edition would fail exactly here.
 */
describe('reading an archive the companion agent wrote', () => {
  beforeEach(async () => {
    await clearAllLocalData();
  });

  it('recognises the agent as the edition that wrote it', async () => {
    const summary = await inspectArchive(await agentArchive());

    expect(summary.manifest.producedBy).toBe('Agent');
    expect(summary.manifest.schemaVersion).toBe(2);
    expect(summary.pathKind).toBe('Absolute');
  });

  it('imports a walk of a real machine into this workspace', async () => {
    const imported = await importArchive(await agentArchive());

    expect(imported.status).toBe('imported');
    expect(imported.metrics.files).toBeGreaterThan(0);
    expect(imported.topItems.length).toBeGreaterThan(0);

    // Absolute paths, from a file system no browser can reach.
    expect(imported.topItems.some((entry) => /^[A-Za-z]:\\/.test(entry.relativePath))).toBe(true);
  });

  it('brings the local rule catalog with it', async () => {
    const imported = await importArchive(await agentArchive());
    const cache = imported.topItems.find((entry) => entry.name === 'node_modules');

    expect(cache).toBeDefined();
    expect(cache!.ruleIds.length).toBeGreaterThan(0);
  });
});

describe('reading an archive the desktop edition wrote', () => {
  beforeEach(async () => {
    await clearAllLocalData();
  });

  it('describes it before importing anything', async () => {
    const summary = await inspectArchive(await desktopArchive());

    expect(summary.manifest.schemaVersion).toBe(2);
    expect(summary.manifest.producedBy).toBe('Desktop');
    // Absolute, because the desktop walks a real file system. The browser never does.
    expect(summary.pathKind).toBe('Absolute');
    expect(summary.itemCount).toBeGreaterThan(0);
  });

  it('imports it into local storage with its real paths intact', async () => {
    const imported = await importArchive(await desktopArchive());

    expect(imported.status).toBe('imported');
    expect(imported.metrics.files).toBeGreaterThan(0);

    // The whole point of crossing editions: a path this edition could never have produced.
    const topPaths = imported.topItems.map((entry) => entry.relativePath);
    expect(topPaths.some((path) => /^[A-Za-z]:\\/.test(path))).toBe(true);
  });

  it('carries the desktop rule catalog across', async () => {
    const imported = await importArchive(await desktopArchive());
    const cache = imported.topItems.find((entry) => entry.name === 'node_modules');

    expect(cache).toBeDefined();
    // Matched by the desktop's rule engine, which this edition has no copy of.
    expect(cache!.ruleIds.length).toBeGreaterThan(0);
  });

  it('refuses an archive that was edited after it was written', async () => {
    const original = await desktopArchive();
    const files = unzipSync(new Uint8Array(await original.arrayBuffer()));

    // Append a line to the item payload, leaving the manifest's hash stale.
    const tampered = { ...files };
    tampered[ARCHIVE_ENTRIES.items] = new Uint8Array([
      ...files[ARCHIVE_ENTRIES.items]!,
      ...new TextEncoder().encode('{"id":"injected","path":"C:\\\\Windows","name":"Windows"}\n'),
    ]);

    const { zipSync } = await import('fflate');
    const rebuilt = new File([zipSync(tampered)], 'tampered.storava');

    await expect(importArchive(rebuilt)).rejects.toSatisfy(
      (error: unknown) => error instanceof ArchiveError && error.reason === 'tampered',
    );
  });

  it('refuses a file that is not an archive at all', async () => {
    const notAnArchive = new File(['this is just text'], 'nope.storava');

    await expect(inspectArchive(notAnArchive)).rejects.toSatisfy(
      (error: unknown) => error instanceof ArchiveError && error.reason === 'not-an-archive',
    );
  });

  it('refuses a schema newer than it can read', async () => {
    const original = await desktopArchive();
    const files = unzipSync(new Uint8Array(await original.arrayBuffer()));
    const manifest = JSON.parse(strFromU8(files[ARCHIVE_ENTRIES.manifest]!)) as ArchiveManifest;
    manifest.schemaVersion = 99;

    const { zipSync, strToU8 } = await import('fflate');
    const rebuilt = new File([
      zipSync({ ...files, [ARCHIVE_ENTRIES.manifest]: strToU8(JSON.stringify(manifest)) }),
    ], 'future.storava');

    await expect(inspectArchive(rebuilt)).rejects.toSatisfy(
      (error: unknown) => error instanceof ArchiveError && error.reason === 'unsupported-version',
    );
  });
});

describe('writing an archive the other editions can open', () => {
  beforeEach(async () => {
    // The fake IndexedDB outlives a single test, so seeding without clearing would leave each
    // test reading the previous one's items.
    await clearAllLocalData();
    await putSession(session());
    await putItemsBatch([
      item(),
      item({ relativePath: 'projects/node_modules', name: 'node_modules', kind: 'folder', size: 2048, ruleIds: ['npm.node-modules'], risk: 'low' }),
    ]);
  });

  it('produces a zip with the entries every edition expects', async () => {
    const { blob, fileName } = await exportArchive('session-1');
    const files = unzipSync(new Uint8Array(await blob.arrayBuffer()));

    expect(fileName.endsWith('.storava')).toBe(true);
    for (const entry of Object.values(ARCHIVE_ENTRIES)) {
      expect(Object.keys(files)).toContain(entry);
    }
  });

  it('says its paths are root-relative, because that is all a browser knows', async () => {
    const { blob } = await exportArchive('session-1');
    const files = unzipSync(new Uint8Array(await blob.arrayBuffer()));
    const manifest = JSON.parse(strFromU8(files[ARCHIVE_ENTRIES.manifest]!)) as ArchiveManifest;

    // A desktop reading this must not mistake these for locations on its own disk.
    expect(manifest.pathKind).toBe('RootRelative');
    expect(manifest.producedBy).toBe('Browser');
    expect(manifest.schemaVersion).toBe(2);
  });

  it('separates item lines with \\n so the hash matches on any platform', async () => {
    const { blob } = await exportArchive('session-1');
    const files = unzipSync(new Uint8Array(await blob.arrayBuffer()));
    const items = strFromU8(files[ARCHIVE_ENTRIES.items]!);

    // The .NET side hashes over "\n"; a platform newline here would fail its own integrity check.
    expect(items).not.toContain('\r');
    expect(items.trimEnd().split('\n')).toHaveLength(2);
  });

  it('maps its display buckets onto the shared vocabulary', async () => {
    const { blob } = await exportArchive('session-1');
    const files = unzipSync(new Uint8Array(await blob.arrayBuffer()));
    const lines = strFromU8(files[ARCHIVE_ENTRIES.items]!).trimEnd().split('\n');
    const exported = lines.map((line) => JSON.parse(line) as ArchiveItem);

    // "code" is this edition's extension bucket; the shared vocabulary is about purpose.
    expect(exported.find((entry) => entry.name === 'app.ts')?.category).toBe('DeveloperTools');
    expect(exported.find((entry) => entry.name === 'node_modules')?.ruleIds).toEqual(['npm.node-modules']);
  });

  it('never claims another machine may be acted on', async () => {
    const { blob } = await exportArchive('session-1');
    const files = unzipSync(new Uint8Array(await blob.arrayBuffer()));
    const exported = strFromU8(files[ARCHIVE_ENTRIES.items]!)
      .trimEnd().split('\n').map((line) => JSON.parse(line) as ArchiveItem);

    // This edition has no rule catalog saying what is safe to remove, so it asserts nothing.
    expect(exported.every((entry) => !entry.canDelete && !entry.canMove)).toBe(true);
  });

  it('round-trips through its own reader', async () => {
    const { blob, fileName } = await exportArchive('session-1');
    const reimported = await importArchive(new File([blob], fileName));

    expect(reimported.metrics.files).toBe(2);
    expect(reimported.topItems.map((entry) => entry.name).sort()).toEqual(['app.ts', 'node_modules']);
  });
});
