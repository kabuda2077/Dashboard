import { computed, nextTick, onBeforeUnmount, ref, watch, type Ref } from 'vue'
import { provideCollapseTransition } from './collapseTransition'
import { useVirtualRowShift } from './virtualRowShift'

type Phase = 'idle' | 'preparing' | 'animating' | 'settling'

const FLOATING_CLASS = 'collapse-motion-floating'

const waitFrames = async (count: number, signal: AbortSignal) => {
  for (let i = 0; i < count; i++) {
    signal.throwIfAborted()
    await new Promise<void>((resolve, reject) => {
      const abort = () => {
        cancelAnimationFrame(frame)
        reject(signal.reason)
      }
      const frame = requestAnimationFrame(() => {
        signal.removeEventListener('abort', abort)
        resolve()
      })

      signal.addEventListener('abort', abort, { once: true })
    })
  }
  signal.throwIfAborted()
}

export const useCollapseMotion = (open: Ref<boolean>) => {
  const placeholderRef = ref<HTMLDivElement>()
  const cardRef = ref<HTMLDivElement>()
  const headerRef = ref<HTMLDivElement>()
  const bodyRef = ref<HTMLDivElement>()
  const previewRef = ref<HTMLDivElement>()
  const contentRef = ref<HTMLDivElement>()

  const phase = ref<Phase>('idle')
  const expanded = ref(open.value)
  const transitioning = computed(() => phase.value === 'preparing' || phase.value === 'animating')
  const showContent = computed(() => transitioning.value || expanded.value)
  const showPreview = computed(() => transitioning.value || !expanded.value)

  provideCollapseTransition(computed(() => phase.value !== 'idle'))

  const rowShift = useVirtualRowShift()
  let shiftRow: HTMLElement | null = null
  let shiftBaseHeight = 0
  let operation: AbortController | undefined

  const releaseShift = () => {
    placeholderRef.value?.classList.remove(FLOATING_CLASS)
    if (placeholderRef.value) placeholderRef.value.style.height = ''
    shiftRow = null
  }

  watch(open, async (value) => {
    const body = bodyRef.value
    if (!body) return

    // 取消旧动画前读取当前画面。使用布局高度，避免卡片入场的 scale 影响测量。
    const fromHeight = getComputedStyle(body).height
    const fromPreview = previewRef.value ? getComputedStyle(previewRef.value).opacity : '0'
    const fromContent = contentRef.value ? getComputedStyle(contentRef.value).opacity : '0'
    const contentReady = phase.value === 'animating'

    // 准备新内容的几帧也保持当前画面，取消 WAAPI 后不能闪回 CSS 的旧终点。
    if (previewRef.value) previewRef.value.style.opacity = fromPreview
    if (contentRef.value) contentRef.value.style.opacity = fromContent
    operation?.abort()
    operation = new AbortController()
    const { signal } = operation
    const animations: Animation[] = []
    const cancelAnimations = () => animations.forEach((animation) => animation.cancel())
    signal.addEventListener('abort', cancelAnimations, { once: true })

    try {
      phase.value = 'preparing'
      body.style.height = fromHeight
      await nextTick()
      // 展开需要等虚拟列表拿到容器尺寸并挂载可视行；收起只需等预览挂载。
      // 中途反向时两份内容已经挂好，可在同一帧接续，不暂停当前视觉进度。
      if (!contentReady) await waitFrames(value ? 2 : 1, signal)
      signal.throwIfAborted()

      const content = contentRef.value
      const preview = previewRef.value
      const targetHeight = value
        ? (content?.offsetHeight ?? 0) +
          (content ? parseFloat(getComputedStyle(content).marginTop) : 0)
        : (preview?.offsetHeight ?? 0)
      const placeholder = placeholderRef.value
      const card = cardRef.value
      const header = headerRef.value
      const row = placeholder?.parentElement
      const headerHeight = header?.offsetHeight ?? 0
      const baseHeight = shiftRow ? shiftBaseHeight : (card?.offsetHeight ?? 0)
      const style = getComputedStyle(body)
      const duration = style.getPropertyValue('--collapse-motion-duration').trim()
      const timing = {
        duration: parseFloat(duration) * (duration.endsWith('ms') ? 1 : 1000),
        easing: style.getPropertyValue('--collapse-motion-ease').trim(),
        fill: 'both' as const,
      }

      // 所有布局读取完成后，才写入占位和目标位移。
      if (rowShift && placeholder && row) {
        shiftBaseHeight = baseHeight
        rowShift.begin(row, headerHeight + targetHeight - baseHeight, timing, releaseShift)
        if (!shiftRow) {
          placeholder.style.height = `${baseHeight}px`
          placeholder.classList.add(FLOATING_CLASS)
          shiftRow = row
        }
      }

      phase.value = 'animating'
      expanded.value = value
      body.style.height = `${targetHeight}px`
      animations.push(body.animate({ height: [fromHeight, `${targetHeight}px`] }, timing))
      if (preview) {
        animations.push(preview.animate({ opacity: [fromPreview, value ? '0' : '1'] }, timing))
        preview.style.opacity = ''
      }
      if (content) {
        animations.push(content.animate({ opacity: [fromContent, value ? '1' : '0'] }, timing))
        content.style.opacity = ''
      }
      await Promise.all(animations.map((animation) => animation.finished))
      signal.throwIfAborted()

      // 先让最终内容回到自然布局，再交还占位，协调器量到的就是最终尺寸。
      phase.value = 'settling'
      body.style.height = ''
      await nextTick()
      signal.throwIfAborted()
      cancelAnimations()
      if (rowShift && shiftRow) await rowShift.end(shiftRow)

      // 同列交接完成后先绘制一帧，再恢复屏外节点的预渲染。
      await waitFrames(2, signal)
      phase.value = 'idle'
    } catch (error) {
      if (!signal.aborted) throw error
    } finally {
      signal.removeEventListener('abort', cancelAnimations)
      cancelAnimations()
    }
  })

  onBeforeUnmount(() => {
    operation?.abort()
    if (rowShift && shiftRow) rowShift.cancel(shiftRow)
    else releaseShift()
  })

  return {
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
  }
}
