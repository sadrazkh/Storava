import { describe, expect, it } from 'vitest';
import { runMetadataHarness } from '@/performance/scanHarness';

describe('metadata performance harness', () => {
  it('measures 10k through 1m metadata workloads without claiming a fixed speed', () => {
    for (const itemCount of [10_000, 100_000, 500_000, 1_000_000]) {
      const result = runMetadataHarness(itemCount);
      console.info(`[metadata-harness] ${result.itemCount} items: ${result.elapsedMs.toFixed(2)}ms, ${result.itemsPerSecond}/s`);
      expect(result.itemCount).toBe(itemCount);
      expect(result.elapsedMs).toBeGreaterThan(0);
      expect(result.itemsPerSecond).toBeGreaterThan(0);
      expect(result.totalBytes).toBeGreaterThan(0);
    }
  });
});
