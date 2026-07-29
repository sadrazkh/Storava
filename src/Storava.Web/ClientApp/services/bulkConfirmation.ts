/**
 * Binds one approval to a whole set of items.
 *
 * Deleting one item is approved by typing its own name, which works because there is one name and
 * it is on screen. For a set that gate does nothing — typing one name out of twelve says nothing
 * about the other eleven — so the set is approved by a short code derived from every item in it.
 * Change the selection and the code changes, so an approval cannot be spent on a different set
 * than the one that was read.
 *
 * What this is not: a security boundary. The code is computed and checked in the same page, so it
 * stops a reflexive click rather than a determined caller — which is the same thing typing a folder
 * name ever did. The agent's version of this is checked by the agent, across a process boundary,
 * and that one is load-bearing.
 */

/**
 * Six characters, from an alphabet with no pairs that look alike in a sans-serif font.
 *
 * I, l and 1 are missing, as are O and 0, S and 5, Z and 2. Somebody copying a code by eye should
 * not be able to fail at it: a mistyped code reads as a refused approval, and the natural response
 * to that is to try harder rather than to look for the real problem.
 */
const alphabet = 'ABCDEFGHJKMNPQRTUVWXY346789';

const length = 6;

/**
 * A stable fingerprint of the set, independent of the order it was picked in.
 *
 * Sorted on purpose: selecting the same three items in a different order is the same intention, and
 * a code that changed under it would refuse an approval the user had every reason to expect to work.
 * This is the opposite of the agent's plan code, which keeps order because there a move that frees a
 * drive before another fills it is genuinely a different plan.
 */
export function fingerprintOf(keys: readonly string[]): string {
  const material = [...keys].sort().join('\n');

  // FNV-1a. Not a cryptographic hash and not pretending to be: this exists so that a changed
  // selection produces a visibly different code, and the check it feeds happens in this same page.
  let hash = 0x811c9dc5;
  for (let index = 0; index < material.length; index += 1) {
    hash ^= material.charCodeAt(index);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }

  return hash.toString(16).padStart(8, '0');
}

/** The code the user types, derived from the fingerprint so it moves with the selection. */
export function codeFor(keys: readonly string[]): string {
  let hash = Number.parseInt(fingerprintOf(keys), 16);
  let code = '';

  for (let index = 0; index < length; index += 1) {
    code += alphabet[hash % alphabet.length];
    hash = Math.floor(hash / alphabet.length) + index * 7919;
  }

  return code;
}

/**
 * Whether what was typed approves this set.
 *
 * Case and surrounding space are forgiven: the code is read off a screen and retyped, and refusing
 * it over a shift key would teach nobody anything.
 */
export function approves(keys: readonly string[], typed: string): boolean {
  return keys.length > 0 && typed.trim().toUpperCase() === codeFor(keys);
}
