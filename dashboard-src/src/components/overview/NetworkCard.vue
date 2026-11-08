<template>
  <div
    ref="card"
    class="network-card base-container w-full p-4"
  >
    <div class="network-card-grid">
      <section class="network-column network-card-tests">
        <div class="network-panel-header">
          <div
            class="network-switch-tabs"
            role="tablist"
            :aria-label="$t('networkTestsNavigation')"
          >
            <button
              type="button"
              role="tab"
              class="network-divider-tab"
              :class="{ active: activeTab === 'latency' }"
              :aria-selected="activeTab === 'latency'"
              :title="$t('networkLatencyHelp')"
              @click="activeTab = 'latency'"
            >
              {{ $t('latency') }}
            </button>
            <span
              class="network-tabs-divider"
              aria-hidden="true"
            />
            <button
              type="button"
              role="tab"
              class="network-divider-tab"
              :class="{ active: activeTab === 'routing' }"
              :aria-selected="activeTab === 'routing'"
              :title="$t('networkRoutingHelp')"
              @click="activeTab = 'routing'"
            >
              {{ $t('networkRouting') }}
            </button>
          </div>
          <button
            class="network-icon-button"
            type="button"
            :disabled="isActiveTabBusy || !backendSessionReady"
            :aria-label="
              activeTab === 'latency' ? $t('networkTestLatency') : $t('networkTestRouting')
            "
            @click="triggerActiveTest"
          >
            <BoltIcon
              class="h-3.5 w-3.5"
              :class="isActiveTabTesting ? 'animate-pulse' : ''"
            />
          </button>
        </div>

        <ConnectionStatus
          v-if="activeTab === 'latency'"
          :active="cardVisible && activeTab === 'latency'"
          hide-header
        />
        <RoutingCheck
          v-else
          :active="cardVisible && activeTab === 'routing'"
          hide-header
        />
      </section>
      <IPCheck />
    </div>
  </div>
</template>

<script setup lang="ts">
import ConnectionStatus from '@/components/overview/ConnectionStatus.vue'
import IPCheck from '@/components/overview/IPCheck.vue'
import RoutingCheck from '@/components/overview/RoutingCheck.vue'
import { backendSessionReady } from '@/helper/backendSession'
import { getLatency, isTestingLatency, latencyTargets } from '@/helper/overview'
import { getRouting, isTestingRouting, routingTargets } from '@/helper/routing-test'
import { BoltIcon } from '@heroicons/vue/24/outline'
import { computed, onMounted, onUnmounted, ref } from 'vue'

const card = ref<HTMLElement>()
const cardVisible = ref(false)
const activeTab = ref<'latency' | 'routing'>('latency')

const isActiveTabBusy = computed(() =>
  activeTab.value === 'latency'
    ? latencyTargets.every((target) => target.checking.value)
    : routingTargets.every((target) => target.checking.value),
)
const isActiveTabTesting = computed(() =>
  activeTab.value === 'latency' ? isTestingLatency.value : isTestingRouting.value,
)
const triggerActiveTest = () => {
  if (activeTab.value === 'latency') void getLatency()
  else void getRouting()
}

let visibilityObserver: IntersectionObserver | undefined
onMounted(() => {
  if (!card.value) return
  visibilityObserver = new IntersectionObserver(([entry]) => {
    cardVisible.value = entry?.isIntersecting ?? false
  })
  visibilityObserver.observe(card.value)
})
onUnmounted(() => {
  visibilityObserver?.disconnect()
})
</script>

<style>
.network-card {
  --color-base-content: light-dark(#1d1d1f, #ececef);
  --color-low-latency: light-dark(#3b866b, #55b490);
  --color-medium-latency: light-dark(#b88636, #dba950);
  --color-high-latency: light-dark(#bf5b56, #df746e);
  --network-surface: light-dark(#fafafb, #2b2b2f);
  --network-surface-hover: light-dark(#f3f3f6, #323237);
  --network-subtle: light-dark(rgba(29, 29, 31, 0.045), rgba(255, 255, 255, 0.06));
  container-type: inline-size;
  background: light-dark(#ffffff, #232326);
  color: var(--color-base-content);
  line-height: normal;
  box-shadow:
    0 1px 2px light-dark(rgba(0, 0, 0, 0.03), transparent),
    0 1px 3px light-dark(transparent, rgba(0, 0, 0, 0.28));
}
.network-card-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1rem;
}
.network-column {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.network-card-tests {
  container-type: inline-size;
  container-name: network-tests;
}
.network-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  height: 1.625rem;
}
.network-header-actions {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}
.network-icon-button {
  display: inline-grid;
  place-items: center;
  width: 1.5rem;
  height: 1.5rem;
  border: none;
  border-radius: 999px;
  padding: 0;
  background: transparent;
  color: var(--color-base-content);
  opacity: 0.72;
  cursor: pointer;
  transition:
    background-color 150ms ease,
    opacity 150ms ease;
}
.network-icon-button:hover:not(:disabled) {
  background: var(--network-subtle);
  opacity: 1;
}
.network-icon-button:disabled {
  cursor: default;
  opacity: 0.3;
}
.network-icon-button:focus-visible {
  outline: 1px solid var(--color-primary);
  outline-offset: 2px;
}
.network-icon-button svg {
  width: 0.875rem;
  height: 0.875rem;
  stroke-width: 1.75;
}
.network-switch-tabs {
  display: inline-flex;
  align-items: center;
  gap: 0.625rem;
}
.network-divider-tab {
  position: relative;
  border: none;
  background: none;
  padding: 0;
  font-family: inherit;
  font-size: 0.75rem;
  line-height: 1rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: color-mix(in srgb, var(--color-base-content) 30%, transparent);
  cursor: pointer;
  transition: color 150ms ease;
}
.network-divider-tab:hover,
.network-divider-tab.active {
  color: color-mix(in srgb, var(--color-base-content) 60%, transparent);
}
.network-divider-tab.active::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  bottom: -4px;
  height: 1.5px;
  border-radius: 999px;
  background: color-mix(in srgb, var(--color-base-content) 60%, transparent);
}
.network-tabs-divider {
  width: 1px;
  height: 11px;
  background: light-dark(rgba(29, 29, 31, 0.12), rgba(255, 255, 255, 0.14));
  flex-shrink: 0;
}
.network-test-page {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}
.network-test-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  grid-template-rows: repeat(4, minmax(0, 1fr));
  flex: 1;
  gap: 0.625rem;
  margin-top: 0.625rem;
  min-height: 0;
}
.network-bento-tile {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  width: 100%;
  min-width: 0;
  padding: 0.75rem 0.875rem 0.625rem;
  border-radius: 0.6875rem;
  border: none;
  background: var(--network-surface);
  font-family: inherit;
  color: inherit;
  text-align: left;
  cursor: pointer;
  transition: background-color 150ms ease;
}
.network-bento-tile:hover:not(:disabled),
.network-bento-tile:focus-visible {
  background: var(--network-surface-hover);
}
.network-bento-tile:focus-visible {
  outline: 1px solid var(--color-primary);
  outline-offset: 2px;
}
.network-bento-tile:disabled {
  cursor: default;
}
.network-tile-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.375rem;
  min-width: 0;
}
.network-tile-title-wrap {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  min-width: 0;
}
.network-tile-dot {
  width: 5.5px;
  height: 5.5px;
  border-radius: 999px;
  flex-shrink: 0;
}
.network-tile-name {
  font-size: 0.8125rem;
  line-height: 1.125rem;
  font-weight: 500;
  color: var(--color-base-content);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.network-tile-badge {
  font-size: 0.625rem;
  line-height: 1;
  padding: 2.5px 5.5px;
  border-radius: 0.25rem;
  background: var(--network-subtle);
  color: color-mix(in srgb, var(--color-base-content) 42%, transparent);
  flex-shrink: 0;
}
.network-tile-route-val {
  font-size: 0.8125rem;
  line-height: 1.625rem;
  font-weight: 450;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  margin-top: 0.5rem;
}
.network-tile-track {
  height: 2px;
  width: 100%;
  background: var(--network-subtle);
  border-radius: 999px;
  margin-top: 0.5rem;
  overflow: hidden;
}
.network-tile-track-fill {
  height: 100%;
  border-radius: 999px;
  opacity: 0.78;
  transition: width 300ms ease;
}
@container network-tests (min-width: 500px) {
  .network-test-grid {
    grid-template-columns: repeat(4, minmax(0, 1fr));
    grid-template-rows: repeat(2, minmax(0, 1fr));
  }
}
@container (min-width: 768px) {
  .network-card-grid {
    grid-template-columns: minmax(0, 11fr) minmax(0, 9fr);
    gap: 1.25rem;
  }
}
</style>
