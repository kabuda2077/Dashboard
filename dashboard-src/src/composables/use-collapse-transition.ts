import { inject, provide, type InjectionKey, type Ref } from 'vue'

/*
 * 折叠卡片限制预渲染的标记，覆盖展开 / 收起及外层列表的位置交接。
 *
 * 展开时它从内容挂载那一刻就是 true —— 卡片是在动画开始之前挂好的(见 collapseMotion.ts)，
 * 所以「动画期间」实际覆盖的是挂载 + 动画这一整段。虚拟列表在这段时间里只渲一屏，
 * 屏外的几行等同列动画完成、外层列表交接位置并绘制一帧之后再补上。
 */
const collapseTransitionKey: InjectionKey<Ref<boolean>> = Symbol('collapse-transition')

export const provideCollapseTransition = (transitioning: Ref<boolean>) => {
  provide(collapseTransitionKey, transitioning)
}

export const useCollapseTransition = () => inject(collapseTransitionKey, null)
