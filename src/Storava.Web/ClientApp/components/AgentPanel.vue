<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { usePreferences } from '@/composables/usePreferences';
import { getAgentMessages } from '@/localization/agentMessages';
import {
  connectToAgent,
  listDevices,
  readPageCredentials,
  type AgentConnection,
  type AgentFailure,
  type BrowserDevice,
} from '@/services/agentService';

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

function formatMoment(value: string): string {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString(locale.value);
}

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

  try {
    const result = await connectToAgent(credentials, selectedDeviceId.value);
    if (result.ok) {
      connection.value = result.connection;
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

.agent__note, .agent__boundary { margin: 0; color: var(--muted); font-size: .83rem; line-height: 1.6; }
.agent__boundary { padding-top: .4rem; border-top: 1px solid var(--line); }

@media (max-width: 700px) {
  .agent__hero, .agent__empty { align-items: start; flex-direction: column; }
  .agent__device { grid-template-columns: 1fr; }
  .agent__connect { grid-row: auto; grid-column: 1; }
}
</style>
