/**
 * Putting a path on the clipboard.
 *
 * A page cannot open Explorer — no browser will let it, and offering a button that cannot work
 * would be worse than not offering one. Copying is the half that is possible, and it is the half
 * that matters when the address is long enough that reading it off the screen and retyping it is
 * the alternative.
 *
 * What gets copied is the root-relative address, because that is all this edition has. The browser
 * never learns where the chosen folder sits on the disk, which is the whole privacy shape of the
 * web client — so "copy path" here means "copy as much of the path as exists in this page".
 */

/** True when the modern clipboard API is usable: it needs a secure context, and http is not one. */
function hasAsyncClipboard(): boolean {
  return typeof navigator !== 'undefined'
    && typeof navigator.clipboard?.writeText === 'function'
    && window.isSecureContext;
}

/**
 * The pre-clipboard-API route, kept for an origin served over plain http.
 *
 * Deliberately not the first choice — it moves focus and briefly puts an element in the document —
 * but a deployment behind a plain http address would otherwise have no copy button at all, and
 * that is the deployment most likely to be somebody's own machine on a LAN.
 */
function copyWithSelection(text: string): boolean {
  const holder = document.createElement('textarea');
  holder.value = text;

  // Off-screen rather than hidden: a display:none element cannot be selected.
  holder.setAttribute('readonly', '');
  holder.style.position = 'fixed';
  holder.style.top = '-1000px';
  holder.style.opacity = '0';

  document.body.appendChild(holder);
  try {
    holder.select();
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    holder.remove();
  }
}

export async function copyText(text: string): Promise<boolean> {
  if (!text) return false;

  if (hasAsyncClipboard()) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Denied by permissions policy, or the document was not focused. Worth trying the old way
      // before telling the user it cannot be done.
    }
  }

  return copyWithSelection(text);
}
