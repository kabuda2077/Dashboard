// 折叠动画的时序回归：位置交接后才补屏外节点，并覆盖同列并发和快速反向切换。
// pnpm build && node test/collapse.mjs
import assert from 'node:assert/strict'
import { parseArgs } from 'node:util'
import { sleep } from './lib/cdp.mjs'
import { startHarness } from './lib/harness.mjs'

const { values } = parseArgs({
  options: {
    groups: { type: 'string', default: '30' },
    nodes: { type: 'string', default: '200' },
    dist: { type: 'string' },
    url: { type: 'string' },
    headful: { type: 'boolean', default: false },
  },
})
const harness = await startHarness({
  groups: Number(values.groups),
  nodes: Number(values.nodes),
  connections: 0,
  distDir: values.dist,
  appUrl: values.url,
  headless: !values.headful,
})

try {
  const page = await harness.openProxiesPage({
    settings: { 'config/two-columns': 'false', 'cache/collapse-group-map': '{}' },
  })

  await page.waitForCards(2)
  await page.waitForLayout()
  await page.evaluate(`(() => {
    window.__collapseErrors = []
    addEventListener('unhandledrejection', (event) => window.__collapseErrors.push(String(event.reason)))
    addEventListener('error', (event) => window.__collapseErrors.push(event.message))
  })()`)

  const run = (actions) =>
    page.evaluateJson(`(async () => {
      const cards = [...document.querySelectorAll('[data-group-name]')].slice(0, 2)
      const read = () => cards.map((card) => ({
        open: Boolean(card.querySelector('.collapse-motion-open')),
        floating: card.classList.contains('collapse-motion-floating'),
        transitioning: Boolean(card.querySelector('.collapse-motion-body-transitioning')),
        rows: card.querySelectorAll('.collapse-motion-content [data-index]').length,
        preview: Boolean(card.querySelector('.collapse-motion-preview')),
        height: card.style.height,
        bodyHeight: card.querySelector('.collapse-motion-body').style.height,
        animations: card.getAnimations({ subtree: true }).length,
        animationStates: card.getAnimations({ subtree: true }).map((animation) => ({
          state: animation.playState, time: animation.currentTime, target: animation.effect.target.className,
        })),
      }))
      const frames = []
      let recording = true
      const sample = () => {
        frames.push(read())
        if (recording) requestAnimationFrame(sample)
      }
      requestAnimationFrame(sample)

      for (const [delay, index] of ${JSON.stringify(actions)}) {
        if (delay) await new Promise((resolve) => setTimeout(resolve, delay))
        cards[index].querySelector('.collapse-motion-header').click()
      }
      const deadline = performance.now() + 3000
      do {
        await new Promise((resolve) => setTimeout(resolve, 25))
      } while (performance.now() < deadline && cards.some((card) =>
        card.classList.contains('collapse-motion-floating') ||
        card.querySelector('.collapse-motion-body-transitioning')
      ))
      await new Promise((resolve) => setTimeout(resolve, 100))
      // Dashboard adds CSS entrance/hover animations alongside collapse WAAPI.
      // Wait for all owned card animations to settle, then retain the zero-animation assertion.
      while (performance.now() < deadline && cards.some((card) =>
        card.getAnimations({ subtree: true }).length
      )) {
        await new Promise((resolve) => setTimeout(resolve, 25))
      }
      recording = false

      const rows = cards.map((card) => card.parentElement.getBoundingClientRect())
      return JSON.stringify({
        frames,
        final: read(),
        shifted: document.querySelectorAll('.virtual-row-shift-row').length,
        gap: rows[1].top - rows[0].bottom,
        errors: window.__collapseErrors.slice(),
      })
    })()`)

  const checkSettled = (result, expected) => {
    assert.deepEqual(result.errors, [], '动画过程不应出现运行错误')
    assert.deepEqual(
      result.final.map((card) => card.open),
      expected,
    )
    assert.equal(result.shifted, 0, '交接后应清除后续行的位移：' + JSON.stringify(result.final))
    assert.ok(Math.abs(result.gap - 12) < 1, `交接后行间距异常：${result.gap}`)
    for (const [index, card] of result.final.entries()) {
      assert.equal(card.floating, false)
      assert.equal(card.transitioning, false)
      assert.equal(card.height, '')
      assert.equal(card.bodyHeight, '')
      assert.equal(
        card.animations,
        0,
        '交接后应释放动画对象：' + JSON.stringify(card.animationStates),
      )
      assert.equal(card.preview, !expected[index])
      assert.equal(card.rows > 0, expected[index])
    }
  }

  const checkDeferredRows = (result, index) => {
    const floatingIndex = result.frames.findIndex((frame) => frame[index].floating)
    assert.ok(floatingIndex >= 0, '必须实际经历浮动动画')
    const handoff = result.frames
      .slice(floatingIndex + 1)
      .find((frame) => !frame[index].floating && !frame[index].transitioning)

    assert.ok(handoff, '应观察到位置交接')
    assert.ok(handoff[index].rows > 0, '交接时可视节点仍应存在')
    assert.ok(result.final[index].rows > handoff[index].rows, '屏外节点应在位置交接绘制后才补上')
  }

  const opened = await run([[0, 0]])
  checkSettled(opened, [true, false])
  checkDeferredRows(opened, 0)
  console.log('✓ 单组展开：交接绘制后才补屏外节点')

  checkSettled(await run([[0, 0]]), [false, false])
  console.log('✓ 单组收起：内容卸载且行位置恢复')

  const concurrent = await run([
    [0, 0],
    [80, 1],
  ])
  checkSettled(concurrent, [true, true])
  checkDeferredRows(concurrent, 0)
  checkDeferredRows(concurrent, 1)
  assert.ok(
    concurrent.frames.some(
      (frame) => frame[0].floating && !frame[0].transitioning && frame[1].transitioning,
    ),
    '先结束的组应等待同列的另一组交接',
  )
  console.log('✓ 同列错峰展开：等待整批交接后补渲染')

  checkSettled(
    await run([
      [0, 0],
      [0, 1],
    ]),
    [false, false],
  )
  console.log('✓ 同列同时收起：无残留占位或位移')

  checkSettled(
    await run([
      [0, 0],
      [1, 0],
    ]),
    [false, false],
  )
  checkSettled(
    await run([
      [0, 0],
      [100, 0],
      [50, 0],
    ]),
    [true, false],
  )
  console.log('✓ 快速取消与动画反向：最终状态正确')

  checkSettled(await run([[0, 0]]), [false, false])
  checkSettled(
    await run([
      [0, 0],
      [100, 1],
      [200, 0],
    ]),
    [false, true],
  )
  console.log('✓ 等待同列交接时反向：旧操作不会覆盖新状态')
  checkSettled(await run([[0, 1]]), [false, false])

  await page.call('Emulation.setEmulatedMedia', {
    features: [{ name: 'prefers-reduced-motion', value: 'reduce' }],
  })
  checkSettled(
    await run([
      [0, 0],
      [0, 1],
    ]),
    [true, true],
  )
  checkSettled(
    await run([
      [0, 0],
      [0, 1],
    ]),
    [false, false],
  )
  console.log('✓ 减少动态效果：零时长动画正常交接和清理')
  await page.call('Emulation.setEmulatedMedia', { features: [] })

  await page.evaluate(`(() => {
    const preview = document.querySelector('.collapse-motion-preview')
    const style = document.createElement('style')
    style.id = 'equal-collapse-height'
    style.textContent = '.collapse-motion-content { height: ' + (preview.offsetHeight - 8) + 'px !important }'
    document.head.append(style)
  })()`)
  checkSettled(await run([[0, 0]]), [true, false])
  checkSettled(await run([[0, 0]]), [false, false])
  await page.evaluate(`document.getElementById('equal-collapse-height').remove()`)
  console.log('✓ 展开与收起高度相同：仍能完成内容切换')

  // 在动画播放中通过搜索卸载代理组，所有等待与 WAAPI 动画均应被取消。
  await page.evaluate(`(() => {
    document.querySelector('.collapse-motion-header').click()
    setTimeout(() => {
      const input = document.querySelector('input[placeholder*="earch"]')
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, '__no_matching_group__')
      input.dispatchEvent(new Event('input', { bubbles: true }))
    }, 100)
  })()`)
  await sleep(700)
  assert.equal(
    await page.evaluate(`document.querySelectorAll('.collapse-motion-placeholder').length`),
    0,
  )
  assert.deepEqual(await page.evaluate(`window.__collapseErrors`), [])
  console.log('✓ 动画中卸载：无残留节点、运行错误或未处理的取消拒绝')

  await page.close()
} finally {
  await harness.close()
}
