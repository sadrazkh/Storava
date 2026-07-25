import { categorize } from '@/services/categoryService';
import { evaluateRules } from '@/rules/ruleEngine';

export interface HarnessResult {
  itemCount: number;
  elapsedMs: number;
  itemsPerSecond: number;
  totalBytes: number;
}

export function runMetadataHarness(itemCount: number): HarnessResult {
  const started = performance.now();
  let totalBytes = 0;
  for (let index = 0; index < itemCount; index += 1) {
    const name = index % 17 === 0 ? `archive-${index}.zip` : `file-${index}.dat`;
    const size = (index % 10_000) * 1024;
    const item = {
      name,
      kind: 'file' as const,
      size,
      modifiedAt: Date.now() - (index % 500) * 86_400_000,
      relativePath: `root/${index % 1000}/${name}`,
      extension: name.split('.').pop() ?? '',
    };
    categorize(name, 'file');
    evaluateRules(item);
    totalBytes += size;
  }
  const elapsedMs = Math.max(0.01, performance.now() - started);
  return { itemCount, elapsedMs, itemsPerSecond: Math.round(itemCount * 1000 / elapsedMs), totalBytes };
}
