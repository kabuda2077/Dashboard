<template>
  <div
    ref="containerRef"
    role="tablist"
    class="nav-menu nav-menu-horizontal relative"
    :class="block && 'nav-menu-block'"
  >
    <div
      aria-hidden="true"
      class="nav-indicator pointer-events-none absolute top-0 left-0"
      :class="{ 'nav-indicator-ready': ready }"
      :style="indicatorStyle"
    />
    <button
      v-for="opt in options"
      ref="segmentRefs"
      :key="opt.value"
      :data-value="opt.value"
      role="tab"
      type="button"
      class="nav-item"
      :aria-selected="modelValue === opt.value"
      :aria-haspopup="opt.hasPopup"
      :aria-expanded="opt.expanded"
      :aria-controls="opt.controls"
      :tabindex="modelValue === opt.value ? 0 : -1"
      @keydown="navigate($event, opt.value)"
      @click="select(opt.value, $event)"
    >
      <component
        :is="opt.icon"
        v-if="opt.icon"
        class="nav-item-icon"
        aria-hidden="true"
      />
      <span
        v-if="opt.label"
        class="truncate"
      >
        {{ opt.label }}
      </span>
      <span
        v-if="opt.count !== undefined && opt.count !== ''"
        class="nav-item-count"
      >
        {{ opt.count }}
      </span>
      <component
        :is="opt.suffixIcon"
        v-if="opt.suffixIcon"
        class="nav-item-suffix-icon"
        aria-hidden="true"
      />
    </button>
  </div>
</template>

<script setup lang="ts">
import { useElementSize } from '@vueuse/core'
import { nextTick, onUnmounted, ref, watch, type Component } from 'vue'

export type SegmentOption = {
  value: string
  label?: string
  count?: string | number
  icon?: Component
  suffixIcon?: Component
  hasPopup?: 'menu'
  expanded?: boolean
  controls?: string
}

const props = withDefaults(
  defineProps<{
    modelValue: string
    options: SegmentOption[]
    block?: boolean
  }>(),
  {
    block: false,
  },
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'reselect', value: string, event: MouseEvent): void
}>()

const containerRef = ref<HTMLDivElement>()
const segmentRefs = ref<HTMLButtonElement[]>([])
const { width } = useElementSize(containerRef)

const ready = ref(false)
let readyFrame: number | undefined
const indicatorStyle = ref({
  height: '0px',
  opacity: '0',
  transform: 'translate3d(0, 0, 0)',
  width: '0px',
})

const updateIndicator = async () => {
  await nextTick()
  const el = segmentRefs.value.find((s) => s?.dataset.value === props.modelValue)
  if (!el || !el.offsetWidth) {
    indicatorStyle.value.opacity = '0'
    return
  }

  indicatorStyle.value = {
    height: `${el.offsetHeight}px`,
    opacity: '1',
    transform: `translate3d(${el.offsetLeft}px, ${el.offsetTop}px, 0)`,
    width: `${el.offsetWidth}px`,
  }
  if (!ready.value) {
    if (readyFrame !== undefined) cancelAnimationFrame(readyFrame)
    readyFrame = requestAnimationFrame(() => {
      ready.value = true
      readyFrame = undefined
    })
  }
}

const select = (value: string, event: MouseEvent) => {
  if (value === props.modelValue) {
    emit('reselect', value, event)
    return
  }
  emit('update:modelValue', value)
}

const navigate = async (event: KeyboardEvent, value: string) => {
  const length = props.options.length
  if (!length || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  const index = props.options.findIndex((option) => option.value === value)
  const next = event.key === 'Home' ? 0 : event.key === 'End' ? length - 1
    : (index + (event.key === 'ArrowRight' ? 1 : -1) + length) % length
  const selected = props.options[next].value
  if (selected !== props.modelValue) emit('update:modelValue', selected)
  await nextTick()
  segmentRefs.value.find((element) => element.dataset.value === selected)?.focus()
}

onUnmounted(() => {
  if (readyFrame !== undefined) cancelAnimationFrame(readyFrame)
})

watch(() => [props.modelValue, props.options, width.value], updateIndicator, {
  immediate: true,
  deep: true,
})
</script>

<style scoped>
.nav-menu {
  width: auto;
  flex-direction: row;
  gap: 2px;
  border-radius: 0.625rem;
  padding: 3px;
  background-color: color-mix(in srgb, var(--color-base-content) 6%, transparent);
}

.nav-menu-block {
  width: 100%;
}

.nav-indicator {
  background-color: var(--color-primary);
  box-shadow:
    0 0 0 1px color-mix(in srgb, var(--color-base-content) 5%, transparent),
    0 1px 2px color-mix(in srgb, var(--color-base-content) 10%, transparent);
}

.nav-item {
  min-height: 26px;
  flex-shrink: 1;
  justify-content: center;
  gap: 0.375rem;
  padding: 3px 0.625rem;
  font-size: 0.875rem;
  line-height: 1.25rem;
}

.nav-menu-block .nav-item {
  flex: 1;
}

.nav-item:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 2px;
}

.nav-item-icon {
  width: 1rem;
  height: 1rem;
}

.nav-item-suffix-icon {
  width: 0.75rem;
  height: 0.75rem;
  margin-inline-start: -0.125rem;
  opacity: 0.7;
}

.nav-item:not([aria-selected='true']) {
  color: color-mix(in srgb, var(--color-base-content) 55%, transparent);
}

@media (hover: hover) {
  .nav-item:not([aria-selected='true']):hover {
    color: var(--color-base-content);
  }
}

.nav-item[aria-selected='true'] {
  color: var(--color-primary-content);
}

.nav-item[aria-selected='true'] .nav-item-icon {
  color: currentcolor;
}

.nav-item[aria-selected='true'] .nav-item-count {
  color: color-mix(in srgb, currentcolor 70%, transparent);
}
</style>
