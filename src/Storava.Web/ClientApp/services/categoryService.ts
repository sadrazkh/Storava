const categoryExtensions: Record<string, Set<string>> = {
  documents: new Set(['pdf', 'doc', 'docx', 'txt', 'md', 'rtf', 'odt', 'xls', 'xlsx', 'csv', 'ppt', 'pptx']),
  media: new Set(['jpg', 'jpeg', 'png', 'gif', 'webp', 'svg', 'heic', 'mp3', 'wav', 'flac', 'mp4', 'mkv', 'mov', 'avi']),
  archives: new Set(['zip', '7z', 'rar', 'tar', 'gz', 'bz2', 'xz', 'iso']),
  code: new Set(['cs', 'ts', 'tsx', 'js', 'jsx', 'vue', 'py', 'java', 'go', 'rs', 'cpp', 'h', 'html', 'css', 'json', 'xml', 'yml', 'yaml']),
  applications: new Set(['exe', 'msi', 'appx', 'dmg', 'pkg', 'apk', 'deb', 'rpm']),
};

export function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot > 0 && dot < name.length - 1 ? name.slice(dot + 1).toLowerCase() : '';
}

export function categorize(name: string, kind: 'file' | 'folder'): string {
  if (kind === 'folder') return 'folders';
  const extension = extensionOf(name);
  for (const [category, extensions] of Object.entries(categoryExtensions)) {
    if (extensions.has(extension)) return category;
  }
  return 'other';
}
