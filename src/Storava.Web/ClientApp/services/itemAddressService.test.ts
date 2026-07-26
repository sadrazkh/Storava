import { describe, expect, it } from 'vitest';
import { buildBrowserRelativeAddress } from '@/services/itemAddressService';

describe('browser-relative item addresses', () => {
  it('joins the selected root label and scanner-relative path', () => {
    expect(buildBrowserRelativeAddress('Projects', 'app/src/main.ts'))
      .toBe('Projects/app/src/main.ts');
  });

  it('does not duplicate a root segment from an older imported scan', () => {
    expect(buildBrowserRelativeAddress('Projects', 'Projects/app/src/main.ts'))
      .toBe('Projects/app/src/main.ts');
  });

  it('normalizes separators into a portable browser-relative address', () => {
    expect(buildBrowserRelativeAddress('Projects\\', '\\app\\readme.md'))
      .toBe('Projects/app/readme.md');
  });
});
