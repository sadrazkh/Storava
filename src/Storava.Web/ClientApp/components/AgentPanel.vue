<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { usePreferences } from '@/composables/usePreferences';
import { getAgentMessages } from '@/localization/agentMessages';
import { downloadExport } from '@/services/exportImportService';
import {
  cancelScan,
  connectToAgent,
  downloadScanArchive,
  executeAction,
  getScan,
  getScanItems,
  listDevices,
  listDrives,
  previewAction,
  readPageCredentials,
  startScan,
  type AgentActionOutcome,
  type AgentActionPreview,
  type AgentConnection,
  type AgentDrive,
  type AgentFailure,
  type AgentScanItem,
  type AgentScanProgress,
  type BrowserDevice,
} from '@/services/agentService';

const emit = defineEmits<{ (event: 'open-archive', file: File): void }>();

const { locale } = usePreferences();
const copy = computed(() => getAgentMessages(locale.value));

const credentials = readPageCredentials(document.querySelector<HTMLElement>('[data-vue-island="scan"]'));
const devices = ref<BrowserDevice[]>([]);
const selectedDeviceId = ref('');
const connection = ref<AgentConnection | null>(null);
const failure = ref<AgentFailure | null>(null);
const isConnecting = ref(false);
const hasLoadedDevices = ref(false);

const selectedDevice = computed(() => devices.value.find((device) => device.id === selectedDeviceId.value) ?? null);

/**
 * Every refusal gets its own heading and its own next step. "Could not connect" would be true of
 * all of them and useful for none — the fix for a stopped Agent is nothing like the fix for a
 * removed device.
 */
const problem = computed(() => {
  switch (failure.value) {
    case 'blocked':
      return { title: copy.value.permissionTitle, body: copy.value.permissionBody };
    case 'not-running':
      return { title: copy.value.notRunningTitle, body: copy.value.notRunningBody };
    case 'other-device':
      return { title: copy.value.otherDeviceTitle, body: copy.value.otherDeviceBody };
    case 'rejected':
      return { title: copy.value.rejectedTitle, body: copy.value.rejectedBody };
    case 'no-token':
      return { title: copy.value.noTokenTitle, body: copy.value.noTokenBody };
    case 'incompatible':
      return { title: copy.value.incompatibleTitle, body: copy.value.incompatibleBody };
    default:
      return null;
  }
});

const connectedBody = computed(() => {
  if (!connection.value) return '';
  return copy.value.connectedBody
    .replace('{name}', connection.value.status.deviceName)
    .replace('{port}', String(connection.value.port));
});

const drives = ref<AgentDrive[]>([]);
const rootPath = ref('');
const deep = ref(false);
const scan = ref<AgentScanProgress | null>(null);
const scanProblem = ref<string | null>(null);
const results = ref<AgentScanItem[]>([]);
const foldersOnly = ref(false);
const copiedPath = ref('');

let poller: number | null = null;

const isScanning = computed(() => scan.value?.state === 'Running');
const canScan = computed(() => Boolean(connection.value) && rootPath.value.trim().length > 0 && !isScanning.value);

const scanHeading = computed(() => {
  switch (scan.value?.state) {
    case 'Completed': return copy.value.scanDoneTitle;
    case 'Cancelled': return copy.value.scanCancelledTitle;
    case 'Failed': return copy.value.scanFailedTitle;
    default: return '';
  }
});

function formatMoment(value: string): string {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString(locale.value);
}

/** Binary units, matching how the rest of the workspace reports sizes. */
function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB', 'PB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(value >= 100 ? 0 : 1)} ${units[unit]}`;
}

function fill(template: string, values: Record<string, string>): string {
  return Object.entries(values).reduce(
    (text, [name, value]) => text.replaceAll(`{${name}}`, value),
    template,
  );
}

function describeItem(item: AgentScanItem): string {
  if (item.technology) return item.technology;
  return item.category;
}

async function copyPath(path: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(path);
    copiedPath.value = path;
    setTimeout(() => {
      if (copiedPath.value === path) copiedPath.value = '';
    }, 2000);
  } catch {
    // Clipboard permission can be refused; the path is on screen either way.
  }
}

function stopPolling(): void {
  if (poller !== null) {
    clearInterval(poller);
    poller = null;
  }
}

/**
 * Polls the walk rather than streaming it. The numbers are cumulative, so a missed tick costs
 * nothing, and there is no stream to reconnect when the page is backgrounded.
 */
function followScan(): void {
  stopPolling();
  poller = window.setInterval(() => void tick(), 500);
}

async function tick(): Promise<void> {
  const current = connection.value;
  const running = scan.value;
  if (!current || !running) {
    stopPolling();
    return;
  }

  const next = await getScan(current, running.scanId);
  if (!next) {
    stopPolling();
    return;
  }

  scan.value = next;
  if (next.state !== 'Running') {
    stopPolling();
    if (next.state === 'Completed') await loadResults();
  }
}

/**
 * The walk as a portable file. Everything above shows the top hundred rows over a live connection;
 * this is the whole tree, in the format the desktop application and this page both read — so an
 * agent scan can be kept, opened here, or carried to another machine rather than living only for
 * as long as the Agent is running.
 */
const archiveState = ref<'idle' | 'writing'>('idle');
const archiveProblem = ref<string | null>(null);

async function takeArchive(then: 'save' | 'open'): Promise<void> {
  const current = connection.value;
  const finished = scan.value;
  if (!current || finished?.state !== 'Completed' || archiveState.value === 'writing') return;

  archiveState.value = 'writing';
  archiveProblem.value = null;

  try {
    const archive = await downloadScanArchive(current, finished.scanId);
    if (!archive) {
      archiveProblem.value = copy.value.archiveFailed;
      return;
    }

    if (then === 'save') {
      downloadExport(archive.blob, archive.fileName);
      return;
    }

    // Handed up as a File rather than imported here: this panel talks to the Agent, and the
    // workspace that holds imported scans belongs to the page around it.
    emit('open-archive', new File([archive.blob], archive.fileName));
  } catch {
    archiveProblem.value = copy.value.archiveFailed;
  } finally {
    archiveState.value = 'idle';
  }
}

async function loadResults(): Promise<void> {
  const current = connection.value;
  const finished = scan.value;
  if (!current || !finished || finished.state !== 'Completed') return;

  results.value = await getScanItems(current, finished.scanId, 100, foldersOnly.value);
}

async function beginScan(): Promise<void> {
  const current = connection.value;
  if (!current || !canScan.value) return;

  scanProblem.value = null;
  archiveProblem.value = null;
  results.value = [];
  outcome.value = null;
  resultsAreStale.value = false;

  const started = await startScan(current, rootPath.value.trim(), deep.value ? 'deep' : 'quick');
  if ('problem' in started) {
    scanProblem.value = started.problem.message;
    return;
  }

  scan.value = started.progress;
  followScan();
}

async function stopScan(): Promise<void> {
  const current = connection.value;
  const running = scan.value;
  if (current && running) await cancelScan(current, running.scanId);
}

async function loadDrives(): Promise<void> {
  const current = connection.value;
  if (!current) return;

  drives.value = await listDrives(current);
  const first = drives.value.find((drive) => drive.isReady);
  if (!rootPath.value && first) rootPath.value = first.name;
}

onBeforeUnmount(stopPolling);

// --- Acting on what was found ------------------------------------------------
// Two steps on purpose. The preview measures the folder now and states what would happen; nothing
// is touched until the user types the folder's own name back.

const pending = ref<AgentActionPreview | null>(null);
const pendingItem = ref<AgentScanItem | null>(null);
const typedName = ref('');
const moveDestination = ref('');
const actionProblem = ref<string | null>(null);
const outcome = ref<AgentActionOutcome | null>(null);
const isActing = ref(false);

/** True once something has been acted on, so the table no longer matches the disk. */
const resultsAreStale = ref(false);

const confirmationSatisfied = computed(() =>
  pending.value !== null && typedName.value.trim() === pending.value.confirmationPhrase);

const warningText = computed(() => (pending.value?.warnings ?? []).map((warning) => {
  switch (warning) {
    case 'grew_since_scan': return copy.value.warnGrew;
    case 'shrank_since_scan': return copy.value.warnShrank;
    case 'high_risk': return copy.value.warnHighRisk;
    case 'junction_left_behind': return copy.value.warnJunction;
    default: return warning;
  }
}));

function closeConfirmation(): void {
  pending.value = null;
  pendingItem.value = null;
  typedName.value = '';
  moveDestination.value = '';
  actionProblem.value = null;
}

/** Asks what would happen. Repeated whenever the destination changes, so the fingerprint tracks it. */
async function askPreview(item: AgentScanItem, action: 'delete' | 'move'): Promise<void> {
  const current = connection.value;
  const finished = scan.value;
  if (!current || !finished) return;

  actionProblem.value = null;
  outcome.value = null;
  pendingItem.value = item;

  const asked = await previewAction(
    current,
    finished.scanId,
    item.id,
    action,
    action === 'move' ? moveDestination.value.trim() : undefined,
  );

  if ('problem' in asked) {
    pending.value = null;
    actionProblem.value = asked.problem.message;
    return;
  }

  pending.value = asked.preview;
  typedName.value = '';
}

async function refreshPreviewForDestination(): Promise<void> {
  const item = pendingItem.value;
  if (item && pending.value?.action === 'move') await askPreview(item, 'move');
}

async function confirmAction(): Promise<void> {
  const current = connection.value;
  const preview = pending.value;
  if (!current || !preview || !confirmationSatisfied.value || isActing.value) return;

  isActing.value = true;
  actionProblem.value = null;

  try {
    const result = await executeAction(current, preview, typedName.value.trim());
    if (!result) {
      actionProblem.value = copy.value.actionFailedTitle;
      return;
    }

    outcome.value = result;
    if (result.succeeded) {
      const acted = preview.sourcePath;
      closeConfirmation();

      // The stored walk still describes the disk as it was a moment ago. Rather than re-reading
      // it — which would list the folder that was just removed and invite a second attempt — the
      // acted-on subtree is dropped and the table is marked as no longer current.
      const prefix = acted.endsWith('\\') ? acted : `${acted}\\`;
      results.value = results.value.filter(
        (item) => item.path !== acted && !item.path.startsWith(prefix),
      );
      resultsAreStale.value = true;
    } else {
      actionProblem.value = result.errorMessage ?? copy.value.actionFailedTitle;
    }
  } finally {
    isActing.value = false;
  }
}

const outcomeText = computed(() => {
  const done = outcome.value;
  if (!done?.succeeded) return '';
  const template = done.linkPath || pending.value?.action === 'move'
    ? copy.value.actionDoneMove
    : copy.value.actionDoneDelete;
  return fill(template, { bytes: formatBytes(done.bytesFreed) });
});

async function loadDevices(): Promise<void> {
  devices.value = await listDevices(credentials);
  const first = devices.value[0];
  if (!selectedDeviceId.value && first) {
    selectedDeviceId.value = first.id;
  }
  hasLoadedDevices.value = true;
}

async function connect(): Promise<void> {
  if (!selectedDeviceId.value || isConnecting.value) return;

  isConnecting.value = true;
  failure.value = null;
  connection.value = null;
  stopPolling();
  scan.value = null;
  results.value = [];
  drives.value = [];

  try {
    const result = await connectToAgent(credentials, selectedDeviceId.value);
    if (result.ok) {
      connection.value = result.connection;
      await loadDrives();
    } else {
      failure.value = result.failure;
    }
  } finally {
    isConnecting.value = false;
    // The server records the attempt, so the "last asked for" column is stale the moment we ask.
    await loadDevices();
  }
}

onMounted(loadDevices);
</script>

<template>
  <section class="agent">
    <header class="agent__hero">
      <div>
        <p>{{ copy.kicker }}</p>
        <h1>{{ copy.title }}</h1>
        <span>{{ copy.intro }}</span>
      </div>
    </header>

    <section v-if="!credentials.signedIn" class="agent__empty">
      <div>
        <h2>{{ copy.signedOutTitle }}</h2>
        <p>{{ copy.signedOutBody }}</p>
      </div>
      <a class="agent__link" href="/account/login">{{ copy.signIn }}</a>
    </section>

    <section v-else-if="hasLoadedDevices && devices.length === 0" class="agent__empty">
      <div>
        <h2>{{ copy.noDevicesTitle }}</h2>
        <p>{{ copy.noDevicesBody }}</p>
      </div>
      <a class="agent__link" href="/account">{{ copy.openAccount }}</a>
    </section>

    <section v-else-if="devices.length > 0" class="agent__body">
      <div class="agent__device">
        <label class="agent__field">
          <span>{{ copy.deviceLabel }}</span>
          <select v-model="selectedDeviceId">
            <option v-for="device in devices" :key="device.id" :value="device.id">
              {{ device.displayName }}
            </option>
          </select>
        </label>

        <p v-if="selectedDevice" class="agent__meta">
          {{ copy.lastSeen }}: {{ formatMoment(selectedDevice.lastSeenAtUtc) }}
        </p>

        <button type="button" class="agent__connect" :disabled="isConnecting" @click="connect">
          {{ connection || failure ? copy.reconnect : copy.connect }}
        </button>
      </div>

      <p v-if="isConnecting" class="agent__status" role="status">{{ copy.connecting }}</p>

      <section v-if="connection" class="agent__connected">
        <h2>{{ copy.connectedTitle }}</h2>
        <p>{{ connectedBody }}</p>
        <dl>
          <div>
            <dt>{{ copy.agentVersion }}</dt>
            <dd>{{ connection.status.agentVersion }}</dd>
          </div>
          <div>
            <dt>{{ copy.runningSince }}</dt>
            <dd>{{ formatMoment(connection.status.startedAtUtc) }}</dd>
          </div>
        </dl>
        <p class="agent__note">{{ copy.notYet }}</p>
      </section>

      <template v-if="connection">
        <section class="agent__drives">
          <h2>{{ copy.drivesTitle }}</h2>
          <p>{{ copy.drivesBody }}</p>

          <div class="agent__drive-grid">
            <button
              v-for="drive in drives"
              :key="drive.name"
              type="button"
              class="agent__drive"
              :class="{ 'is-active': rootPath === drive.name }"
              :disabled="!drive.isReady || isScanning"
              @click="rootPath = drive.name"
            >
              <strong>{{ drive.name }}</strong>
              <span>{{ drive.volumeLabel || drive.driveFormat }}</span>
              <small>{{ formatBytes(drive.freeBytes) }} {{ copy.driveFree }} {{ formatBytes(drive.totalBytes) }}</small>
            </button>
          </div>

          <label class="agent__field">
            <span>{{ copy.folderLabel }}</span>
            <input v-model="rootPath" type="text" dir="ltr" :placeholder="copy.folderPlaceholder" :disabled="isScanning">
          </label>

          <label class="agent__check">
            <input v-model="deep" type="checkbox" :disabled="isScanning">
            <span>{{ copy.deepMode }}</span>
          </label>

          <div class="agent__actions">
            <button type="button" class="agent__connect" :disabled="!canScan" @click="beginScan">
              {{ copy.startScan }}
            </button>
            <button v-if="isScanning" type="button" class="agent__stop" @click="stopScan">
              {{ copy.cancelScan }}
            </button>
          </div>

          <p v-if="scanProblem" class="agent__scan-problem" role="alert">{{ scanProblem }}</p>
        </section>

        <section v-if="scan" class="agent__scan" :data-state="scan.state">
          <h2 v-if="isScanning">{{ fill(copy.scanning, { path: scan.currentPath }) }}</h2>
          <h2 v-else>{{ scanHeading }}</h2>

          <p>
            {{ fill(copy.scanStats, {
              files: scan.files.toLocaleString(locale),
              folders: scan.folders.toLocaleString(locale),
              bytes: formatBytes(scan.bytes),
            }) }}
            ·
            {{ fill(copy.scanElapsed, { seconds: String(scan.elapsedSeconds) }) }}
            <template v-if="scan.errors > 0">
              · {{ fill(copy.scanErrors, { errors: scan.errors.toLocaleString(locale) }) }}
            </template>
          </p>

          <p v-if="scan.error" class="agent__scan-problem">{{ scan.error }}</p>
        </section>

        <section v-if="scan?.state === 'Completed'" class="agent__results">
          <header>
            <div>
              <h2>{{ copy.resultsTitle }}</h2>
              <p>{{ copy.resultsBody }}</p>
            </div>
            <div class="agent__results-tools">
              <label class="agent__check">
                <input v-model="foldersOnly" type="checkbox" @change="loadResults">
                <span>{{ copy.foldersOnly }}</span>
              </label>
              <div class="agent__archive">
                <button type="button" class="agent__copy" :disabled="archiveState === 'writing'" @click="takeArchive('open')">
                  {{ archiveState === 'writing' ? copy.archiveWriting : copy.archiveOpen }}
                </button>
                <button type="button" class="agent__copy" :disabled="archiveState === 'writing'" @click="takeArchive('save')">
                  {{ copy.archiveSave }}
                </button>
              </div>
            </div>
          </header>

          <p class="agent__note">{{ copy.archiveBody }}</p>
          <p v-if="archiveProblem" class="agent__scan-problem" role="alert">{{ archiveProblem }}</p>

          <p v-if="results.length === 0">{{ copy.noResults }}</p>

          <table v-else>
            <thead>
              <tr>
                <th scope="col">{{ copy.colPath }}</th>
                <th scope="col">{{ copy.colKind }}</th>
                <th scope="col">{{ copy.colSize }}</th>
                <th scope="col"><span class="agent__sr">{{ copy.copyPath }}</span></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="item in results" :key="item.id">
                <td>
                  <code dir="ltr">{{ item.path }}</code>
                  <em v-if="item.isProtected">{{ copy.protectedItem }}</em>
                </td>
                <td>{{ describeItem(item) }}</td>
                <td class="agent__size">{{ formatBytes(item.size) }}</td>
                <td class="agent__row-actions">
                  <button type="button" class="agent__copy" @click="copyPath(item.path)">
                    {{ copiedPath === item.path ? copy.copied : copy.copyPath }}
                  </button>
                  <button
                    v-if="item.canDelete"
                    type="button"
                    class="agent__copy agent__copy--danger"
                    @click="askPreview(item, 'delete')"
                  >
                    {{ copy.actDelete }}
                  </button>
                  <button
                    v-if="item.canMove"
                    type="button"
                    class="agent__copy"
                    @click="askPreview(item, 'move')"
                  >
                    {{ copy.actMove }}
                  </button>
                </td>
              </tr>
            </tbody>
          </table>

          <p v-if="outcome?.succeeded" class="agent__outcome" role="status">
            <strong>{{ copy.actionDoneTitle }}</strong> {{ outcomeText }}
          </p>
          <p v-if="resultsAreStale" class="agent__note">{{ copy.resultsStale }}</p>
        </section>

        <!-- Nothing above this point has touched the disk. This is where it can. -->
        <section v-if="pending" class="agent__confirm" role="dialog" aria-modal="false">
          <h2>{{ copy.confirmTitle }}</h2>
          <code dir="ltr">{{ pending.sourcePath }}</code>

          <p>{{ fill(copy.confirmMeasured, { bytes: formatBytes(pending.measuredBytes) }) }}</p>
          <p>{{ pending.action === 'move' ? copy.confirmMoveBody : copy.confirmDeleteBody }}</p>

          <ul v-if="warningText.length > 0" class="agent__warnings">
            <li v-for="warning in warningText" :key="warning">{{ warning }}</li>
          </ul>

          <label v-if="pending.action === 'move'" class="agent__field">
            <span>{{ copy.confirmDestination }}</span>
            <input
              v-model="moveDestination"
              type="text"
              dir="ltr"
              :placeholder="copy.folderPlaceholder"
              @change="refreshPreviewForDestination"
            >
            <small>{{ copy.confirmDestinationHint }}</small>
          </label>

          <label class="agent__field">
            <span>{{ fill(copy.confirmTypePrompt, { name: pending.confirmationPhrase }) }}</span>
            <input v-model="typedName" type="text" dir="ltr" autocomplete="off">
          </label>

          <p v-if="actionProblem" class="agent__scan-problem" role="alert">{{ actionProblem }}</p>

          <div class="agent__actions">
            <button
              type="button"
              class="agent__danger"
              :disabled="!confirmationSatisfied || isActing"
              @click="confirmAction"
            >
              {{ copy.confirmAction }}
            </button>
            <button type="button" class="agent__stop" @click="closeConfirmation">
              {{ copy.confirmCancel }}
            </button>
          </div>
        </section>
      </template>

      <section v-else-if="problem" class="agent__problem" role="alert">
        <h2>{{ problem.title }}</h2>
        <p>{{ problem.body }}</p>
      </section>
    </section>

    <p class="agent__boundary">{{ copy.boundary }}</p>
  </section>
</template>

<style scoped>
.agent { display: grid; gap: 1.4rem; }

.agent__hero {
  display: flex;
  justify-content: space-between;
  gap: 2rem;
  align-items: end;
  padding: 1.2rem 0 1.8rem;
  border-bottom: 1px solid var(--line);
}
.agent__hero > div { max-width: 760px; }
.agent__hero p {
  margin: 0 0 .65rem;
  color: var(--pine-bright);
  font-size: .74rem;
  font-weight: 800;
  letter-spacing: .14em;
}
.agent__hero h1 {
  max-width: 720px;
  margin: 0;
  color: var(--ink);
  font-size: clamp(2.2rem, 5vw, 5.2rem);
  line-height: .96;
  letter-spacing: -.045em;
}
.agent__hero span {
  display: block;
  max-width: 68ch;
  margin-top: 1rem;
  color: var(--muted);
  line-height: 1.7;
}

.agent__empty {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  padding: 1.4rem;
  border: 1px solid var(--line);
  background: var(--surface);
}
.agent__empty h2, .agent__empty p { margin: .15rem 0; }
.agent__empty p { max-width: 62ch; color: var(--muted); line-height: 1.6; }

.agent__link {
  padding: .6rem .9rem;
  border: 1px solid var(--line);
  color: var(--ink);
  font-size: .82rem;
  font-weight: 700;
  white-space: nowrap;
}

.agent__body { display: grid; gap: 1.2rem; }

.agent__device {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: .6rem 1rem;
  align-items: end;
  padding: 1.3rem;
  border: 1px solid var(--line);
  background: var(--surface);
}

.agent__field { display: grid; gap: .45rem; }
.agent__field > span { color: var(--ink); font-size: .78rem; font-weight: 800; }
.agent__field select {
  width: 100%;
  min-height: 2.75rem;
  padding: .62rem .75rem;
  border: 1px solid var(--line);
  border-radius: 0;
  background: var(--paper);
  color: var(--ink);
  font: inherit;
}

.agent__meta { grid-column: 1; margin: 0; color: var(--muted); font-size: .8rem; }

.agent__connect {
  grid-row: 1;
  grid-column: 2;
  min-height: 2.75rem;
  padding: 0 1.2rem;
  border: 1px solid var(--ink);
  background: var(--ink);
  color: var(--lime);
  font: 800 .82rem/1 inherit;
  cursor: pointer;
}
.agent__connect:disabled { cursor: not-allowed; opacity: .45; }

.agent__status { margin: 0; color: var(--muted); }

.agent__connected, .agent__problem {
  display: grid;
  gap: .6rem;
  padding: 1.3rem;
  border: 1px solid var(--line);
  background: var(--surface);
}
.agent__connected {
  border-color: var(--pine-bright);
  background: color-mix(in srgb, var(--lime), transparent 88%);
}
.agent__problem {
  border-color: color-mix(in srgb, var(--amber), transparent 40%);
  background: color-mix(in srgb, var(--amber), transparent 90%);
}
.agent__connected h2, .agent__problem h2 { margin: 0; font-size: 1.2rem; }
.agent__connected p, .agent__problem p { max-width: 68ch; margin: 0; color: var(--muted); line-height: 1.65; }

.agent__connected dl {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: .8rem;
  margin: .4rem 0 0;
}
.agent__connected dt { color: var(--ink); font-size: .72rem; font-weight: 800; text-transform: uppercase; letter-spacing: .08em; }
.agent__connected dd { margin: .25rem 0 0; color: var(--muted); }

.agent__drives, .agent__scan, .agent__results {
  display: grid;
  gap: .8rem;
  padding: 1.3rem;
  border: 1px solid var(--line);
  background: var(--surface);
}
.agent__drives h2, .agent__scan h2, .agent__results h2 { margin: 0; font-size: 1.2rem; }
.agent__drives > p, .agent__results p { max-width: 68ch; margin: 0; color: var(--muted); line-height: 1.65; }

.agent__drive-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  gap: .6rem;
}
.agent__drive {
  display: grid;
  gap: .2rem;
  padding: .8rem;
  border: 1px solid var(--line);
  background: var(--paper);
  color: var(--ink);
  text-align: start;
  cursor: pointer;
}
.agent__drive.is-active { border-color: var(--pine-bright); box-shadow: inset 0 -3px var(--pine-bright); }
.agent__drive:disabled { cursor: not-allowed; opacity: .45; }
.agent__drive strong { font-size: 1rem; }
.agent__drive span, .agent__drive small { color: var(--muted); font-size: .72rem; }

.agent__field input {
  width: 100%;
  min-height: 2.75rem;
  padding: .62rem .75rem;
  border: 1px solid var(--line);
  border-radius: 0;
  background: var(--paper);
  color: var(--ink);
  font: inherit;
}

.agent__check { display: flex; gap: .55rem; align-items: center; color: var(--muted); cursor: pointer; }
.agent__check input { accent-color: var(--pine-bright); }

.agent__actions { display: flex; gap: .6rem; }
.agent__stop {
  min-height: 2.75rem;
  padding: 0 1.2rem;
  border: 1px solid color-mix(in srgb, var(--danger) 45%, transparent);
  background: transparent;
  color: var(--danger);
  font: 800 .82rem/1 inherit;
  cursor: pointer;
}
.agent__scan-problem { margin: 0; color: var(--danger); line-height: 1.6; }

.agent__scan[data-state="Running"] { border-color: var(--pine-bright); }
.agent__scan h2 {
  overflow: hidden;
  font-size: .95rem;
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
  direction: ltr;
  unicode-bidi: plaintext;
}
.agent__scan p { margin: 0; color: var(--muted); }

.agent__results > header { display: flex; justify-content: space-between; gap: 1.5rem; align-items: start; }
.agent__results table { width: 100%; border-collapse: collapse; font-size: .82rem; }
.agent__results th {
  padding: .5rem .6rem;
  border-bottom: 1px solid var(--line);
  color: var(--ink);
  font-size: .7rem;
  letter-spacing: .08em;
  text-align: start;
  text-transform: uppercase;
}
.agent__results td { padding: .55rem .6rem; border-bottom: 1px solid var(--line); vertical-align: top; }
.agent__results code {
  /* Always left to right: a Windows path reordered by RTL layout is unreadable and uncopyable. */
  font-family: ui-monospace, "Cascadia Mono", Consolas, monospace;
  font-size: .78rem;
  word-break: break-all;
  direction: ltr;
  unicode-bidi: isolate;
}
.agent__results em { margin-inline-start: .4rem; color: var(--danger); font-size: .68rem; font-style: normal; }
.agent__size { text-align: end; white-space: nowrap; }
.agent__copy {
  padding: .3rem .55rem;
  border: 1px solid var(--line);
  background: transparent;
  color: var(--ink);
  font: 700 .7rem/1 inherit;
  cursor: pointer;
}
.agent__copy--danger { border-color: color-mix(in srgb, var(--danger) 45%, transparent); color: var(--danger); }
.agent__copy:disabled { opacity: .55; cursor: progress; }

/* Header controls stack on narrow screens rather than crowding the heading beside them. */
.agent__results-tools { display: flex; flex-direction: column; gap: .7rem; align-items: end; }
.agent__archive { display: flex; gap: .45rem; flex-wrap: wrap; justify-content: end; }
/* Larger than the per-row buttons: these act on the whole walk, not on one line of it. */
.agent__archive .agent__copy { padding: .45rem .8rem; font-size: var(--label-sm); }
.agent__archive .agent__copy:first-child {
  border-color: color-mix(in srgb, var(--pine-bright) 55%, transparent);
  color: var(--pine-bright);
}

@media (max-width: 640px) {
  .agent__results > header { flex-direction: column; }
  .agent__results-tools { align-items: start; width: 100%; }
  .agent__archive { justify-content: start; }
}
.agent__row-actions { display: flex; gap: .35rem; white-space: nowrap; }

.agent__outcome { margin: .4rem 0 0; color: var(--muted); line-height: 1.6; }
.agent__outcome strong { color: var(--ink); }

/* The only surface in the browser edition behind which a file changes. Marked as such. */
.agent__confirm {
  display: grid;
  gap: .7rem;
  padding: 1.4rem;
  border: 2px solid var(--danger);
  background: color-mix(in srgb, var(--danger), transparent 94%);
}
.agent__confirm h2 { margin: 0; font-size: 1.2rem; }
.agent__confirm > code {
  padding: .5rem .6rem;
  border: 1px solid var(--line);
  background: var(--paper);
  font-family: ui-monospace, "Cascadia Mono", Consolas, monospace;
  font-size: .8rem;
  word-break: break-all;
  direction: ltr;
  unicode-bidi: isolate;
}
.agent__confirm p { max-width: 68ch; margin: 0; color: var(--muted); line-height: 1.65; }
.agent__confirm small { color: var(--muted); font-size: .72rem; }

.agent__warnings { margin: 0; padding-inline-start: 1.2rem; color: var(--ink); }
.agent__warnings li { margin: .2rem 0; line-height: 1.55; }

.agent__danger {
  min-height: 2.75rem;
  padding: 0 1.2rem;
  border: 1px solid var(--danger);
  background: var(--danger);
  color: white;
  font: 800 .82rem/1 inherit;
  cursor: pointer;
}
.agent__danger:disabled { cursor: not-allowed; opacity: .45; }

.agent__sr {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip-path: inset(50%);
}

.agent__note, .agent__boundary { margin: 0; color: var(--muted); font-size: .83rem; line-height: 1.6; }
.agent__boundary { padding-top: .4rem; border-top: 1px solid var(--line); }

@media (max-width: 700px) {
  .agent__hero, .agent__empty { align-items: start; flex-direction: column; }
  .agent__device { grid-template-columns: 1fr; }
  .agent__connect { grid-row: auto; grid-column: 1; }
}
</style>
