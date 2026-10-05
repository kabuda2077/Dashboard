<script setup lang="ts">
/*
 * 按订阅分段,每段各挂一个 ProxiesContent —— 段与段之间节点数差很多,合成一个虚拟列表
 * 就得把标题也当成行、行号跨段累加;分开挂则每段自己算自己的,列数一样、共用外面同一个
 * 滚动容器,视觉上和一整片网格没区别。
 */
import { groupProxiesByProviderName } from '@/composables/renderProxies'
import { computed, onBeforeUnmount, onBeforeUpdate, ref } from 'vue'
import ProxiesContent from './ProxiesContent.vue'

const props = defineProps<{
  name: string
  now: string
  renderProxies: string[]
}>()

const groupedProxies = computed(() => groupProxiesByProviderName(props.renderProxies))

/*
 * 每段的 scrollMargin 是「自己距滚动容器内容顶部的距离」。前面的段一变高(行高从估算
 * 修正成实测、卡片尺寸改了、筛选结果变了),后面的段全部错位,而它们自己的尺寸没变、
 * 收不到通知,所以任意一段高度变化都要让所有段重新量一次。
 *
 * 观察的是段容器而不是外层:两段一个变高一个变矮、总高不变的情况,外层观察不到。
 */
const sections = ref<(InstanceType<typeof ProxiesContent> | null)[]>([])
const observer = new ResizeObserver(() => {
  for (const section of sections.value) {
    section?.syncScrollMargin()
  }
})
const observedSections = new WeakSet<Element>()
const observeSection = (el: Element | null) => {
  if (!el || observedSections.has(el)) return

  observedSections.add(el)
  observer.observe(el)
}

onBeforeUpdate(() => {
  sections.value = []
})
onBeforeUnmount(() => {
  observer.disconnect()
})
</script>

<template>
  <div class="flex flex-col gap-2">
    <div
      v-for="({ providerName, proxies }, index) in groupedProxies"
      :key="providerName"
      :ref="(el) => observeSection(el as Element | null)"
    >
      <p
        class="my-2 text-sm font-semibold"
        v-if="providerName !== ''"
      >
        {{ providerName }}
      </p>
      <ProxiesContent
        :ref="(el) => (sections[index] = el as InstanceType<typeof ProxiesContent> | null)"
        :name="name"
        :now="now"
        :render-proxies="proxies"
      />
    </div>
  </div>
</template>
