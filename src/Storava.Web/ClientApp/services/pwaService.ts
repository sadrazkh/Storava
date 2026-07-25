export async function registerServiceWorker(): Promise<void> {
  if (!('serviceWorker' in navigator) || !window.isSecureContext) return;

  try {
    await navigator.serviceWorker.register('/service-worker.js', { scope: '/' });
  } catch {
    // The online application remains fully usable if shell caching is unavailable.
  }
}
