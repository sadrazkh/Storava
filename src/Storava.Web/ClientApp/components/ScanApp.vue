<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch, watchEffect } from 'vue';
import AdvisorPanel from '@/components/AdvisorPanel.vue';
import AgentPanel from '@/components/AgentPanel.vue';
import BrandMark from '@/components/BrandMark.vue';
import PreferenceControls from '@/components/PreferenceControls.vue';
import TreemapCanvas from '@/components/TreemapCanvas.vue';
import { usePreferences } from '@/composables/usePreferences';
import { getAdvisorMessages } from '@/localization/advisorMessages';
import { getAgentMessages } from '@/localization/agentMessages';
import { getExplorerMessages } from '@/localization/explorerMessages';
import type { AdvisorResult, AdvisorReviewTarget } from '@/models/advisor';
import type { ScanFilters, ScanItem, ScanSession } from '@/models/scan';
import { detectCapabilities } from '@/services/capabilityService';
import { deleteLocalItem, readLocalFile } from '@/services/fileActionService';
import { FolderSelectionCancelledError, selectFolder } from '@/services/folderPermissionService';
import { buildBrowserRelativeAddress } from '@/services/itemAddressService';
import {
  clearAllLocalData,
  deleteSession,
  getAdvisorResult,
  getSession,
  listSessions,
  putAdvisorResult,
  queryItems,
} from '@/services/scanDatabase';
import { exportArchive, importArchive } from '@/services/archiveService';
import { downloadExport, importSession } from '@/services/exportImportService';
import { ScannerService } from '@/services/scannerService';
import { createOfflineReport } from '@/services/reportService';

const { t, locale } = usePreferences();
const advisorCopy = computed(() => getAdvisorMessages(locale.value));
const agentCopy = computed(() => getAgentMessages(locale.value));
const explorerCopy = computed(() => getExplorerMessages(locale.value));
const activeView = ref<'overview' | 'explorer' | 'advisor' | 'agent' | 'history'>('overview');
const session = ref<ScanSession | null>(null);
const advisorResult = ref<AdvisorResult | null>(null);
const sessions = ref<ScanSession[]>([]);
const items = ref<ScanItem[]>([]);
const treeFolders = ref<ScanItem[]>([]);
const currentFolder = ref('');
const selectedItem = ref<ScanItem | null>(null);
const errors = ref<string[]>([]);
const isChoosing = ref(false);
const importInput = ref<HTMLInputElement | null>(null);
const importPercent = ref<number | null>(null);
const notice = ref('');
const scrollTop = ref(0);
const compareIds = ref<string[]>([]);
const pendingDelete = ref<ScanItem | null>(null);
const deleteConfirmation = ref('');
const deleteInput = ref<HTMLInputElement | null>(null);
const isDeleting = ref(false);
const isOpeningFile = ref(false);
const rowHeight = 54;
const viewportHeight = 540;
const filters = ref<ScanFilters>({
  query: '',
  category: 'all',
  kind: 'all',
  risk: 'all',
  recommendation: 'all',
  aiRuleIds: [],
  sort: 'size-desc',
  parentPath: null,
});
let filterTimer: number | undefined;

const scanner = new ScannerService({
  onSession: (next) => {
    session.value = next;
    void refreshHistory();
    if (next.status === 'completed' || next.status === 'cancelled') void loadItems();
  },
  onBatch: (batch, next) => {
    session.value = next;
    const combined = [...batch, ...items.value];
    items.value = combined.sort((left, right) => right.size - left.size).slice(0, 1_500);
  },
  onError: (message) => {
    errors.value = [message, ...errors.value].slice(0, 30);
  },
});

const isActive = computed(() => session.value?.status === 'running' || session.value?.status === 'paused');
const canManageSelectedItem = computed(() =>
  session.value?.source === 'native'
  && Boolean(selectedItem.value?.relativePath)
  && !isActive.value);
const visibleStart = computed(() => Math.max(0, Math.floor(scrollTop.value / rowHeight) - 5));
const visibleItems = computed(() => items.value.slice(visibleStart.value, visibleStart.value + Math.ceil(viewportHeight / rowHeight) + 10));
const comparison = computed(() => {
  const selected = compareIds.value.map((id) => sessions.value.find((item) => item.id === id)).filter(Boolean) as ScanSession[];
  if (selected.length !== 2) return null;
  const ordered = [...selected].sort((left, right) => left.createdAt - right.createdAt);
  const older = ordered[0];
  const newer = ordered[1];
  if (!older || !newer) return null;
  return {
    older,
    newer,
    bytes: newer.metrics.bytes - older.metrics.bytes,
    files: newer.metrics.files - older.metrics.files,
  };
});
const breadcrumbs = computed(() => {
  const parts = currentFolder.value.split('/').filter(Boolean);
  return parts.map((name, index) => ({ name, path: parts.slice(0, index + 1).join('/') }));
});

watchEffect(() => {
  document.title = `${t('scanMetaTitle')} · ${t('productName')} Web`;
});

watch(filters, () => {
  clearTimeout(filterTimer);
  filterTimer = window.setTimeout(() => void loadItems(), 180);
}, { deep: true });

watch(() => filters.value.recommendation, (recommendation) => {
  if (recommendation === 'ai-targeted' && filters.value.aiRuleIds.length === 0) {
    filters.value.aiRuleIds = advisorResult.value?.reviewTargets.map((target) => target.signal) ?? [];
  }
});

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
  const index = Math.min(Math.floor(Math.log(Math.abs(bytes)) / Math.log(1024)), units.length - 1);
  return `${new Intl.NumberFormat(locale.value, { maximumFractionDigits: index > 1 ? 1 : 0, signDisplay: bytes < 0 ? 'always' : 'auto' }).format(bytes / 1024 ** index)} ${units[index]}`;
}

function formatCount(value: number): string {
  return new Intl.NumberFormat(locale.value).format(value);
}

function formatDuration(milliseconds: number): string {
  const seconds = Math.floor(milliseconds / 1000);
  const minutes = Math.floor(seconds / 60);
  return `${new Intl.NumberFormat(locale.value).format(minutes)}:${new Intl.NumberFormat(locale.value, { minimumIntegerDigits: 2 }).format(seconds % 60)}`;
}

function formatDate(value: number | null): string {
  return value ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }).format(value) : '—';
}

function statusLabel(value: ScanSession['status']): string {
  const key = {
    running: 'scanning',
    paused: 'paused',
    completed: 'completed',
    cancelled: 'cancelled',
    failed: 'failed',
    imported: 'imported',
  }[value] as Parameters<typeof t>[0];
  return t(key);
}

function categoryLabel(category: string): string {
  const key = {
    documents: 'categoryDocuments',
    media: 'categoryMedia',
    archives: 'categoryArchives',
    code: 'categoryCode',
    applications: 'categoryApplications',
    folders: 'categoryFolders',
    other: 'categoryOther',
  }[category] as Parameters<typeof t>[0] | undefined;
  return key ? t(key) : category;
}

function ruleLabel(ruleId: string): string {
  if (ruleId === 'generated-folder') return t('generatedSignal');
  if (ruleId === 'large-file' || ruleId === 'huge-file') return t('largeFileSignal');
  if (ruleId === 'archive') return t('archiveSignal');
  if (ruleId === 'stale-large-file') return t('staleSignal');
  return t('backupSignal');
}

function matchingAdvisorTarget(item: ScanItem): AdvisorReviewTarget | undefined {
  return advisorResult.value?.reviewTargets.find((target) => item.ruleIds.includes(target.signal));
}

function itemAddress(item: ScanItem): string {
  return buildBrowserRelativeAddress(session.value?.rootName ?? '', item.relativePath);
}

function resetRecommendationFilter(): void {
  filters.value.recommendation = 'all';
  filters.value.aiRuleIds = [];
}

async function chooseAndScan(): Promise<void> {
  isChoosing.value = true;
  notice.value = '';
  errors.value = [];
  try {
    const selection = await selectFolder(detectCapabilities());
    items.value = [];
    advisorResult.value = null;
    currentFolder.value = '';
    filters.value.parentPath = null;
    resetRecommendationFilter();
    activeView.value = 'overview';
    await scanner.start(selection);
  } catch (error) {
    if (!(error instanceof FolderSelectionCancelledError)) {
      notice.value = error instanceof Error ? error.message : t('selectionError');
    }
  } finally {
    isChoosing.value = false;
  }
}

async function loadItems(): Promise<void> {
  if (!session.value) {
    items.value = [];
    return;
  }
  items.value = (await queryItems(session.value.id, filters.value, 0, 2_000)).items;
  treeFolders.value = (await queryItems(
    session.value.id,
    {
      query: '',
      category: 'all',
      kind: 'folder',
      risk: 'all',
      recommendation: 'all',
      aiRuleIds: [],
      sort: 'name',
      parentPath: currentFolder.value,
    },
    0,
    100,
  )).items.filter((item) => item.relativePath !== currentFolder.value);
  scrollTop.value = 0;
}

async function openFolder(path: string): Promise<void> {
  currentFolder.value = path;
  filters.value.parentPath = path;
  await loadItems();
}

async function openHistorySession(id: string): Promise<void> {
  const stored = await getSession(id);
  if (!stored) return;
  session.value = stored;
  advisorResult.value = await getAdvisorResult(id) ?? null;
  currentFolder.value = '';
  filters.value.parentPath = null;
  resetRecommendationFilter();
  activeView.value = 'overview';
  await loadItems();
}

async function handleAdvisorResult(result: AdvisorResult): Promise<void> {
  if (!session.value) return;
  advisorResult.value = result;
  await putAdvisorResult(session.value.id, result);
}

async function openAdvisorTarget(target: AdvisorReviewTarget): Promise<void> {
  currentFolder.value = '';
  activeView.value = 'explorer';
  filters.value.query = '';
  filters.value.parentPath = null;
  filters.value.recommendation = 'ai-targeted';
  filters.value.aiRuleIds = [target.signal];
  await loadItems();
}

async function copyItemAddress(item: ScanItem): Promise<void> {
  try {
    await navigator.clipboard.writeText(itemAddress(item));
    notice.value = explorerCopy.value.copiedAddress;
  } catch {
    notice.value = explorerCopy.value.actionFailed;
  }
}

async function openSelectedFile(): Promise<void> {
  if (!session.value || !selectedItem.value || selectedItem.value.kind !== 'file') return;
  isOpeningFile.value = true;
  try {
    const file = await readLocalFile(session.value, selectedItem.value);
    const url = URL.createObjectURL(file);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.target = '_blank';
    anchor.rel = 'noopener noreferrer';
    anchor.click();
    window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
  } catch {
    notice.value = explorerCopy.value.previewBlocked;
  } finally {
    isOpeningFile.value = false;
  }
}

async function requestDelete(item: ScanItem): Promise<void> {
  if (!item.relativePath) {
    notice.value = explorerCopy.value.rootProtected;
    return;
  }
  pendingDelete.value = item;
  deleteConfirmation.value = '';
  await nextTick();
  deleteInput.value?.focus();
}

async function confirmDelete(): Promise<void> {
  const target = pendingDelete.value;
  if (
    !session.value
    || !target
    || deleteConfirmation.value !== target.name
    || isDeleting.value
  ) return;
  isDeleting.value = true;
  try {
    const removal = await deleteLocalItem(session.value, target);
    session.value = removal.session;
    selectedItem.value = null;
    pendingDelete.value = null;
    deleteConfirmation.value = '';
    notice.value = `${explorerCopy.value.deleteSuccess} · ${formatBytes(removal.freedBytes)}`;
    await refreshHistory();
    await loadItems();
  } catch {
    notice.value = explorerCopy.value.actionFailed;
  } finally {
    isDeleting.value = false;
  }
}

async function refreshHistory(): Promise<void> {
  sessions.value = await listSessions();
}

async function exportCurrent(target: ScanSession): Promise<void> {
  // The shared .storava archive, which the desktop edition and the Agent open too.
  const result = await exportArchive(target.id);
  downloadExport(result.blob, result.fileName);
  notice.value = t('exportReady');
}

function exportReport(target: ScanSession): void {
  const report = createOfflineReport(target, locale.value, {
    kicker: t('reportKicker'),
    privacy: t('reportPrivacy'),
    size: t('reportSize'),
    files: t('reportFiles'),
    folders: t('reportFolders'),
    categories: t('reportCategories'),
    category: t('reportCategory'),
    count: t('reportCount'),
    largest: t('reportLargest'),
    relativePath: t('reportRelativePath'),
    risk: t('reportRisk'),
  });
  downloadExport(report.blob, report.fileName);
}

/**
 * Reads one file into the workspace and shows it. Shared by the file picker and by the Agent
 * panel, which hands up an archive of a walk it just ran: to this function they are the same
 * thing, and a scan of a real drive should behave no differently from one that was opened.
 */
async function absorb(file: File, done: string): Promise<void> {
  importPercent.value = 0;
  notice.value = t('importProgress');
  try {
    const importedSession = file.name.endsWith('.storava-web')
      ? await importSession(file, (processed, total) => {
        importPercent.value = total > 0 ? Math.round(processed / total * 100) : 0;
      })
      : await importArchive(file, (imported, total) => {
        importPercent.value = total > 0 ? Math.round(imported / total * 100) : 0;
      });
    session.value = importedSession;
    advisorResult.value = null;
    resetRecommendationFilter();
    notice.value = done;
    await refreshHistory();
    await loadItems();
  } catch (error) {
    notice.value = `${t('importFailed')}: ${error instanceof Error ? error.message : ''}`;
  } finally {
    importPercent.value = null;
  }
}

async function importFile(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) return;

  try {
    await absorb(file, t('importComplete'));
  } finally {
    input.value = '';
  }
}

/**
 * A walk the Agent ran, brought into this workspace. It lands in the overview rather than staying
 * on the Agent panel, because from here on it is an ordinary scan — history, the advisor and the
 * explorer all work on it without knowing where it came from.
 */
async function absorbAgentArchive(file: File): Promise<void> {
  await absorb(file, agentCopy.value.archiveImported);
  if (session.value) activeView.value = 'overview';
}

async function removeSession(id: string): Promise<void> {
  if (!confirm(t('confirmDelete'))) return;
  await deleteSession(id);
  if (session.value?.id === id) {
    session.value = null;
    advisorResult.value = null;
    items.value = [];
  }
  await refreshHistory();
}

async function clearData(): Promise<void> {
  if (!confirm(t('confirmClear'))) return;
  await clearAllLocalData();
  session.value = null;
  advisorResult.value = null;
  sessions.value = [];
  items.value = [];
}

function toggleCompare(id: string): void {
  if (compareIds.value.includes(id)) compareIds.value = compareIds.value.filter((item) => item !== id);
  else compareIds.value = [...compareIds.value.slice(-1), id];
}

onMounted(async () => {
  await refreshHistory();
  const requested = new URLSearchParams(location.search).get('session');
  const first = requested ?? sessions.value[0]?.id;
  if (first) await openHistorySession(first);
  await nextTick();
});
onBeforeUnmount(() => {
  clearTimeout(filterTimer);
  scanner.dispose();
});
</script>

<template>
  <div class="workspace-shell">
    <header class="workspace-header">
      <BrandMark />
      <div class="workspace-header__title">
        <span>{{ t('scanWorkspace') }}</span>
        <small>{{ t('localDatabase') }}</small>
      </div>
      <div class="workspace-header__actions">
        <PreferenceControls />
        <button class="button button--small button--primary" type="button" :disabled="isChoosing || isActive" @click="chooseAndScan">
          {{ t('newScan') }}
        </button>
      </div>
    </header>

    <div class="workspace-layout">
      <aside class="workspace-rail" :aria-label="t('scanWorkspace')">
        <button :class="{ 'is-active': activeView === 'overview' }" type="button" :aria-current="activeView === 'overview' ? 'page' : undefined" @click="activeView = 'overview'">
          <span aria-hidden="true">◫</span>{{ t('overview') }}
        </button>
        <button :class="{ 'is-active': activeView === 'explorer' }" type="button" :aria-current="activeView === 'explorer' ? 'page' : undefined" @click="activeView = 'explorer'">
          <span aria-hidden="true">⌘</span>{{ t('explorer') }}
        </button>
        <button :class="{ 'is-active': activeView === 'history' }" type="button" :aria-current="activeView === 'history' ? 'page' : undefined" @click="activeView = 'history'">
          <span aria-hidden="true">◷</span>{{ t('history') }}
        </button>
        <button :class="{ 'is-active': activeView === 'advisor' }" type="button" :aria-current="activeView === 'advisor' ? 'page' : undefined" @click="activeView = 'advisor'">
          <span aria-hidden="true">✦</span>{{ advisorCopy.railLabel }}
        </button>
        <button :class="{ 'is-active': activeView === 'agent' }" type="button" :aria-current="activeView === 'agent' ? 'page' : undefined" @click="activeView = 'agent'">
          <span aria-hidden="true">⬡</span>{{ agentCopy.railLabel }}
        </button>
        <div class="workspace-rail__privacy">
          <strong>{{ t('privacyPromise') }}</strong>
          <span>{{ t('browserLimit') }}</span>
        </div>
      </aside>

      <main class="workspace-main">
        <div v-if="notice" class="workspace-notice" role="status">
          <span>{{ notice }}</span>
          <span v-if="importPercent !== null">{{ importPercent }}%</span>
          <button type="button" :aria-label="t('closeDialog')" @click="notice = ''">×</button>
        </div>

        <section v-if="!session && (activeView === 'overview' || activeView === 'explorer')" class="scan-empty">
          <div class="scan-empty__orbit" aria-hidden="true"><span /><span /><span /></div>
          <p class="kicker">{{ t('privacyPromise') }}</p>
          <h1>{{ t('chooseScanFolder') }}</h1>
          <p>{{ t('chooseScanFolderBody') }}</p>
          <button class="button button--primary" type="button" :disabled="isChoosing" @click="chooseAndScan">
            {{ t('beginScan') }}
          </button>
        </section>

        <template v-else-if="activeView === 'overview' && session">
          <section class="scan-command">
            <div>
              <span class="status-dot" :data-status="session.status" />
              <p>{{ statusLabel(session.status) }}</p>
              <h1>{{ session.rootName }}</h1>
              <span class="scan-command__path">{{ session.metrics.currentPath || t('liveActivity') }}</span>
            </div>
            <div class="scan-command__actions">
              <button v-if="session.status === 'running'" class="button button--quiet" type="button" @click="scanner.pause">{{ t('pause') }}</button>
              <button v-if="session.status === 'paused'" class="button button--primary" type="button" @click="scanner.resume">{{ t('resume') }}</button>
              <button v-if="isActive" class="button button--danger" type="button" @click="scanner.cancel">{{ t('cancel') }}</button>
              <button v-if="!isActive" class="button button--quiet" type="button" @click="exportCurrent(session)">{{ t('exportScan') }}</button>
              <button v-if="!isActive" class="button button--quiet" type="button" @click="exportReport(session)">{{ t('offlineReport') }}</button>
            </div>
          </section>

          <div v-if="isActive" class="activity-line" aria-hidden="true"><span /></div>
          <p v-if="isActive" class="indeterminate-note">{{ t('indeterminateNote') }}</p>

          <section class="metric-grid">
            <article><span>{{ t('scannedSpace') }}</span><strong>{{ formatBytes(session.metrics.bytes) }}</strong></article>
            <article><span>{{ t('fileCount') }}</span><strong>{{ formatCount(session.metrics.files) }}</strong></article>
            <article><span>{{ t('folderCount') }}</span><strong>{{ formatCount(session.metrics.folders) }}</strong></article>
            <article><span>{{ t('scanSpeed') }}</span><strong>{{ formatCount(session.metrics.itemsPerSecond) }}</strong></article>
            <article><span>{{ t('elapsed') }}</span><strong>{{ formatDuration(session.metrics.elapsedMs) }}</strong></article>
            <article :class="{ 'has-warning': session.metrics.errors > 0 }"><span>{{ t('accessErrors') }}</span><strong>{{ formatCount(session.metrics.errors) }}</strong></article>
          </section>

          <section class="analysis-grid">
            <article class="analysis-panel analysis-panel--treemap">
              <header><div><span>{{ t('largestConsumers') }}</span><h2>{{ t('storageTreemap') }}</h2></div><small>{{ t('treemapHint') }}</small></header>
              <TreemapCanvas :items="session.topItems" @select="selectedItem = $event" />
            </article>
            <article class="analysis-panel category-panel">
              <header><div><span>{{ t('overview') }}</span><h2>{{ t('categories') }}</h2></div></header>
              <ol>
                <li v-for="aggregate in session.categories" :key="aggregate.category">
                  <span class="category-swatch" :data-category="aggregate.category" />
                  <strong>{{ categoryLabel(aggregate.category) }}</strong>
                  <span>{{ formatCount(aggregate.count) }}</span>
                  <b>{{ formatBytes(aggregate.bytes) }}</b>
                </li>
              </ol>
            </article>
          </section>

          <section v-if="errors.length" class="access-errors">
            <h2>{{ t('accessErrors') }}</h2>
            <ul><li v-for="error in errors" :key="error">{{ error }}</li></ul>
          </section>
        </template>

        <template v-else-if="activeView === 'explorer' && session">
          <header class="explorer-header">
            <div><p class="kicker">{{ session.rootName }}</p><h1>{{ t('explorer') }}</h1></div>
            <button class="button button--quiet" type="button" @click="exportCurrent(session)">{{ t('exportScan') }}</button>
          </header>
          <section class="explorer-tools">
            <label class="search-field"><span aria-hidden="true">⌕</span><input v-model="filters.query" :placeholder="t('searchFiles')"></label>
            <select v-model="filters.category" :aria-label="t('category')">
              <option value="all">{{ t('allCategories') }}</option>
              <option v-for="aggregate in session.categories" :key="aggregate.category" :value="aggregate.category">{{ categoryLabel(aggregate.category) }}</option>
            </select>
            <select v-model="filters.kind" :aria-label="t('allKinds')">
              <option value="all">{{ t('allKinds') }}</option><option value="file">{{ t('filesOnly') }}</option><option value="folder">{{ t('foldersOnly') }}</option>
            </select>
            <select v-model="filters.risk" :aria-label="t('allRisks')">
              <option value="all">{{ t('allRisks') }}</option><option value="none">{{ t('riskNone') }}</option><option value="low">{{ t('riskLow') }}</option><option value="medium">{{ t('riskMedium') }}</option><option value="high">{{ t('riskHigh') }}</option>
            </select>
            <select v-model="filters.recommendation" :aria-label="explorerCopy.reviewFilter" data-testid="recommendation-filter">
              <option value="all">{{ explorerCopy.allItems }}</option>
              <option value="local-signals">{{ explorerCopy.localSignals }}</option>
              <option value="ai-targeted" :disabled="!advisorResult?.reviewTargets.length">{{ explorerCopy.aiTargeted }}</option>
            </select>
            <select v-model="filters.sort" :aria-label="t('sortLargest')">
              <option value="size-desc">{{ t('sortLargest') }}</option><option value="size-asc">{{ t('sortSmallest') }}</option><option value="name">{{ t('sortName') }}</option><option value="modified">{{ t('sortModified') }}</option>
            </select>
          </section>
          <section class="ai-review-map" :class="{ 'ai-review-map--empty': !advisorResult?.reviewTargets.length }">
            <div>
              <span aria-hidden="true">AI</span>
              <div>
                <strong>{{ explorerCopy.aiMapTitle }}</strong>
                <p>{{ advisorResult?.reviewTargets.length ? explorerCopy.aiMapBody : explorerCopy.noAiTargets }}</p>
              </div>
            </div>
            <div v-if="advisorResult?.reviewTargets.length" class="ai-review-map__targets">
              <button
                v-for="target in advisorResult.reviewTargets"
                :key="`${target.signal}-${target.disposition}`"
                type="button"
                :class="{ 'is-active': filters.recommendation === 'ai-targeted' && filters.aiRuleIds.includes(target.signal) }"
                @click="openAdvisorTarget(target)"
              >
                {{ ruleLabel(target.signal) }}
              </button>
            </div>
          </section>
          <nav class="path-breadcrumb" :aria-label="t('path')">
            <button type="button" @click="openFolder('')">{{ t('rootFolder') }}</button>
            <template v-for="crumb in breadcrumbs" :key="crumb.path">
              <span>/</span><button type="button" @click="openFolder(crumb.path)">{{ crumb.name }}</button>
            </template>
          </nav>
          <div class="explorer-data">
            <aside class="lazy-tree">
              <h2>{{ t('folderTree') }}</h2>
              <button v-for="folder in treeFolders" :key="folder.id" type="button" @click="openFolder(folder.relativePath)">
                <i aria-hidden="true" /> <span>{{ folder.name }}</span><b>{{ formatBytes(folder.size) }}</b>
              </button>
            </aside>
            <section class="virtual-table">
              <header><span>{{ t('name') }}</span><span>{{ t('category') }}</span><span>{{ t('size') }}</span><span>{{ t('modified') }}</span><span>{{ t('signals') }}</span></header>
              <div class="virtual-table__viewport" @scroll="scrollTop = ($event.target as HTMLElement).scrollTop">
                <div :style="{ height: `${items.length * rowHeight}px`, position: 'relative' }">
                  <button
                    v-for="(item, index) in visibleItems"
                    :key="item.id"
                    class="virtual-row"
                    :class="{ 'is-ai-targeted': Boolean(matchingAdvisorTarget(item)) }"
                    type="button"
                    :style="{ transform: `translateY(${(visibleStart + index) * rowHeight}px)` }"
                    @click="selectedItem = item"
                  >
                    <span class="item-name"><i :data-kind="item.kind" /> <span><strong>{{ item.name }}</strong><small>{{ item.relativePath }}</small></span></span>
                    <span>{{ categoryLabel(item.category) }}</span><span>{{ formatBytes(item.size) }}</span><span>{{ formatDate(item.modifiedAt) }}</span>
                    <span class="row-signals">
                      <b v-if="matchingAdvisorTarget(item)" class="recommendation-pill" data-testid="ai-recommendation-tag">{{ explorerCopy.aiTag }}</b>
                      <b v-else-if="item.ruleIds.length" class="recommendation-pill recommendation-pill--local">{{ explorerCopy.localTag }}</b>
                      <b v-if="item.risk !== 'none'" class="risk-pill" :data-risk="item.risk">{{ t(`risk${item.risk[0]?.toUpperCase()}${item.risk.slice(1)}` as Parameters<typeof t>[0]) }}</b>
                      <span v-else-if="!item.ruleIds.length">—</span>
                    </span>
                  </button>
                </div>
              </div>
            </section>
          </div>
        </template>

        <template v-else-if="activeView === 'history'">
          <header class="explorer-header">
            <div><p class="kicker">{{ t('localDatabase') }}</p><h1>{{ t('scanHistory') }}</h1><p>{{ t('historyBody') }}</p></div>
            <div><button class="button button--quiet" type="button" @click="importInput?.click()">{{ t('importScan') }}</button><button class="button button--danger" type="button" @click="clearData">{{ t('clearLocalData') }}</button></div>
          </header>
          <input ref="importInput" class="visually-hidden" type="file" accept=".storava,.storava-web,application/zip,application/x-ndjson" @change="importFile">
          <section class="history-grid">
            <article v-for="stored in sessions" :key="stored.id" class="history-card">
              <label><input type="checkbox" :checked="compareIds.includes(stored.id)" @change="toggleCompare(stored.id)"><span>{{ t('compare') }}</span></label>
              <span class="status-dot" :data-status="stored.status" />
              <h2>{{ stored.rootName }}</h2><p>{{ formatDate(stored.createdAt) }}</p>
              <div><strong>{{ formatBytes(stored.metrics.bytes) }}</strong><span>{{ formatCount(stored.metrics.files) }} {{ t('fileCount') }}</span></div>
              <footer><button type="button" @click="openHistorySession(stored.id)">{{ t('openScan') }}</button><button type="button" @click="exportCurrent(stored)">{{ t('exportScan') }}</button><button type="button" @click="removeSession(stored.id)">{{ t('deleteScan') }}</button></footer>
            </article>
          </section>
          <section class="comparison-panel">
            <h2>{{ t('comparisonTitleData') }}</h2>
            <p v-if="!comparison">{{ t('comparisonNeedTwo') }}</p>
            <div v-else class="comparison-metrics">
              <div><span>{{ t('olderScan') }}</span><strong>{{ comparison.older.rootName }}</strong></div>
              <div><span>{{ t('newerScan') }}</span><strong>{{ comparison.newer.rootName }}</strong></div>
              <div><span>{{ t('changeInSize') }}</span><strong>{{ formatBytes(comparison.bytes) }}</strong></div>
              <div><span>{{ t('changeInFiles') }}</span><strong>{{ formatCount(comparison.files) }}</strong></div>
            </div>
          </section>
        </template>

        <AdvisorPanel
          v-else-if="activeView === 'advisor'"
          :session="session"
          :stored-result="advisorResult"
          @result="handleAdvisorResult"
          @open-target="openAdvisorTarget"
        />

        <AgentPanel v-else-if="activeView === 'agent'" @open-archive="absorbAgentArchive" />
      </main>
    </div>

    <Transition name="drawer">
      <aside
        v-if="selectedItem"
        class="detail-drawer"
        role="dialog"
        aria-modal="true"
        :aria-label="t('itemDetails')"
      >
        <button type="button" :aria-label="t('closeDetails')" @click="selectedItem = null">×</button>
        <p class="kicker">{{ t('itemDetails') }}</p><h2>{{ selectedItem.name }}</h2>
        <div v-if="matchingAdvisorTarget(selectedItem)" class="drawer-advisor-tag">
          <strong>{{ explorerCopy.aiTag }}</strong>
          <span>{{ matchingAdvisorTarget(selectedItem)?.rationale }}</span>
        </div>
        <dl><div><dt>{{ explorerCopy.itemAddress }}</dt><dd>{{ itemAddress(selectedItem) }}<small>{{ explorerCopy.addressLimitation }}</small></dd></div><div><dt>{{ t('size') }}</dt><dd>{{ formatBytes(selectedItem.size) }}</dd></div><div><dt>{{ t('modified') }}</dt><dd>{{ formatDate(selectedItem.modifiedAt) }}</dd></div><div><dt>{{ t('category') }}</dt><dd>{{ categoryLabel(selectedItem.category) }}</dd></div></dl>
        <ul v-if="selectedItem.ruleIds.length"><li v-for="rule in selectedItem.ruleIds" :key="rule">{{ ruleLabel(rule) }}</li></ul>
        <div class="drawer-actions">
          <button class="button button--quiet" type="button" @click="copyItemAddress(selectedItem)">{{ explorerCopy.copyAddress }}</button>
          <button
            v-if="selectedItem.kind === 'file'"
            class="button button--quiet"
            type="button"
            :disabled="!canManageSelectedItem || isOpeningFile"
            @click="openSelectedFile"
          >
            {{ isOpeningFile ? explorerCopy.openingFile : explorerCopy.openFile }}
          </button>
          <button
            class="button button--danger"
            type="button"
            :disabled="!canManageSelectedItem"
            @click="requestDelete(selectedItem)"
          >
            {{ explorerCopy.deleteItem }}
          </button>
          <p v-if="!canManageSelectedItem">{{ explorerCopy.deleteUnavailable }}</p>
        </div>
      </aside>
    </Transition>

    <div v-if="pendingDelete" class="delete-confirmation" @click.self="pendingDelete = null">
      <section role="alertdialog" aria-modal="true" aria-labelledby="delete-dialog-title">
        <span class="delete-confirmation__mark" aria-hidden="true">!</span>
        <p class="kicker">{{ explorerCopy.aiLocalBoundary }}</p>
        <h2 id="delete-dialog-title">{{ explorerCopy.deleteTitle }}</h2>
        <p>{{ explorerCopy.deleteWarning }}</p>
        <code>{{ itemAddress(pendingDelete) }}</code>
        <label>
          <span>{{ explorerCopy.deletePrompt }}: <strong>{{ pendingDelete.name }}</strong></span>
          <input
            ref="deleteInput"
            v-model="deleteConfirmation"
            type="text"
            autocomplete="off"
            spellcheck="false"
            @keydown.esc="pendingDelete = null"
            @keydown.enter="confirmDelete"
          >
        </label>
        <footer>
          <button class="button button--quiet" type="button" :disabled="isDeleting" @click="pendingDelete = null">{{ explorerCopy.deleteCancel }}</button>
          <button class="button button--danger" type="button" :disabled="deleteConfirmation !== pendingDelete.name || isDeleting" @click="confirmDelete">
            {{ isDeleting ? explorerCopy.deleting : explorerCopy.deleteConfirm }}
          </button>
        </footer>
      </section>
    </div>
  </div>
</template>

<style scoped>
.detail-drawer dd small {
  display: block;
  max-width: 48ch;
  margin-top: .45rem;
  color: var(--muted);
  font-size: .72rem;
  line-height: 1.5;
}

.explorer-tools {
  grid-template-columns: minmax(240px, 1fr) repeat(5, minmax(120px, auto));
}

.ai-review-map {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-block-end: .75rem;
  padding: .8rem 1rem;
  border: 1px solid color-mix(in srgb, var(--pine-bright), var(--line) 65%);
  background: color-mix(in srgb, var(--lime), transparent 91%);
}

.ai-review-map > div:first-child {
  display: flex;
  align-items: center;
  gap: .75rem;
}

.ai-review-map > div:first-child > span {
  display: grid;
  flex: 0 0 auto;
  place-items: center;
  width: 2.4rem;
  height: 2.4rem;
  border-radius: 50%;
  background: var(--ink);
  color: var(--lime);
  font-size: .72rem;
  font-weight: 900;
}

.ai-review-map strong,
.ai-review-map p {
  margin: 0;
}

.ai-review-map p {
  max-width: 68ch;
  margin-block-start: .2rem;
  color: var(--muted);
  font-size: .76rem;
  line-height: 1.45;
}

.ai-review-map--empty {
  border-style: dashed;
  background: var(--surface);
}

.ai-review-map__targets {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: .4rem;
}

.ai-review-map__targets button {
  padding: .35rem .55rem;
  border: 1px solid var(--line);
  background: var(--surface);
  color: var(--ink);
  font: inherit;
  font-size: .68rem;
  cursor: pointer;
}

.ai-review-map__targets button.is-active {
  border-color: var(--pine-bright);
  background: var(--ink);
  color: var(--lime);
}

.virtual-row.is-ai-targeted {
  box-shadow: inset 3px 0 0 var(--pine-bright);
  background: color-mix(in srgb, var(--lime), transparent 95%);
}

[dir='rtl'] .virtual-row.is-ai-targeted {
  box-shadow: inset -3px 0 0 var(--pine-bright);
}

.row-signals {
  display: flex;
  align-items: center;
  gap: .3rem;
}

.recommendation-pill {
  padding: .2rem .4rem;
  border-radius: 999px;
  background: var(--lime);
  color: #071a1c;
  font-size: .56rem;
  font-weight: 900;
  letter-spacing: .02em;
}

.recommendation-pill--local {
  border: 1px solid var(--line);
  background: transparent;
  color: var(--muted);
}

.drawer-advisor-tag {
  display: grid;
  gap: .35rem;
  padding: .8rem;
  border: 1px solid color-mix(in srgb, var(--pine-bright), var(--line) 60%);
  background: color-mix(in srgb, var(--lime), transparent 90%);
}

.drawer-advisor-tag strong {
  color: var(--pine-bright);
  font-size: .72rem;
  letter-spacing: .08em;
  text-transform: uppercase;
}

.drawer-advisor-tag span {
  color: var(--muted);
  font-size: .82rem;
  line-height: 1.5;
}

.drawer-actions {
  display: grid;
  gap: .55rem;
  margin-block-start: 1.3rem;
  padding-block-start: 1.2rem;
  border-block-start: 1px solid var(--line);
}

.drawer-actions > .button {
  position: static;
  width: 100%;
  height: auto;
  border-radius: 0;
  font-size: .78rem;
}

.drawer-actions p {
  margin: .25rem 0 0;
  color: var(--muted);
  font-size: .75rem;
  line-height: 1.55;
}

.delete-confirmation {
  position: fixed;
  z-index: 300;
  inset: 0;
  display: grid;
  place-items: center;
  padding: 1rem;
  background: #031112b8;
  backdrop-filter: blur(8px);
}

.delete-confirmation > section {
  width: min(580px, 100%);
  padding: clamp(1.3rem, 4vw, 2.2rem);
  border: 1px solid var(--line);
  background: var(--surface-raised);
  box-shadow: var(--shadow-lg);
}

.delete-confirmation__mark {
  display: grid;
  place-items: center;
  width: 3rem;
  height: 3rem;
  margin-block-end: 1rem;
  border-radius: 50%;
  background: var(--danger);
  color: white;
  font-size: 1.5rem;
  font-weight: 900;
}

.delete-confirmation h2 {
  margin: .3rem 0 .7rem;
  font-size: clamp(1.8rem, 5vw, 3rem);
}

.delete-confirmation p {
  color: var(--muted);
  line-height: 1.65;
}

.delete-confirmation code {
  display: block;
  max-height: 6rem;
  padding: .8rem;
  overflow: auto;
  border: 1px solid var(--line);
  background: var(--paper);
  color: var(--ink);
  direction: ltr;
  text-align: left;
}

.delete-confirmation label {
  display: grid;
  gap: .5rem;
  margin-block-start: 1rem;
  font-size: .8rem;
}

.delete-confirmation input {
  width: 100%;
  min-height: 3rem;
  padding: .7rem .8rem;
  border: 1px solid var(--line);
  background: var(--paper);
  color: var(--ink);
  font: inherit;
}

.delete-confirmation footer {
  display: flex;
  justify-content: flex-end;
  gap: .6rem;
  margin-block-start: 1.2rem;
}

@media (max-width: 1100px) {
  .explorer-tools {
    grid-template-columns: repeat(3, 1fr);
  }

  .search-field {
    grid-column: span 3;
  }
}

@media (max-width: 760px) {
  .explorer-tools {
    grid-template-columns: repeat(2, 1fr);
  }

  .search-field {
    grid-column: span 2;
  }

  .ai-review-map {
    align-items: stretch;
    flex-direction: column;
  }

  .ai-review-map__targets {
    justify-content: flex-start;
  }

  .delete-confirmation footer {
    flex-direction: column-reverse;
  }

  .delete-confirmation footer .button {
    width: 100%;
  }
}
</style>
