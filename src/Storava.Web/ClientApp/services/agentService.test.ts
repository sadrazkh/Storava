import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  cancelScan,
  connectToAgent,
  discoverAgent,
  downloadScanArchive,
  getScan,
  getScanItems,
  listDevices,
  executeAction,
  listDrives,
  previewAction,
  readPageCredentials,
  requestAccessToken,
  startScan,
  type AgentConnection,
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

describe('using a connected agent', () => {
  const connection: AgentConnection = {
    baseAddress: 'http://127.0.0.1:47615',
    port: 47615,
    token: 'storava1.payload.signature',
    status: {
      deviceId: DEVICE,
      deviceName: 'Workshop PC',
      agentVersion: '1.0.0.0',
      startedAtUtc: '2026-07-27T10:00:00Z',
    },
  };

  it('carries the pass on every call to the agent', async () => {
    const fetchSpy = stubFetch(() => jsonResponse([]));

    await listDrives(connection);
    await getScan(connection, 'abc');
    await cancelScan(connection, 'abc');
    await getScanItems(connection, 'abc');

    expect(fetchSpy.mock.calls).toHaveLength(4);
    for (const [url, init] of fetchSpy.mock.calls) {
      expect(addressOf(url).startsWith('http://127.0.0.1:47615')).toBe(true);
      expect((init?.headers as Record<string, string>).Authorization)
        .toBe('Bearer storava1.payload.signature');
    }
  });

  it('lists the drives the agent reports', async () => {
    stubFetch(() => jsonResponse([
      { name: 'C:\\', volumeLabel: 'System', driveFormat: 'NTFS', totalBytes: 1000, freeBytes: 400, isReady: true },
    ]));

    const drives = await listDrives(connection);

    // Something a browser cannot enumerate at all.
    expect(drives).toHaveLength(1);
    expect(drives[0]!.name).toBe('C:\\');
  });

  it('sends the folder and mode when starting a walk', async () => {
    const fetchSpy = stubFetch(() => jsonResponse({ scanId: 's1', state: 'Running' }));

    const started = await startScan(connection, 'C:\\projects', 'deep');

    expect(started).toHaveProperty('progress');
    const [url, init] = fetchSpy.mock.calls[0]!;
    expect(addressOf(url)).toBe('http://127.0.0.1:47615/v1/scans');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(init?.body as string)).toEqual({ rootPath: 'C:\\projects', mode: 'deep' });
  });

  it('surfaces the agent’s reason for refusing a folder', async () => {
    stubFetch(() => jsonResponse({ reason: 'not_found', message: 'There is no folder at that path.' }, 400));

    const started = await startScan(connection, 'C:\\nope');

    // The page shows the agent's own words rather than inventing a generic failure.
    expect(started).toEqual({
      problem: { reason: 'not_found', message: 'There is no folder at that path.' },
    });
  });

  it('returns items carrying real operating-system paths', async () => {
    stubFetch(() => jsonResponse({
      scanId: 's1',
      items: [{
        id: 'i1',
        path: 'C:\\projects\\app\\node_modules',
        name: 'node_modules',
        isFolder: true,
        size: 5000,
        fileCount: 20,
        folderCount: 3,
        category: 'PackageCache',
        technology: 'npm',
        ruleId: 'npm.node_modules',
        risk: 'Low',
        isProtected: false,
        isReparsePoint: false,
      }],
    }));

    const items = await getScanItems(connection, 's1');

    expect(items[0]!.path).toBe('C:\\projects\\app\\node_modules');
    expect(items[0]!.ruleId).toBe('npm.node_modules');
  });

  it('asks for folders only when told to', async () => {
    const fetchSpy = stubFetch(() => jsonResponse({ scanId: 's1', items: [] }));

    await getScanItems(connection, 's1', 25, true);

    expect(addressOf(fetchSpy.mock.calls[0]![0])).toContain('limit=25&foldersOnly=true');
  });

  it('reports nothing rather than throwing when the agent has no results yet', async () => {
    stubFetch(() => new Response(null, { status: 404 }));

    expect(await getScanItems(connection, 's1')).toEqual([]);
    expect(await getScan(connection, 's1')).toBeNull();
    expect(await listDrives(connection)).toEqual([]);
  });

  // The archive is what stops a walk from living only as long as the connection that ran it.

  it('fetches the whole walk as a file, with the pass and the name the agent chose', async () => {
    const fetchSpy = stubFetch(() => new Response(new Blob(['PKarchive-bytes']), {
      status: 200,
      headers: {
        'Content-Type': 'application/octet-stream',
        'Content-Disposition': 'attachment; filename="storava-project.storava"',
      },
    }));

    const archive = await downloadScanArchive(connection, 's1');

    expect(archive?.fileName).toBe('storava-project.storava');
    expect(await archive?.blob.text()).toContain('archive-bytes');

    const [url, init] = fetchSpy.mock.calls[0]!;
    expect(addressOf(url)).toBe('http://127.0.0.1:47615/v1/scans/s1/archive');
    expect((init?.headers as Record<string, string>).Authorization)
      .toBe('Bearer storava1.payload.signature');
  });

  it('reads a name that had to be encoded', async () => {
    stubFetch(() => new Response(new Blob(['x']), {
      status: 200,
      headers: {
        // What the agent sends when the folder's name is not plain ASCII — a Persian folder name
        // is the ordinary case here, not an exotic one.
        'Content-Disposition':
          "attachment; filename=\"storava-scan.storava\"; filename*=UTF-8''storava-%D9%BE%D8%B1%D9%88%DA%98%D9%87.storava",
      },
    }));

    const archive = await downloadScanArchive(connection, 's1');

    expect(archive?.fileName).toBe('storava-پروژه.storava');
  });

  it('still produces a usable name when the agent sends none', async () => {
    stubFetch(() => new Response(new Blob(['x']), { status: 200 }));

    expect((await downloadScanArchive(connection, 's1'))?.fileName).toBe('storava-scan.storava');
  });

  it('returns nothing rather than an empty file when the walk has no archive', async () => {
    stubFetch(() => new Response(null, { status: 404 }));

    expect(await downloadScanArchive(connection, 's1')).toBeNull();
  });
});

describe('acting on what the agent found', () => {
  const connection: AgentConnection = {
    baseAddress: 'http://127.0.0.1:47615',
    port: 47615,
    token: 'storava1.payload.signature',
    status: {
      deviceId: DEVICE,
      deviceName: 'Workshop PC',
      agentVersion: '1.0.0.0',
      startedAtUtc: '2026-07-27T10:00:00Z',
    },
  };

  const preview = {
    stepId: 'step-1',
    action: 'delete' as const,
    sourcePath: 'C:\\projects\\app\\node_modules',
    destinationPath: null,
    measuredBytes: 4096,
    confirmationPhrase: 'node_modules',
    fingerprint: 'abc123',
    warnings: ['high_risk'],
  };

  it('asks what would happen without asking for it to happen', async () => {
    const fetchSpy = stubFetch(() => jsonResponse(preview));

    const asked = await previewAction(connection, 'scan-1', 'item-1', 'delete');

    expect(asked).toEqual({ preview });
    const [url, init] = fetchSpy.mock.calls[0]!;
    expect(addressOf(url)).toBe('http://127.0.0.1:47615/v1/actions/preview');
    expect(JSON.parse(init?.body as string)).toEqual({
      scanId: 'scan-1',
      itemId: 'item-1',
      action: 'delete',
      destinationPath: null,
    });
  });

  it('passes the destination through for a move', async () => {
    const fetchSpy = stubFetch(() => jsonResponse({ ...preview, action: 'move' }));

    await previewAction(connection, 'scan-1', 'item-1', 'move', 'D:\\caches\\nuget');

    const sent = JSON.parse(fetchSpy.mock.calls[0]![1]?.body as string) as { destinationPath: string };
    expect(sent.destinationPath)
      .toBe('D:\\caches\\nuget');
  });

  it('surfaces the agent’s reason for refusing an action', async () => {
    stubFetch(() => jsonResponse(
      { reason: 'not_permitted', message: 'The local rules do not permit deleting this item.' },
      400,
    ));

    const asked = await previewAction(connection, 'scan-1', 'item-1', 'delete');

    expect(asked).toEqual({
      problem: { reason: 'not_permitted', message: 'The local rules do not permit deleting this item.' },
    });
  });

  it('echoes back the fingerprint it was given, never one of its own', async () => {
    const fetchSpy = stubFetch(() => jsonResponse({
      succeeded: true,
      status: 'Completed',
      bytesFreed: 4096,
      recycledPath: preview.sourcePath,
      linkPath: null,
      errorCode: null,
      errorMessage: null,
    }));

    const done = await executeAction(connection, preview, 'node_modules');

    expect(done?.succeeded).toBe(true);
    // Binding the approval to what was on screen is the whole point; the page must not compute
    // or alter the fingerprint.
    expect(JSON.parse(fetchSpy.mock.calls[0]![1]?.body as string)).toEqual({
      stepId: 'step-1',
      fingerprint: 'abc123',
      typedName: 'node_modules',
    });
  });

  it('reports a refusal as an outcome rather than a thrown error', async () => {
    stubFetch(() => jsonResponse({
      succeeded: false,
      status: 'Pending',
      bytesFreed: 0,
      recycledPath: null,
      linkPath: null,
      errorCode: 'exec.not_confirmed',
      errorMessage: 'This step has not been confirmed.',
    }, 200));

    const done = await executeAction(connection, preview, 'wrong');

    expect(done?.succeeded).toBe(false);
    expect(done?.errorCode).toBe('exec.not_confirmed');
    expect(done?.bytesFreed).toBe(0);
  });

  it('returns nothing when the step is no longer waiting', async () => {
    stubFetch(() => new Response(null, { status: 404 }));

    expect(await executeAction(connection, preview, 'node_modules')).toBeNull();
  });
});
