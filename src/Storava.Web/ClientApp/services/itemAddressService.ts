function normalizePortablePath(value: string): string {
  return value
    .replaceAll('\\', '/')
    .split('/')
    .filter((segment) => segment.length > 0)
    .join('/');
}

export function buildBrowserRelativeAddress(rootName: string, relativePath: string): string {
  const root = normalizePortablePath(rootName);
  const relative = normalizePortablePath(relativePath);
  if (!relative) return root;
  if (!root || relative === root || relative.startsWith(`${root}/`)) return relative;
  return `${root}/${relative}`;
}
