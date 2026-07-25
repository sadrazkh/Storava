<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch, watchEffect } from 'vue';
import AdvisorPanel from '@/components/AdvisorPanel.vue';
import BrandMark from '@/components/BrandMark.vue';
import PreferenceControls from '@/components/PreferenceControls.vue';
import TreemapCanvas from '@/components/TreemapCanvas.vue';
import { usePreferences } from '@/composables/usePreferences';
import { getAdvisorMessages } from '@/localization/advisorMessages';
import type { ScanFilters, ScanItem, ScanSession } from '@/models/scan';
import { detectCapabilities } from '@/services/capabilityService';
import { FolderSelectionCancelledError, selectFolder } from '@/services/folderPermissionService';
import { clearAllLocalData, deleteSession, getSession, listSessions, queryItems } from '@/services/scanDatabase';
import { downloadExport, exportSession, importSession } from '@/services/exportImportService';
import { ScannerService } from '@/services/scannerService';
import { createOfflineReport } from '@/services/reportService';

const { t, locale } = usePreferences();
const advisorCopy = computed(() => getAdvisorMessages(locale.value));
const activeView = ref<'overview' | 'explorer' | 'advisor' | 'history'>('overview');
const session = ref<ScanSession | null>(null);
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
const rowHeight = 54;
const viewportHeight = 540;
const filters = ref<ScanFilters>({ query: '', category: 'all', kind: 'all', risk: 'all', sort: 'size-desc', parentPath: null });
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

async function chooseAndScan(): Promise<void> {
  isChoosing.value = true;
  notice.value = '';
  errors.value = [];
  try {
    const selection = await selectFolder(detectCapabilities());
    items.value = [];
    currentFolder.value = '';
    filters.value.parentPath = null;
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
    { query: '', category: 'all', kind: 'folder', risk: 'all', sort: 'name', parentPath: currentFolder.value },
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
  currentFolder.value = '';
  filters.value.parentPath = null;
  activeView.value = 'overview';
  await loadItems();
}

async function refreshHistory(): Promise<void> {
  sessions.value = await listSessions();
}

async function exportCurrent(target: ScanSession): Promise<void> {
  const result = await exportSession(target.id);
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

async function importFile(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) return;
  importPercent.value = 0;
  notice.value = t('importProgress');
  try {
    const importedSession = await importSession(file, (processed, total) => {
      importPercent.value = total > 0 ? Math.round(processed / total * 100) : 0;
    });
    session.value = importedSession;
    notice.value = t('importComplete');
    await refreshHistory();
    await loadItems();
  } catch (error) {
    notice.value = `${t('importFailed')}: ${error instanceof Error ? error.message : ''}`;
  } finally {
    importPercent.value = null;
    input.value = '';
  }
}

async function removeSession(id: string): Promise<void> {
  if (!confirm(t('confirmDelete'))) return;
  await deleteSession(id);
  if (session.value?.id === id) {
    session.value = null;
    items.value = [];
  }
  await refreshHistory();
}

async function clearData(): Promise<void> {
  if (!confirm(t('confirmClear'))) return;
  await clearAllLocalData();
  session.value = null;
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
        <button :class="{ 'is-active': activeView === 'overview' }" type="button" @click="activeView = 'overview'">
          <span aria-hidden="true">◫</span>{{ t('overview') }}
        </button>
        <button :class="{ 'is-active': activeView === 'explorer' }" type="button" @click="activeView = 'explorer'">
          <span aria-hidden="true">⌘</span>{{ t('explorer') }}
        </button>
        <button :class="{ 'is-active': activeView === 'history' }" type="button" @click="activeView = 'history'">
          <span aria-hidden="true">◷</span>{{ t('history') }}
        </button>
        <button :class="{ 'is-active': activeView === 'advisor' }" type="button" @click="activeView = 'advisor'">
          <span aria-hidden="true">✦</span>{{ advisorCopy.railLabel }}
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
            <select v-model="filters.sort" :aria-label="t('sortLargest')">
              <option value="size-desc">{{ t('sortLargest') }}</option><option value="size-asc">{{ t('sortSmallest') }}</option><option value="name">{{ t('sortName') }}</option><option value="modified">{{ t('sortModified') }}</option>
            </select>
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
                    type="button"
                    :style="{ transform: `translateY(${(visibleStart + index) * rowHeight}px)` }"
                    @click="selectedItem = item"
                  >
                    <span class="item-name"><i :data-kind="item.kind" /> <span><strong>{{ item.name }}</strong><small>{{ item.relativePath }}</small></span></span>
                    <span>{{ categoryLabel(item.category) }}</span><span>{{ formatBytes(item.size) }}</span><span>{{ formatDate(item.modifiedAt) }}</span>
                    <span><b v-if="item.risk !== 'none'" class="risk-pill" :data-risk="item.risk">{{ t(`risk${item.risk[0]?.toUpperCase()}${item.risk.slice(1)}` as Parameters<typeof t>[0]) }}</b><span v-else>—</span></span>
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
          <input ref="importInput" class="visually-hidden" type="file" accept=".storava-web,application/x-ndjson" @change="importFile">
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

        <AdvisorPanel v-else-if="activeView === 'advisor'" :session="session" />
      </main>
    </div>

    <Transition name="drawer">
      <aside v-if="selectedItem" class="detail-drawer">
        <button type="button" :aria-label="t('closeDetails')" @click="selectedItem = null">×</button>
        <p class="kicker">{{ t('itemDetails') }}</p><h2>{{ selectedItem.name }}</h2>
        <dl><div><dt>{{ t('path') }}</dt><dd>{{ selectedItem.relativePath || session?.rootName }}</dd></div><div><dt>{{ t('size') }}</dt><dd>{{ formatBytes(selectedItem.size) }}</dd></div><div><dt>{{ t('modified') }}</dt><dd>{{ formatDate(selectedItem.modifiedAt) }}</dd></div><div><dt>{{ t('category') }}</dt><dd>{{ categoryLabel(selectedItem.category) }}</dd></div></dl>
        <ul v-if="selectedItem.ruleIds.length"><li v-for="rule in selectedItem.ruleIds" :key="rule">{{ ruleLabel(rule) }}</li></ul>
      </aside>
    </Transition>
  </div>
</template>
