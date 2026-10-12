/*
 * 够用就好的 CDP 客户端:连上 headless Chrome 的调试端口,开标签页、跑表达式、发鼠标事件。
 * Node 22+ 自带 WebSocket,所以这里不引入任何第三方依赖 —— 性能脚本不该给仓库添依赖。
 */

const openSocket = (url) =>
  new Promise((resolve, reject) => {
    const socket = new WebSocket(url)

    socket.addEventListener('open', () => resolve(socket))
    socket.addEventListener('error', reject)
  })

export const connectBrowser = async (debuggingPort) => {
  const { webSocketDebuggerUrl } = await fetch(
    `http://127.0.0.1:${debuggingPort}/json/version`,
  ).then((res) => res.json())
  const socket = await openSocket(webSocketDebuggerUrl)

  let lastId = 0
  const pending = new Map()

  socket.addEventListener('message', (event) => {
    const message = JSON.parse(event.data)
    const handlers = pending.get(message.id)

    if (!handlers) return

    pending.delete(message.id)
    clearTimeout(handlers.timer)
    if (message.error) {
      handlers.reject(new Error(`${message.error.message} (${JSON.stringify(message.error)})`))
    } else {
      handlers.resolve(message.result)
    }
  })

  socket.addEventListener('close', () => {
    for (const { reject, timer } of pending.values()) {
      clearTimeout(timer)
      reject(new Error('Chrome debugging connection closed'))
    }
    pending.clear()
  })

  const send = (method, params = {}, sessionId) => {
    const id = ++lastId

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        pending.delete(id)
        reject(new Error(`Chrome debugging call timed out: ${method}`))
      }, 60000)
      pending.set(id, { resolve, reject, timer })
      socket.send(JSON.stringify({ id, method, params, sessionId }))
    })
  }

  const newPage = async ({ width = 1440, height = 900, cpuThrottling = 1 } = {}) => {
    const { browserContextId } = await send('Target.createBrowserContext')
    const { targetId } = await send('Target.createTarget', { url: 'about:blank', browserContextId })
    const { sessionId } = await send('Target.attachToTarget', { targetId, flatten: true })
    const call = (method, params) => send(method, params, sessionId)

    await call('Page.enable')
    await call('Runtime.enable')
    await call('Network.enable')
    // PWA 的 service worker 会拿旧构建的资源应付请求,拿到的就不是刚 build 出来的那份了
    await call('Network.setBypassServiceWorker', { bypass: true })
    await call('Emulation.setDeviceMetricsOverride', {
      width,
      height,
      deviceScaleFactor: 1,
      mobile: false,
    })

    if (cpuThrottling > 1) {
      await call('Emulation.setCPUThrottlingRate', { rate: cpuThrottling })
    }

    const evaluate = async (expression) => {
      const { result, exceptionDetails } = await call('Runtime.evaluate', {
        expression,
        returnByValue: true,
        awaitPromise: true,
      })

      if (exceptionDetails) {
        throw new Error(exceptionDetails.exception?.description ?? exceptionDetails.text)
      }

      return result?.value
    }

    return {
      call,
      evaluate,
      // 页面里返回 JSON 字符串再解析,免得 returnByValue 把 Map / DOM 之类原样吐出来
      evaluateJson: async (expression) => JSON.parse(await evaluate(expression)),
      goto: (url) => call('Page.navigate', { url }),
      reload: () => call('Page.reload', {}),
      viewport: { width, height },
      async click(x, y, button = 'left') {
        await call('Input.dispatchMouseEvent', {
          type: 'mousePressed',
          x,
          y,
          button,
          clickCount: 1,
        })
        await call('Input.dispatchMouseEvent', {
          type: 'mouseReleased',
          x,
          y,
          button,
          clickCount: 1,
        })
      },
      async screenshot(path) {
        const { data } = await call('Page.captureScreenshot', { format: 'png' })
        const { writeFile } = await import('node:fs/promises')

        await writeFile(path, Buffer.from(data, 'base64'))
      },
      close: () => send('Target.disposeBrowserContext', { browserContextId }),
    }
  }

  return {
    newPage,
    close: () => socket.close(),
  }
}

export const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

// 轮询到条件成立为止,返回耗时;超时返回 null(调用方自己决定这算不算失败)
export const waitFor = async (probe, { timeout = 30000, interval = 100 } = {}) => {
  const startedAt = Date.now()

  while (Date.now() - startedAt < timeout) {
    if (await probe()) {
      return Date.now() - startedAt
    }

    await sleep(interval)
  }

  return null
}
