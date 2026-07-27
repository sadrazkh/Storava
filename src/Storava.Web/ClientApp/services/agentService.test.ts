import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  connectToAgent,
  discoverAgent,
  listDevices,
  readPageCredentials,
  requestAccessToken,
} from '@/services/agentService';

const DEVICE = '11111111-2222-3333-4444-555555555555';
const OTHER_DEVICE = '99999999-8888-7777-6666-555555555555';

const credentials = { signedIn: true, headerName: 'X-Storava-Antiforgery', token: 'af-token' };

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function hello(deviceId: string) {
  return { product: 'storava-agent', protocol: 1, deviceId, paired: true };
}

function status(deviceId: string) {
  return {
    deviceId,
    deviceName: 'Workshop PC',
    agentVersion: '1.0.0.0',
    startedAtUtc: '2026-07-27T10:00:00Z',
  };
}

function tokenResponse(ports: number[] = [47615, 47616]) {
  return {
    token: 'storava1.payload.signature',
    expiresAtUtc: '2026-07-27T10:05:00Z',
    ports,
    protocol: 1,
  };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

/** fetch accepts a string, a URL or a Request; the tests only ever care about the address. */
function addressOf(input: RequestInfo | URL): string {
  if (typeof input === 'string') return input;
  if (input instanceof URL) return input.href;
  return input.url;
}

/** Routes each call by URL so a test can describe the whole machine at once. */
function stubFetch(handler: (url: string, init?: RequestInit) => Response | Promise<Response>) {
  const spy = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) =>
    handler(addressOf(input), init));
  vi.stubGlobal('fetch', spy);
  return spy;
}

describe('page credentials', () => {
  it('reads what the server rendered into the island', () => {
    const root = document.createElement('div');
    root.dataset.signedIn = 'true';
    root.dataset.antiforgeryHeader = 'X-Storava-Antiforgery';
    root.dataset.antiforgeryToken = 'abc';

    expect(readPageCredentials(root)).toEqual({
      signedIn: true,
      headerName: 'X-Storava-Antiforgery',
      token: 'abc',
    });
  });

  it('treats a missing island as signed out', () => {
    expect(readPageCredentials(null).signedIn).toBe(false);
  });
});

describe('asking the account server', () => {
  it('does not call the server at all when signed out', async () => {
    const fetchSpy = stubFetch(() => jsonResponse([]));

    const signedOut = { ...credentials, signedIn: false };
    expect(await listDevices(signedOut)).toEqual([]);
    expect(await requestAccessToken(signedOut, DEVICE)).toBeNull();
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('sends the antiforgery header when asking for a pass', async () => {
    const fetchSpy = stubFetch(() => jsonResponse(tokenResponse()));

    await requestAccessToken(credentials, DEVICE);

    const [url, init] = fetchSpy.mock.calls[0]!;
    expect(addressOf(url)).toContain(`/api/account/devices/${DEVICE}/access-token`);
    expect(init?.method).toBe('POST');
    expect((init?.headers as Record<string, string>)['X-Storava-Antiforgery']).toBe('af-token');
    // Same-origin credentials: the pass is issued against the signed-in session.
    expect(init?.credentials).toBe('same-origin');
  });

  it('returns nothing when the server will not issue a pass', async () => {
    stubFetch(() => new Response(null, { status: 404 }));

    expect(await requestAccessToken(credentials, DEVICE)).toBeNull();
  });
});

describe('finding the agent', () => {
  it('returns the port whose agent owns this device', async () => {
    stubFetch((url) => {
      if (url.includes(':47615')) return jsonResponse(hello(OTHER_DEVICE));
      if (url.includes(':47616')) return jsonResponse(hello(DEVICE));
      return new Response(null, { status: 404 });
    });

    expect(await discoverAgent([47615, 47616], DEVICE)).toEqual({ port: 47616 });
  });

  it('reports nothing running when no port answers', async () => {
    stubFetch(() => {
      throw new TypeError('Failed to fetch');
    });

    expect(await discoverAgent([47615, 47616], DEVICE)).toEqual({ failure: 'not-running' });
  });

  it('distinguishes an agent for another device from no agent at all', async () => {
    stubFetch(() => jsonResponse(hello(OTHER_DEVICE)));

    // The fix for this is different from the fix for a stopped agent, so the page must not
    // collapse the two into one message.
    expect(await discoverAgent([47615], DEVICE)).toEqual({ failure: 'other-device' });
  });

  it('reports a blocked local-network request separately', async () => {
    stubFetch(() => {
      throw new TypeError('Local network access was blocked by the user');
    });

    expect(await discoverAgent([47615], DEVICE)).toEqual({ failure: 'blocked' });
  });

  it('ignores something else listening on the port', async () => {
    stubFetch(() => jsonResponse({ product: 'some-other-tool', protocol: 1, deviceId: DEVICE }));

    expect(await discoverAgent([47615], DEVICE)).toEqual({ failure: 'not-running' });
  });
});

describe('connecting end to end', () => {
  it('presents the pass as a bearer token and reports the agent status', async () => {
    const fetchSpy = stubFetch((url) => {
      if (url.includes('access-token')) return jsonResponse(tokenResponse([47615]));
      if (url.includes('/v1/hello')) return jsonResponse(hello(DEVICE));
      if (url.includes('/v1/status')) return jsonResponse(status(DEVICE));
      return new Response(null, { status: 404 });
    });

    const result = await connectToAgent(credentials, DEVICE);

    expect(result).toMatchObject({
      ok: true,
      connection: { port: 47615, baseAddress: 'http://127.0.0.1:47615' },
    });

    const statusCall = fetchSpy.mock.calls.find(([url]) => addressOf(url).includes('/v1/status'))!;
    const headers = statusCall[1]?.headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer storava1.payload.signature');
  });

  it('never reaches the agent when the server refuses a pass', async () => {
    const fetchSpy = stubFetch(() => new Response(null, { status: 404 }));

    expect(await connectToAgent(credentials, DEVICE)).toEqual({ ok: false, failure: 'no-token' });
    expect(fetchSpy.mock.calls.every(([url]) => !addressOf(url).includes('127.0.0.1'))).toBe(true);
  });

  it('reports a refused pass as its own problem', async () => {
    stubFetch((url) => {
      if (url.includes('access-token')) return jsonResponse(tokenResponse([47615]));
      if (url.includes('/v1/hello')) return jsonResponse(hello(DEVICE));
      return new Response(null, { status: 401 });
    });

    // The usual cause is the device having been removed, which destroys the signing secret.
    expect(await connectToAgent(credentials, DEVICE)).toEqual({ ok: false, failure: 'rejected' });
  });

  it('refuses an agent that answers with a different device than it advertised', async () => {
    stubFetch((url) => {
      if (url.includes('access-token')) return jsonResponse(tokenResponse([47615]));
      if (url.includes('/v1/hello')) return jsonResponse(hello(DEVICE));
      if (url.includes('/v1/status')) return jsonResponse(status(OTHER_DEVICE));
      return new Response(null, { status: 404 });
    });

    expect(await connectToAgent(credentials, DEVICE)).toEqual({ ok: false, failure: 'other-device' });
  });

  it('says signed out rather than trying anything', async () => {
    const fetchSpy = stubFetch(() => jsonResponse({}));

    expect(await connectToAgent({ ...credentials, signedIn: false }, DEVICE))
      .toEqual({ ok: false, failure: 'signed-out' });
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('only ever talks to loopback and this origin', async () => {
    const fetchSpy = stubFetch((url) => {
      if (url.includes('access-token')) return jsonResponse(tokenResponse([47615]));
      if (url.includes('/v1/hello')) return jsonResponse(hello(DEVICE));
      if (url.includes('/v1/status')) return jsonResponse(status(DEVICE));
      return new Response(null, { status: 404 });
    });

    await connectToAgent(credentials, DEVICE);

    // The boundary this whole design exists to keep: no scan traffic to a third host.
    for (const [url] of fetchSpy.mock.calls) {
      const target = addressOf(url);
      expect(target.startsWith('/api/') || target.startsWith('http://127.0.0.1:')).toBe(true);
    }
  });
});
