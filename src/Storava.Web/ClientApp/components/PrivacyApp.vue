<script setup lang="ts">
import { watchEffect } from 'vue';
import BrandMark from '@/components/BrandMark.vue';
import PreferenceControls from '@/components/PreferenceControls.vue';
import { usePreferences } from '@/composables/usePreferences';

const { t } = usePreferences();

watchEffect(() => {
  document.title = `${t('privacyMetaTitle')} · ${t('productName')} Web`;
});
</script>

<template>
  <div class="privacy-page">
    <header class="site-header site-header--solid">
      <div class="container site-header__inner">
        <BrandMark />
        <div class="site-header__actions">
          <PreferenceControls />
          <a class="button button--small button--ink" href="/">{{ t('backToProduct') }}</a>
        </div>
      </div>
    </header>
    <section class="privacy-page__hero">
      <div class="container">
        <p class="kicker">{{ t('privacyPageKicker') }}</p>
        <h1>{{ t('privacyPageTitle') }}</h1>
        <p>{{ t('privacyPageLead') }}</p>
      </div>
    </section>
    <section class="privacy-flow">
      <div class="container">
        <div class="privacy-flow__track">
          <article><span>01</span><strong>{{ t('privacyFlowBrowser') }}</strong></article>
          <i aria-hidden="true">→</i>
          <article><span>02</span><strong>{{ t('privacyFlowWorker') }}</strong></article>
          <i aria-hidden="true">→</i>
          <article><span>03</span><strong>{{ t('privacyFlowDatabase') }}</strong></article>
          <div class="privacy-flow__barrier" aria-hidden="true">×</div>
          <article class="privacy-flow__server"><span>04</span><strong>{{ t('privacyFlowServer') }}</strong><small>{{ t('privacyFlowBlocked') }}</small></article>
        </div>
        <div class="privacy-detail-grid">
          <article>
            <span class="feature-icon feature-icon--lime">
              <svg viewBox="0 0 32 32"><path d="M8 8 24 24M24 8 8 24" /></svg>
            </span>
            <h2>{{ t('dataNeverSent') }}</h2>
            <p>{{ t('dataNeverSentBody') }}</p>
          </article>
          <article>
            <span class="feature-icon">
              <svg viewBox="0 0 32 32"><path d="M16 4v24M4 16h24" /></svg>
            </span>
            <h2>{{ t('dataMayLeave') }}</h2>
            <p>{{ t('dataMayLeaveBody') }}</p>
          </article>
          <article>
            <span class="feature-icon">
              <svg viewBox="0 0 32 32"><path d="M5 8h22v16H5zM5 13h22" /></svg>
            </span>
            <h2>{{ t('serverSurface') }}</h2>
            <p>{{ t('serverSurfaceBody') }}</p>
          </article>
        </div>
      </div>
    </section>
  </div>
</template>
