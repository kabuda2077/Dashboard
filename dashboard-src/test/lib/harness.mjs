/*
 * 把「mock 后端 + 静态服务(dist) + headless Chrome + 打开代理页」串成一件事。
 * bench 与 verify 都从这里起步,免得各写一份启动流程。
 */
import { existsSync } from 'node:fs'
import { cp, mkdir, mkdtemp, rm } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { createMockServer } from '../mock-server.mjs'
import { connectBrowser, waitFor } from './cdp.mjs'
import { launchChrome } from './chrome.mjs'
import { findFreePort } from './ports.mjs'
import { serveDirectory } from './static-server.mjs'

const REPO_ROOT = join(import.meta.dirname, '..', '..')

// Source: zashboard Release v3.29.1, 50717b02dac68425cdc5b80cb754bea768ee4fc9.
// Dashboard's build pipeline publishes the browser preview here.
export const DIST_DIR = resolve(REPO_ROOT, '..', 'resources', 'dashboard')

export const startHarness = async ({
  groups = 150,
  nodes = 60,
  connections = 300,
  appUrl,
  distDir = DIST_DIR,
  headless = true,
} = {}) => {
  for (const [name, value, minimum] of [
    ['groups', groups, 1],
    ['nodes', nodes, 1],
    ['conns', connections, 0],
  ]) {
    if (!Number.isSafeInteger(value) || value < minimum)
      throw new Error(`${name} must be an integer >= ${minimum}`)
  }
  if (appUrl) {
    const url = new URL(appUrl)
    if (url.protocol !== 'http:' || !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)) {
      throw new Error('--url must point to a local HTTP browser preview')
    }
    appUrl = url.href.replace(/\/$/, '')
  } else if (!existsSync(join(distDir, 'index.html'))) {
    throw new Error(
      `Browser preview missing at ${distDir}. Build through the project pipeline, or pass --dist / --url.`,
    )
  }

  let runDirectory, snapshot, mock, app, chrome, browser, closing
  const close = () =>
    (closing ??= (async () => {
      process.removeListener('SIGINT', onSignal)
      process.removeListener('SIGTERM', onSignal)
      const results = await Promise.allSettled([
        Promise.resolve().then(() => browser?.close()),
        Promise.resolve().then(() => chrome?.close()),
        Promise.resolve().then(() => app?.close()),
        Promise.resolve().then(() => mock?.close()),
      ])
      if (runDirectory)
        await rm(runDirectory, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 })
      const failures = results.filter((result) => result.status === 'rejected')
      if (failures.length)
        throw new AggregateError(
          failures.map((result) => result.reason),
          'Harness cleanup failed',
        )
    })())
  const onSignal = () => {
    close().then(
      () => process.exit(130),
      (error) => {
        console.error(error)
        process.exit(1)
      },
    )
  }

  try {
    const testsRoot = resolve(REPO_ROOT, '..', '.tmp', 'tests')
    await mkdir(testsRoot, { recursive: true })
    runDirectory = await mkdtemp(join(testsRoot, 'proxy-'))
    if (!appUrl) {
      snapshot = join(runDirectory, 'dist')
      await cp(resolve(distDir), snapshot, { recursive: true })
      console.error(`Preview snapshot: ${resolve(distDir)}`)
    }
    mock = await createMockServer({ groups, nodes, connections })
    app = appUrl ? { url: appUrl, close: () => {} } : await serveDirectory(snapshot)
    const debuggingPort = await findFreePort()
    chrome = await launchChrome({ port: debuggingPort, headless, runDirectory })
    browser = await connectBrowser(debuggingPort)
    process.once('SIGINT', onSignal)
    process.once('SIGTERM', onSignal)

    /*
     * 直接开 #/proxies?hostname=... 是不行的:没有后端时路由守卫会先跳到 /setup,
     * hash 上的 query 在这一跳里就丢了。所以先落地 localStorage 再进代理页。
     */
    const openProxiesPage = async ({ cpuThrottling = 1, viewport = {}, settings = {} } = {}) => {
      const width = viewport.width ?? 1440
      const height = viewport.height ?? 900
      if (
        !Number.isSafeInteger(width) ||
        width <= 0 ||
        !Number.isSafeInteger(height) ||
        height <= 0
      ) {
        throw new Error('Viewport width and height must be positive integers')
      }
      if (!Number.isFinite(cpuThrottling) || cpuThrottling < 1) throw new Error('cpu must be >= 1')
      const page = await browser.newPage({ cpuThrottling, width, height })

      // Seed on the preview's origin before Vue initializes its session and router.
      // Each page has its own browser context; settings cannot leak between scenarios.
      await page.call('Page.addScriptToEvaluateOnNewDocument', {
        source: `
      if (location.origin === ${JSON.stringify(new URL(app.url).origin)}) {
      localStorage.setItem('setup-v2/backends', ${JSON.stringify(
        JSON.stringify([
          {
            uuid: 'perf',
            type: 'clash',
            protocol: 'http',
            host: '127.0.0.1',
            port: String(mock.port),
            password: '',
            secondaryPath: '',
            label: 'perf',
          },
        ]),
      )})
      localStorage.setItem('setup-v2/active', 'perf')
      ${Object.entries({ 'config/language': 'en-US', ...settings })
        .map(
          ([key, value]) =>
            `localStorage.setItem(${JSON.stringify(key)}, ${JSON.stringify(value)})`,
        )
        .join('\n      ')}
      }
    `,
      })

      // Measure navigation through first visible cards, with configuration already seeded.
      const navigationStartedAt = Date.now()
      await page.goto(`${app.url}/#/proxies`)

      const proxiesPage = withProxiesHelpers(page)

      proxiesPage.navigationStartedAt = navigationStartedAt
      // 起不来就当场说清楚:多半是 dist 太旧、mock 没连上,或者路由守卫把页面留在了 /setup
      // 轮询间隔放宽些:限速跑基准时,每次 evaluate 都在跟被测页面抢主线程
      const ready = await waitFor(
        async () =>
          await page.evaluate(
            `Boolean(document.querySelector('.overflow-y-scroll')) && location.hash.includes('proxies')`,
          ),
        { timeout: 30000, interval: 200 },
      )

      if (ready === null) {
        const href = await page.evaluate('location.href')
        const text = await page.evaluate('document.body.innerText.slice(0, 200)')

        await page.close()
        throw new Error(`代理页没能打开(${href})\n页面上是:${text.replace(/\n/g, ' | ')}`)
      }

      return proxiesPage
    }

    return {
      mock,
      app,
      openProxiesPage,
      setMockControl: (control) =>
        fetch(`${mock.url}/__mock/control`, {
          method: 'POST',
          body: JSON.stringify(control),
        }).then((res) => res.json()),
      close,
    }
  } catch (error) {
    await close()
    throw error
  }
}

// 代理页上反复要问的那几个问题,集中在这里
const withProxiesHelpers = (page) => {
  const helpers = {
    groupCardCount: () => page.evaluate(`document.querySelectorAll('[data-group-name]').length`),
    domNodeCount: () => page.evaluate(`document.getElementsByTagName('*').length`),
    scrollMetrics: () =>
      page.evaluateJson(`(() => {
        const el = document.querySelector('.overflow-y-scroll')
        return JSON.stringify(el ? { top: el.scrollTop, height: el.scrollHeight, client: el.clientHeight } : {})
      })()`),
    scrollTo: (top) =>
      page.evaluate(`(() => {
        const el = document.querySelector('.overflow-y-scroll')
        el.scrollTop = ${top}
        el.dispatchEvent(new Event('scroll'))
      })()`),
    // Observe layout over a quiet interval; a single frame can precede virtual-row measurement.
    async waitForLayout() {
      let previous, stableSince
      const settled = await waitFor(
        async () => {
          const state = await page.evaluate(`(() => {
          const scroller = document.querySelector('.overflow-y-scroll')
          if (!scroller || !scroller.querySelector('[data-group-name]')) return null
          const cards = [...scroller.querySelectorAll('[data-group-name]')]
          const moving = cards.some((card) =>
            card.classList.contains('collapse-motion-floating') ||
            card.querySelector('.collapse-motion-body-transitioning') ||
            card.getAnimations({ subtree: true }).some((animation) =>
              (animation.pending || animation.playState === 'running') &&
              animation.effect?.getTiming().iterations !== Infinity
            )
          )
          if (moving) return null
          return JSON.stringify([
            scroller.scrollTop, scroller.scrollHeight, scroller.clientHeight,
            cards.map((card) => {
              const rect = card.getBoundingClientRect()
              return [card.dataset.groupName, rect.top, rect.height,
                card.querySelectorAll('[data-index]').length,
                [...card.querySelectorAll('.latency-tag')].map((tag) => tag.textContent)]
            }),
          ])
        })()`)
          if (state === null || state !== previous) {
            previous = state
            stableSince = Date.now()
            return false
          }
          return Date.now() - stableSince >= 400
        },
        { timeout: 10000, interval: 100 },
      )
      if (settled === null) throw new Error('Proxy layout did not settle within 10 seconds')
    },
    // 返回元素中心点坐标,拿来发鼠标事件
    boxOf: (selector, { dx, dy } = {}) =>
      page.evaluateJson(`(() => {
        const el = document.querySelector(${JSON.stringify(selector)})
        if (!el) return 'null'
        const r = el.getBoundingClientRect()
        return JSON.stringify({
          x: Math.round(r.x + (${dx ?? 'null'} ?? r.width / 2)),
          y: Math.round(r.y + (${dy ?? 'null'} ?? r.height / 2)),
        })
      })()`),
    async clickSelector(selector, offset) {
      const box = await helpers.boxOf(selector, offset)

      if (!box) throw new Error(`点不到:${selector}`)

      await page.click(box.x, box.y)

      return box
    },
    waitForCards: (min = 1, timeout = 60000) =>
      waitFor(async () => (await helpers.groupCardCount()) >= min, { timeout }),
  }

  return Object.assign(page, helpers)
}
