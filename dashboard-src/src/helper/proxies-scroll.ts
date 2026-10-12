import { ref, type InjectionKey } from 'vue'

/*
 * 虚拟化之后,节点卡片可能不在渲染窗口里 —— 想滚到某个节点得让列表自己去滚,
 * 卡片拿不到自己的 DOM 也就无从滚起。由 ProxiesContent 提供。
 * 定位一律瞬时:动态行高下带动画的滚动会被沿途的测量修正打断,停在半路。
 */
export type ScrollProxyNodeIntoView = (name: string) => void

export const scrollNodeIntoViewKey: InjectionKey<ScrollProxyNodeIntoView> =
  Symbol('scrollNodeIntoView')

/*
 * 测速后的高亮提到模块作用域:重排会把卡片挪出渲染窗口再挪回来,
 * 状态留在组件实例里的话,卡片一卸载高亮就没了。
 */
export const highlightedProxyNode = ref('')

let highlightTimer: ReturnType<typeof setTimeout> | undefined

export const highlightProxyNode = (name: string) => {
  highlightedProxyNode.value = name
  clearTimeout(highlightTimer)
  highlightTimer = setTimeout(() => {
    if (highlightedProxyNode.value === name) {
      highlightedProxyNode.value = ''
    }
  }, 1500)
}
