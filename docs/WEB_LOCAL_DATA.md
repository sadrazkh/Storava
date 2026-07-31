# Storava Web local data and transfer

## Browser storage

Storava Web stores scan sessions and individual metadata items in the versioned
`storava-web` IndexedDB database. Schema version 3 stores sessions, item records, native
directory handles, validated advisor results, and recommendations carried in an imported
archive, in separate object stores. Item records are written in batches and indexed by
session, size, relative path, and modification time. A scan is never serialized into one
oversized record.

The Scan History view reports what this edition holds and offers to empty it per store:
scans, advisor results, and preferences. IndexedDB exposes no per-store byte size, so each
row carries a record count and the origin total comes from `navigator.storage.estimate()`.
The stored API key is listed but removed only from the AI panel, where the consequence of
losing it is stated.

Stored metadata includes the selected root label, root-relative item path, kind, byte size,
last-modified timestamp, extension, local category, rule identifiers, and risk level. It
does not include file bytes, file content, an absolute path, browser profile data, API keys,
or tokens.

Native handles are capability objects managed by the browser. They are structured-cloned
into IndexedDB for the matching native scan only; they are never converted to a path,
included in export/report data, or sent over the network. Advisor results contain aggregate
text and closed-list signal identifiers, not item identifiers. The Explorer performs the
signal-to-item mapping locally.

## Account boundary

Phase 7 accounts do not change the browser-data boundary. PostgreSQL stores identity,
revocable login sessions, future companion-device ownership, and usage-ledger entries. It
does not store scan sessions, item metadata, directory handles, advisor payloads, advisor
results, OpenRouter keys, or exported reports. Signing in therefore does not synchronize an
existing browser scan to another browser or device.

An interrupted tab is retained as an interrupted session for evidence, but the UI does not
claim that traversal can resume after the permission-bearing tab closes. Starting another
scan requires choosing the folder again.

## Export format

`.storava-web` is newline-delimited JSON:

1. Versioned manifest with an explicit `relative-paths-only` privacy marker.
2. Scan session metadata.
3. One item record per line.
4. Integrity trailer containing item count, file-byte aggregate, and checksum.

Import reads the file stream incrementally, validates the manifest and every path, writes
500-item batches, yields between chunks, and rejects unsupported versions, absolute paths,
malformed records, incomplete exports, and integrity mismatches. Keys and tokens are not
part of the schema.

## Browser limitations

- Native directory handles require a secure context and current Chromium-family browser.
- The directory-input fallback receives a fixed file list and cannot discover later changes.
- Fallback and imported scans cannot open or delete source files and are always read-only.
- A native scan can request fresh read or read/write permission for a selected item. Deletion
  is permanent, folders are recursive, and exact-name confirmation is required.
- Browsers expose only the chosen root label and root-relative paths. A Web page cannot
  reliably reveal an arbitrary item in the operating-system file manager.
- Browser permission can expire and must be requested again for a new scan.
- Last-modified time is available; platform allocation size, Windows attributes, junction
  behavior, file locks, and administrator-only paths remain Desktop-only.
- IndexedDB quota and eviction policy are controlled by the browser.
