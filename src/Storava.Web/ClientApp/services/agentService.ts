/**
 * Talking to the companion Agent on this machine.
 *
 * The page asks the account server who the user's Agents are and for a short-lived pass, then
 * speaks to the Agent directly over loopback. Nothing about a scan goes through the server — that
 * is the whole reason the Agent is a local process rather than a service.
 *
 * Since Chrome 142 the first such request raises a Local Network Access permission prompt. The
 * caller is told when that is the likely cause so the page can explain it rather than reporting a
 * bare network error.
 */

/** Matches AgentEndpoints on the server; the ports arrive with the token rather than hard-coded here. */
export const AGENT_PRODUCT = 'storava-agent';
export const AGENT_HOST = '127.0.0.1';

export interface BrowserDevice {
  id: string;
  displayName: string;
  lastSeenAtUtc: string;
}

export interface AgentAccessToken {
  token: string;
  expiresAtUtc: string;
  ports: number[];
  protocol: number;
}

export interface AgentHello {
  product: string;
  protocol: number;
  deviceId: string;
  paired: boolean;
}

export interface AgentStatus {
  deviceId: string;
  deviceName: string;
  agentVersion: string;
  startedAtUtc: string;
}

export type AgentFailure =
  /** No Agent answered on any port — most likely it is simply not running. */
  | 'not-running'
  /** Something answered but is not a Storava Agent, or speaks a protocol this page does not. */
  | 'incompatible'
  /** An Agent answered, but it belongs to a different device than the one asked for. */
  | 'other-device'
  /** The browser refused to reach the local network, almost always the permission prompt. */
  | 'blocked'
  /** The Agent refused the pass. Usually the device was removed from the account. */
  | 'rejected'
  /** The account server would not issue a pass for that device. */
  | 'no-token'
  | 'signed-out';

export interface AgentConnection {
  baseAddress: string;
  port: number;
  status: AgentStatus;
  /** The pass used for this connection. Short-lived, held only in this page's memory. */
  token: string;
}

export interface AgentDrive {
  name: string;
  volumeLabel: string | null;
  driveFormat: string;
  totalBytes: number;
  freeBytes: number;
  isReady: boolean;
}

export type AgentScanState = 'Running' | 'Completed' | 'Cancelled' | 'Failed';

export interface AgentScanProgress {
  scanId: string;
  state: AgentScanState;
  rootPath: string;
  currentPath: string;
  files: number;
  folders: number;
  bytes: number;
  errors: number;
  elapsedSeconds: number;
  error: string | null;
}

export interface AgentScanItem {
  id: string;
  /** A real operating-system path. The browser edition cannot produce one of these. */
  path: string;
  name: string;
  isFolder: boolean;
  size: number;
  fileCount: number;
  folderCount: number;
  category: string;
  technology: string | null;
  ruleId: string | null;
  risk: string;
  isProtected: boolean;
  isReparsePoint: boolean;
  /** What the local rule catalog permits. The page offers nothing these deny. */
  canDelete: boolean;
  canMove: boolean;
}

export interface AgentProblem {
  reason: string;
  message: string;
}

/**
 * What the Agent would do, measured against the disk as it is now. Reading this is the first of
 * two steps: nothing happens until the user types {@link confirmationPhrase} back and the same
 * fingerprint is returned with it.
 */
export interface AgentActionPreview {
  stepId: string;
  action: 'delete' | 'move';
  sourcePath: string;
  destinationPath: string | null;
  measuredBytes: number;
  confirmationPhrase: string;
  fingerprint: string;
  warnings: string[];
}

export interface AgentActionOutcome {
  succeeded: boolean;
  status: string;
  bytesFreed: number;
  /** Where the original went. Removal always means the Recycle Bin. */
  recycledPath: string | null;
  linkPath: string | null;
  errorCode: string | null;
  errorMessage: string | null;
}

export type AgentResult =
  | { ok: true; connection: AgentConnection }
  | { ok: false; failure: AgentFailure };

interface PageCredentials {
  signedIn: boolean;
  headerName: string;
  token: string;
}

/** How long to wait for a port that is very likely not listening at all. */
const PROBE_TIMEOUT_MS = 1500;
const REQUEST_TIMEOUT_MS = 10000;

/** Writing an archive of a whole drive is work, not a lookup, and gets a budget to match. */
const ARCHIVE_TIMEOUT_MS = 300000;

export function readPageCredentials(root: HTMLElement | null): PageCredentials {
  return {
    signedIn: root?.dataset.signedIn === 'true',
    headerName: root?.dataset.antiforgeryHeader ?? 'X-Storava-Antiforgery',
    token: root?.dataset.antiforgeryToken ?? '',
  };
}

async function fetchWithTimeout(url: string, init: RequestInit, timeoutMs: number): Promise<Response> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(url, { ...init, signal: controller.signal });
  } finally {
    clearTimeout(timer);
  }
}

/** The user's paired Agents, as this server knows them. Empty when signed out. */
export async function listDevices(credentials: PageCredentials): Promise<BrowserDevice[]> {
  if (!credentials.signedIn) return [];

  const response = await fetchWithTimeout(
    '/api/account/devices',
    { credentials: 'same-origin', headers: { Accept: 'application/json' } },
    REQUEST_TIMEOUT_MS,
  );

  if (!response.ok) return [];
  return (await response.json()) as BrowserDevice[];
}

/** Asks the account server for a pass to one Agent. Null when it will not issue one. */
export async function requestAccessToken(
  credentials: PageCredentials,
  deviceId: string,
): Promise<AgentAccessToken | null> {
  if (!credentials.signedIn) return null;

  const response = await fetchWithTimeout(
    `/api/account/devices/${encodeURIComponent(deviceId)}/access-token`,
    {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        [credentials.headerName]: credentials.token,
      },
    },
    REQUEST_TIMEOUT_MS,
  );

  if (!response.ok) return null;
  return (await response.json()) as AgentAccessToken;
}

/**
 * Walks the ports looking for the Agent that owns this device.
 *
 * Probing is unauthenticated because it has to be: the page cannot know which port to present a
 * token to until something has answered. The probe reveals only that an Agent is there, and the
 * Agent's CORS policy keeps even that from any origin but this one.
 */
export async function discoverAgent(
  ports: number[],
  deviceId: string,
): Promise<{ port: number } | { failure: AgentFailure }> {
  let sawSomething = false;
  let blocked = false;

  for (const port of ports) {
    const base = `http://${AGENT_HOST}:${port}`;
    try {
      const response = await fetchWithTimeout(
        `${base}/v1/hello`,
        { headers: { Accept: 'application/json' } },
        PROBE_TIMEOUT_MS,
      );

      if (!response.ok) continue;

      const hello = (await response.json()) as AgentHello;
      if (hello.product !== AGENT_PRODUCT) continue;

      sawSomething = true;
      if (hello.deviceId === deviceId) return { port };
    } catch (error) {
      // A refused connection and a blocked one look almost alike from here. A blocked request
      // fails immediately and without ever reaching the socket, which is what this distinguishes.
      if (isLikelyBlocked(error)) blocked = true;
    }
  }

  if (blocked) return { failure: 'blocked' };
  return { failure: sawSomething ? 'other-device' : 'not-running' };
}

/**
 * A blocked local-network request surfaces as a TypeError, the same as an unreachable port. The
 * message is the only signal browsers give, so this stays a hint rather than a certainty and the
 * caller phrases it as one.
 */
function isLikelyBlocked(error: unknown): boolean {
  if (!(error instanceof Error)) return false;
  const message = error.message.toLowerCase();
  return (
    message.includes('local network') ||
    message.includes('private network') ||
    message.includes('permission')
  );
}

/** End to end: find the Agent for a device, get a pass, and use it. */
export async function connectToAgent(
  credentials: PageCredentials,
  deviceId: string,
): Promise<AgentResult> {
  if (!credentials.signedIn) return { ok: false, failure: 'signed-out' };

  const access = await requestAccessToken(credentials, deviceId);
  if (!access) return { ok: false, failure: 'no-token' };

  const found = await discoverAgent(access.ports, deviceId);
  if ('failure' in found) return { ok: false, failure: found.failure };

  const base = `http://${AGENT_HOST}:${found.port}`;

  let response: Response;
  try {
    response = await fetchWithTimeout(
      `${base}/v1/status`,
      { headers: { Accept: 'application/json', Authorization: `Bearer ${access.token}` } },
      REQUEST_TIMEOUT_MS,
    );
  } catch (error) {
    return { ok: false, failure: isLikelyBlocked(error) ? 'blocked' : 'not-running' };
  }

  // 401 here means the Agent has a secret that no longer matches — the usual cause is the device
  // having been removed on the account page, which destroys the secret the token was signed with.
  if (response.status === 401) return { ok: false, failure: 'rejected' };
  if (!response.ok) return { ok: false, failure: 'incompatible' };

  const status = (await response.json()) as AgentStatus;
  if (status.deviceId !== deviceId) return { ok: false, failure: 'other-device' };

  return { ok: true, connection: { baseAddress: base, port: found.port, status, token: access.token } };
}

/**
 * Everything below runs against an Agent this page has already connected to. The pass goes with
 * every call; the Agent verifies it locally and the account server is not involved.
 */
async function agentFetch(
  connection: AgentConnection,
  path: string,
  init: RequestInit = {},
): Promise<Response> {
  return fetchWithTimeout(
    `${connection.baseAddress}${path}`,
    {
      ...init,
      headers: {
        Accept: 'application/json',
        ...(init.headers ?? {}),
        Authorization: `Bearer ${connection.token}`,
      },
    },
    REQUEST_TIMEOUT_MS,
  );
}

/** The machine's drives — something a browser cannot enumerate at all. */
export async function listDrives(connection: AgentConnection): Promise<AgentDrive[]> {
  const response = await agentFetch(connection, '/v1/drives');
  if (!response.ok) return [];
  return (await response.json()) as AgentDrive[];
}

/** Asks the Agent to walk a folder. Returns the first progress report, or why it refused. */
export async function startScan(
  connection: AgentConnection,
  rootPath: string,
  mode: 'quick' | 'deep' = 'quick',
): Promise<{ progress: AgentScanProgress } | { problem: AgentProblem }> {
  const response = await agentFetch(connection, '/v1/scans', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ rootPath, mode }),
  });

  if (response.ok) return { progress: (await response.json()) as AgentScanProgress };

  try {
    return { problem: (await response.json()) as AgentProblem };
  } catch {
    return { problem: { reason: 'failed', message: `The agent replied ${response.status}.` } };
  }
}

export async function getScan(
  connection: AgentConnection,
  scanId: string,
): Promise<AgentScanProgress | null> {
  const response = await agentFetch(connection, `/v1/scans/${encodeURIComponent(scanId)}`);
  if (!response.ok) return null;
  return (await response.json()) as AgentScanProgress;
}

export async function cancelScan(connection: AgentConnection, scanId: string): Promise<void> {
  await agentFetch(connection, `/v1/scans/${encodeURIComponent(scanId)}/cancel`, { method: 'POST' });
}

/** The largest items the walk stored, each with its real path. Only after it has finished. */
export async function getScanItems(
  connection: AgentConnection,
  scanId: string,
  limit = 100,
  foldersOnly = false,
): Promise<AgentScanItem[]> {
  const query = `?limit=${limit}&foldersOnly=${foldersOnly ? 'true' : 'false'}`;
  const response = await agentFetch(
    connection,
    `/v1/scans/${encodeURIComponent(scanId)}/items${query}`,
  );

  if (!response.ok) return [];
  return ((await response.json()) as { items: AgentScanItem[] }).items;
}

/**
 * Downloads a finished walk as a portable `.storava` file.
 *
 * This is what stops an agent scan from being trapped on the machine that ran it: the archive is
 * the one format the desktop application, this page and the Agent all read, so a walk of a real
 * drive can be opened in this workspace, kept, or carried to another computer.
 *
 * Deliberately not on the ten-second budget the other calls use. Those answer from memory; this
 * one writes the whole tree, and a drive-sized walk takes longer than any of them.
 */
export async function downloadScanArchive(
  connection: AgentConnection,
  scanId: string,
): Promise<{ blob: Blob; fileName: string } | null> {
  const response = await fetchWithTimeout(
    `${connection.baseAddress}/v1/scans/${encodeURIComponent(scanId)}/archive`,
    { headers: { Authorization: `Bearer ${connection.token}` } },
    ARCHIVE_TIMEOUT_MS,
  );

  if (!response.ok) return null;

  return {
    blob: await response.blob(),
    // Exposed by the Agent's CORS policy; falling back rather than failing, because a name is a
    // convenience and the archive itself is the point.
    fileName: fileNameFrom(response.headers.get('Content-Disposition')) ?? 'storava-scan.storava',
  };
}

/** Pulls the filename out of a Content-Disposition header, preferring the encoded form. */
function fileNameFrom(header: string | null): string | null {
  if (!header) return null;

  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header)?.[1];
  if (encoded) {
    try {
      return decodeURIComponent(encoded);
    } catch {
      // A malformed header is not worth failing a download over.
    }
  }

  return /filename="?([^";]+)"?/i.exec(header)?.[1] ?? null;
}

/**
 * Asks what would happen, without anything happening. The Agent re-measures the folder and hands
 * back the phrase the user must type; nothing on disk is touched by this call.
 */
export async function previewAction(
  connection: AgentConnection,
  scanId: string,
  itemId: string,
  action: 'delete' | 'move',
  destinationPath?: string,
): Promise<{ preview: AgentActionPreview } | { problem: AgentProblem }> {
  const response = await agentFetch(connection, '/v1/actions/preview', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ scanId, itemId, action, destinationPath: destinationPath ?? null }),
  });

  if (response.ok) return { preview: (await response.json()) as AgentActionPreview };

  try {
    return { problem: (await response.json()) as AgentProblem };
  } catch {
    return { problem: { reason: 'failed', message: `The agent replied ${response.status}.` } };
  }
}

/**
 * Spends the approval. The fingerprint is echoed back exactly as it came, so changing anything
 * between reading and confirming invalidates it rather than silently carrying over.
 */
export async function executeAction(
  connection: AgentConnection,
  preview: AgentActionPreview,
  typedName: string,
): Promise<AgentActionOutcome | null> {
  const response = await agentFetch(connection, '/v1/actions/execute', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      stepId: preview.stepId,
      fingerprint: preview.fingerprint,
      typedName,
    }),
  });

  if (!response.ok && response.status !== 200) {
    // A refusal still carries an outcome when the step existed; only an unknown step has none.
    try {
      return (await response.json()) as AgentActionOutcome;
    } catch {
      return null;
    }
  }

  return (await response.json()) as AgentActionOutcome;
}
