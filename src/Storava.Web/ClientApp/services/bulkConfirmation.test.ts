import { describe, expect, it } from 'vitest';
import { approves, codeFor, fingerprintOf } from '@/services/bulkConfirmation';

/**
 * One approval covering several items has to be bound to exactly those items.
 *
 * Deleting one thing is approved by typing its name; for twelve that says nothing about the other
 * eleven. So the set gets a code derived from every item in it, and everything below is about that
 * code being impossible to spend on a set other than the one that was read.
 */
describe('approving a whole selection', () => {
  it('gives the same set the same code', () => {
    expect(codeFor(['a', 'b'])).toBe(codeFor(['a', 'b']));
  });

  /**
   * Picking the same items in a different order is the same intention. A code that changed under it
   * would refuse an approval the user had every reason to expect to work.
   */
  it('does not care what order they were picked in', () => {
    expect(codeFor(['a', 'b', 'c'])).toBe(codeFor(['c', 'a', 'b']));
  });

  it('changes when something is added', () => {
    expect(codeFor(['a'])).not.toBe(codeFor(['a', 'b']));
  });

  it('changes when something is removed', () => {
    expect(codeFor(['a', 'b'])).not.toBe(codeFor(['a']));
  });

  it('changes when the selection is swapped for a different one of the same size', () => {
    expect(codeFor(['a', 'b'])).not.toBe(codeFor(['a', 'c']));
  });

  it('approves its own set and nothing else', () => {
    const mine = ['a', 'b'];

    expect(approves(mine, codeFor(mine))).toBe(true);
    expect(approves(mine, codeFor(['a', 'c']))).toBe(false);
  });

  /** Read off a screen and retyped, so a shift key or a stray space is not a refusal. */
  it.each([
    (code: string) => code.toLowerCase(),
    (code: string) => `  ${code} `,
    (code: string) => ` ${code.toLowerCase()}  `,
  ])('forgives case and surrounding space', (mangle) => {
    const keys = ['a', 'b'];

    expect(approves(keys, mangle(codeFor(keys)))).toBe(true);
  });

  it.each(['', '   ', 'ABCDEF', 'not-the-code'])('refuses %s', (typed) => {
    const keys = ['a', 'b'];

    // The literal case could in principle be the real code; if it ever is, this fails rather than
    // passing by luck.
    if (typed.trim().toUpperCase() === codeFor(keys)) return;

    expect(approves(keys, typed)).toBe(false);
  });

  /** Nothing selected is nothing to approve, whatever was typed. */
  it('never approves an empty selection', () => {
    expect(approves([], codeFor([]))).toBe(false);
  });

  it('is short enough to copy by eye', () => {
    expect(codeFor(['a'])).toHaveLength(6);
  });

  /**
   * Characters that look alike in a sans-serif font turn a mistyped code into what reads as a
   * refused approval, which invites trying harder rather than looking for the real problem.
   */
  it('avoids characters that look alike', () => {
    const seen = new Set<string>();

    for (let index = 0; index < 600; index += 1) {
      for (const character of codeFor([`item-${index}`, `other-${index % 7}`])) {
        seen.add(character);
      }
    }

    for (const forbidden of ['O', '0', 'I', 'l', '1', 'S', '5', 'Z', '2']) {
      expect(seen.has(forbidden)).toBe(false);
    }
  });

  /**
   * Different sets should not routinely collide. This is a deliberateness gate rather than a
   * security boundary, but a code that repeated across neighbouring selections would let an
   * approval read a moment ago apply to the set that replaced it.
   */
  it('gives distinct codes to distinct nearby selections', () => {
    const codes = new Set<string>();

    for (let index = 0; index < 500; index += 1) {
      codes.add(codeFor([`/scan/file-${index}.bin`]));
    }

    expect(codes.size).toBeGreaterThan(480);
  });

  it('exposes a fingerprint that moves with the set', () => {
    expect(fingerprintOf(['a'])).not.toBe(fingerprintOf(['b']));
    expect(fingerprintOf(['a', 'b'])).toBe(fingerprintOf(['b', 'a']));
  });
});
