<template>
  <div @keydown="handleTabKeydown">
    <SegmentedControl
      v-model="connectionTabShow"
      :options="tabOptions"
      :block="!horizental"
      @reselect="handleReselect"
    />
  </div>
  <Teleport to="#app-content">
    <Transition name="floating-menu">
      <div
        v-if="isMenuOpen"
        :id="menuId"
        ref="menuRef"
        role="menu"
        :aria-label="t('allConnections')"
        class="floating-menu-panel"
        :style="panelStyle"
        @keydown="handleMenuKeydown"
      >
        <button
          v-for="(item, index) in menuItems"
          :key="String(item.closedOnly)"
          type="button"
          role="menuitemradio"
          :aria-checked="showClosedOnly === item.closedOnly"
          :tabindex="index === focusedIndex ? 0 : -1"
          class="floating-menu-option w-full"
          @focus="focusedIndex = index"
          @click="selectFilter(item.closedOnly)"
        >
          <span class="flex-1 text-left whitespace-nowrap">{{ item.label }}</span>
          <CheckIcon
            class="h-4 w-4"
            :class="showClosedOnly !== item.closedOnly && 'invisible'"
          />
        </button>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { useFloatingMenu } from '@/composables/use-floating-menu'
import { CONNECTION_TAB_TYPE } from '@/constant'
import {
  connections,
  connectionTabShow,
  renderConnections,
  showClosedOnly,
} from '@/store/connections'
import { CheckIcon, ChevronDownIcon } from '@heroicons/vue/24/outline'
import { computed, nextTick, ref, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import SegmentedControl from '../common/SegmentedControl.vue'

defineProps({ horizental: { type: Boolean, default: true } })
const { t } = useI18n()
const menuId = `connection-filter-${useId().replace(/[^\w-]/g, '')}`
const isMenuOpen = ref(false)
const menuRef = ref<HTMLDivElement>()
const anchorRef = ref<HTMLButtonElement>()
const focusedIndex = ref(0)
const { panelStyle, remeasure } = useFloatingMenu(anchorRef, menuRef, isMenuOpen)
const connectionsCount = computed(() =>
  renderConnections.value.length !== connections.value.length
    ? `${renderConnections.value.length} / ${connections.value.length}`
    : connections.value.length,
)
const tabOptions = computed(() =>
  [CONNECTION_TAB_TYPE.ACTIVE, CONNECTION_TAB_TYPE.ALL].map((tab) => ({
    value: tab,
    label: t(tab === CONNECTION_TAB_TYPE.ALL && showClosedOnly.value ? 'closedConnections' : tab),
    count: connectionTabShow.value === tab ? connectionsCount.value : undefined,
    suffixIcon:
      tab === CONNECTION_TAB_TYPE.ALL && connectionTabShow.value === tab
        ? ChevronDownIcon
        : undefined,
    hasPopup: tab === CONNECTION_TAB_TYPE.ALL ? ('menu' as const) : undefined,
    expanded: tab === CONNECTION_TAB_TYPE.ALL ? isMenuOpen.value : undefined,
    controls: tab === CONNECTION_TAB_TYPE.ALL ? menuId : undefined,
  })),
)
const menuItems = computed(() => [
  { label: t('allConnections'), closedOnly: false },
  { label: t('closedConnections'), closedOnly: true },
])
const focusItem = async (index: number) => {
  focusedIndex.value = index
  await nextTick()
  menuRef.value?.querySelectorAll<HTMLButtonElement>('button')[index]?.focus()
}
const openMenu = (anchor: HTMLButtonElement) => {
  anchorRef.value = anchor
  isMenuOpen.value = true
  void focusItem(showClosedOnly.value ? 1 : 0)
}
const closeMenu = (returnFocus = false) => {
  isMenuOpen.value = false
  if (returnFocus) anchorRef.value?.focus()
}
const handleReselect = (value: string, event: MouseEvent) => {
  if (value !== CONNECTION_TAB_TYPE.ALL) return
  if (isMenuOpen.value) closeMenu(true)
  else openMenu(event.currentTarget as HTMLButtonElement)
}
const handleTabKeydown = (event: KeyboardEvent) => {
  const anchor = (event.target as HTMLElement).closest<HTMLButtonElement>('button[data-value]')
  if (
    anchor?.dataset.value !== CONNECTION_TAB_TYPE.ALL ||
    !['ArrowDown', 'ArrowUp'].includes(event.key)
  )
    return
  event.preventDefault()
  openMenu(anchor)
}
const handleMenuKeydown = (event: KeyboardEvent) => {
  if (event.key === 'Escape') {
    event.preventDefault()
    event.stopPropagation()
    closeMenu(true)
  } else if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
    event.preventDefault()
    const index =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? 1
          : (focusedIndex.value + (event.key === 'ArrowDown' ? 1 : -1) + menuItems.value.length) %
            menuItems.value.length
    void focusItem(index)
  } else if (event.key === 'Tab') closeMenu(true)
}
const selectFilter = (closedOnly: boolean) => {
  showClosedOnly.value = closedOnly
  closeMenu(true)
}
watch(connectionTabShow, () => closeMenu())
watch(menuItems, remeasure)
</script>
