import { inject, nextTick, provide, type InjectionKey } from 'vue'

/*
 * 虚拟列表里某一行改高度，后面所有行的 top 都要 JS 重算一遍再 patch 回 DOM ——
 * 折叠卡片逐帧变高的话，整列就是每帧重排一次。
 *
 * 动画期间改走这里：正在变化的卡片固定住占位高度、自己浮起来改变高度；列协调器记录
 * 每一行最终会产生的高度差，然后给后续行写入其上方所有高度差的前缀和。这样同列多个组
 * 一起展开 / 收起时也只改 transform，不会互相打断后退回逐帧重排。
 *
 * 一批动画全部结束后再一起释放占位并同步真实高度给 virtualizer。下一次 Vue patch 里
 * virtualizer 的 top 和这里撤掉的 transform 正好抵消，因此收尾也不会跳。
 */
export type VirtualRowShift = {
  begin: (
    row: HTMLElement,
    offset: number,
    timing: KeyframeAnimationOptions,
    release: () => void,
  ) => void
  end: (row: HTMLElement) => Promise<void>
  cancel: (row: HTMLElement) => void
  destroy: () => void
}

type RowShiftState = {
  offset: number
  finished: boolean
  release: () => void
  settled?: Promise<void>
  resolve?: () => void
}

const ROW_CLASS = 'virtual-row-shift-row'
const SHIFT_VAR = '--virtual-row-shift'

const virtualRowShiftKey: InjectionKey<VirtualRowShift> = Symbol('virtual-row-shift')

export const provideVirtualRowShift = (shift: VirtualRowShift) => {
  provide(virtualRowShiftKey, shift)
}

export const useVirtualRowShift = () => inject(virtualRowShiftKey, null)

export const createVirtualRowShift = (
  getColumn: () => HTMLElement | undefined,
  measureRow: (row: HTMLElement) => void,
): VirtualRowShift => {
  const shifts = new Map<HTMLElement, RowShiftState>()
  const rowAnimations = new Map<HTMLElement, { offset: number; animation?: Animation }>()
  let observer: MutationObserver | undefined
  let observedColumn: HTMLElement | undefined
  let settling = false

  const clearRowStyle = (row: HTMLElement) => {
    rowAnimations.get(row)?.animation?.cancel()
    rowAnimations.delete(row)
    row.classList.remove(ROW_CLASS)
    row.style.removeProperty(SHIFT_VAR)
  }

  /*
   * 每个已挂载行的位移 = 它上方所有动画行的目标高度差之和。动画 origin 自己也要吃到
   * 更早 origin 的位移，所以先给当前行写前缀和，再把当前行的差值累加进去。
   */
  const applyOffsets = (timing?: KeyframeAnimationOptions) => {
    const column = getColumn()

    if (!column) return

    let offset = 0
    let followsOrigin = false
    const changes: { row: HTMLElement; offset?: number; from?: string }[] = []

    // 重定向时先读完当前视觉位置，再一起写目标，避免逐行交替读写样式。
    for (const child of column.children) {
      const row = child as HTMLElement

      if (followsOrigin) {
        if (rowAnimations.get(row)?.offset !== offset) {
          changes.push({ row, offset, from: timing ? getComputedStyle(row).transform : undefined })
        }
      } else if (rowAnimations.has(row)) {
        changes.push({ row })
      }

      const state = shifts.get(row)

      if (state) {
        followsOrigin = true
        offset += state.offset
      }
    }

    for (const { row, offset, from } of changes) {
      clearRowStyle(row)
      if (offset === undefined) continue

      row.classList.add(ROW_CLASS)
      row.style.setProperty(SHIFT_VAR, `${offset}px`)
      const animation = timing
        ? row.animate({ transform: [from!, `translateY(${offset}px)`] }, timing)
        : undefined

      // 连点会取消旧位移动画；完成交接由卡片的动画生命周期负责。
      animation?.finished.catch(() => {})
      rowAnimations.set(row, { offset, animation })
    }

    for (const row of rowAnimations.keys()) {
      if (row.parentElement !== column) clearRowStyle(row)
    }
  }

  // 动画中滚动会让 virtualizer 换一批 DOM 行，新挂载的行也要立刻拿到正确的前缀和。
  const observeColumn = () => {
    const column = getColumn()

    if (!column || column === observedColumn) return

    observer?.disconnect()
    observedColumn = column
    observer = new MutationObserver(() => applyOffsets())
    observer.observe(column, { childList: true })
  }

  const stopObservingColumn = () => {
    observer?.disconnect()
    observer = undefined
    observedColumn = undefined
  }

  const releaseAll = () => {
    if (!shifts.size || settling) return

    settling = true

    for (const state of shifts.values()) {
      state.release()
    }

    for (const row of shifts.keys()) {
      if (row.isConnected) {
        measureRow(row)
      }
    }

    /*
     * measureRow 会同步更新 virtualizer 数据，DOM 的 top 在下一次 Vue patch 落下。等同一个
     * nextTick 再撤 transform，二者在一次绘制前完成，视觉位置保持不变。
     */
    nextTick(() => {
      const callbacks = [...shifts.values()].map((state) => state.resolve)

      shifts.clear()
      applyOffsets()
      stopObservingColumn()
      settling = false

      for (const callback of callbacks) {
        callback?.()
      }
    })
  }

  const shift: VirtualRowShift = {
    begin: (row, offset, timing, release) => {
      observeColumn()

      const state = shifts.get(row)

      if (state) {
        state.offset = offset
        state.finished = false
        state.release = release
      } else {
        shifts.set(row, { offset, finished: false, release })
      }

      applyOffsets(timing)
    },
    end: (row) => {
      const state = shifts.get(row)

      if (!state) return Promise.resolve()

      state.finished = true
      state.settled ??= new Promise<void>((resolve) => {
        state.resolve = resolve
      })

      if ([...shifts.values()].every((item) => item.finished)) {
        releaseAll()
      }

      return state.settled
    },
    /*
     * 行在动画中被卸载时不能继续等同批的其他行，否则已经不存在的占位无法在批末测量。
     * 此时整批提前落位；这是滚动 / 过滤时的低频兜底，正常的并发展开仍走批量收尾。
     */
    cancel: (row) => {
      const state = shifts.get(row)

      if (!state) return

      releaseAll()
    },
    destroy: () => {
      stopObservingColumn()

      for (const state of shifts.values()) {
        state.release()
        state.resolve?.()
      }

      shifts.clear()

      for (const row of rowAnimations.keys()) clearRowStyle(row)
    },
  }

  return shift
}
