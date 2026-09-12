<template>
  <div
    ref="placeholderRef"
    class="collapse-motion-placeholder"
  >
    <div
      ref="cardRef"
      class="group collapse-motion collapse"
      :class="expanded && 'collapse-motion-open'"
    >
      <div
        ref="headerRef"
        class="collapse-motion-header relative cursor-pointer px-4 pt-4"
        @click="showCollapse = !showCollapse"
      >
        <slot name="title" />
      </div>
      <div
        ref="bodyRef"
        class="collapse-motion-body"
        :class="transitioning && 'collapse-motion-body-transitioning'"
      >
        <div
          v-if="showPreview"
          ref="previewRef"
          class="collapse-motion-preview px-4 pb-4"
        >
          <slot name="preview" />
        </div>
        <div
          v-if="showContent"
          ref="contentRef"
          class="collapse-motion-content mt-2 max-h-108 overflow-y-auto p-4 pt-0"
          :class="PROXIES_PARENT_CLASS"
        >
          <slot name="content" />
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useCollapseMotion } from '@/composables/collapseMotion'
import { PROXIES_PARENT_CLASS } from '@/helper/utils'
import { collapseGroupMap } from '@/store/settings'
import { computed } from 'vue'

const props = defineProps<{
  name: string
  forceOpen?: boolean
}>()

const showCollapse = computed({
  get: () => Boolean(props.forceOpen || collapseGroupMap.value[props.name]),
  set(value) {
    if (!props.forceOpen) collapseGroupMap.value[props.name] = value
  },
})

const {
  placeholderRef,
  cardRef,
  headerRef,
  bodyRef,
  previewRef,
  contentRef,
  expanded,
  transitioning,
  showContent,
  showPreview,
} = useCollapseMotion(showCollapse)
</script>
