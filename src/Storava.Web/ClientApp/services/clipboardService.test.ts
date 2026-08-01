import { afterEach, describe, expect, it, vi } from 'vitest';
import { copyText } from '@/services/clipboardService';

describe('copying a path', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  function stubClipboard(writeText: (value: string) => Promise<void>, secure = true) {
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } });
    vi.stubGlobal('window', { ...window, isSecureContext: secure });
  }

  it('uses the clipboard api when it can', async () => {
    const written: string[] = [];
    stubClipboard((value) => { written.push(value); return Promise.resolve(); });

    expect(await copyText('root/some/long/path')).toBe(true);
    expect(written).toEqual(['root/some/long/path']);
  });

  it('refuses to copy nothing', async () => {
    const written: string[] = [];
    stubClipboard((value) => { written.push(value); return Promise.resolve(); });

    expect(await copyText('')).toBe(false);
    expect(written).toEqual([]);
  });

  /**
   * The clipboard API needs a secure context. A deployment on plain http is the one most likely to
   * be somebody's own machine on a LAN, and it should still have a copy button.
   */
  it('falls back when the page is not a secure context', async () => {
    let apiCalled = false;
    stubClipboard(() => { apiCalled = true; return Promise.resolve(); }, false);
    const exec = vi.fn().mockReturnValue(true);
    vi.stubGlobal('document', Object.assign(document, { execCommand: exec }));

    expect(await copyText('root/a')).toBe(true);
    expect(apiCalled).toBe(false);
    expect(exec).toHaveBeenCalledWith('copy');
  });

  /** Denied permission or an unfocused document; the older route may still work. */
  it('falls back when the clipboard api refuses', async () => {
    stubClipboard(() => Promise.reject(new Error('not allowed')));
    const exec = vi.fn().mockReturnValue(true);
    vi.stubGlobal('document', Object.assign(document, { execCommand: exec }));

    expect(await copyText('root/a')).toBe(true);
    expect(exec).toHaveBeenCalledWith('copy');
  });

  it('says so when neither route works', async () => {
    stubClipboard(() => Promise.reject(new Error('not allowed')));
    vi.stubGlobal('document', Object.assign(document, { execCommand: vi.fn().mockReturnValue(false) }));

    expect(await copyText('root/a')).toBe(false);
  });

  /** The off-screen element must not be left behind, however the copy ended. */
  it('leaves nothing in the document afterwards', async () => {
    stubClipboard(() => Promise.reject(new Error('not allowed')));
    vi.stubGlobal('document', Object.assign(document, { execCommand: vi.fn().mockReturnValue(true) }));

    const before = document.body.children.length;
    await copyText('root/a');

    expect(document.body.children.length).toBe(before);
  });
});
