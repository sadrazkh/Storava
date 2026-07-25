# Storava Web local data and transfer

## Browser storage

Storava Web stores scan sessions and individual metadata items in the versioned
`storava-web` IndexedDB database. Item records are written in batches and indexed by session,
size, relative path, and modification time. A scan is never serialized into one oversized
record.

Stored metadata includes the selected root label, root-relative item path, kind, byte size,
last-modified timestamp, extension, local category, rule identifiers, and risk level. It
does not include file bytes, file content, an absolute path, browser profile data, API keys,
or tokens.

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
- Browser permission can expire and must be requested again for a new scan.
- Last-modified time is available; platform allocation size, Windows attributes, junction
  behavior, file locks, and administrator-only paths remain Desktop-only.
- IndexedDB quota and eviction policy are controlled by the browser.
