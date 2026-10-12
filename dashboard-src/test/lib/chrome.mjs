// Adapted from zashboard Release v3.29.1 (50717b02dac68425cdc5b80cb754bea768ee4fc9).
// Each run owns a temporary profile and only terminates its own browser process tree.
import { spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import { mkdir, mkdtemp, rm } from 'node:fs/promises'
import { delimiter, join, resolve } from 'node:path'
import { sleep } from './cdp.mjs'

const resolveCommand = (candidate) => {
  if (candidate.includes('/') || candidate.includes('\\')) {
    return existsSync(candidate) ? candidate : undefined
  }
  for (const directory of (process.env.PATH ?? '').split(delimiter)) {
    const path = join(directory, candidate)
    if (existsSync(path)) return path
  }
}

const resolveChrome = () => {
  const candidates = process.env.CHROME_BIN
    ? [process.env.CHROME_BIN]
    : process.platform === 'win32'
      ? [
          join(
            process.env.PROGRAMFILES ?? 'C:/Program Files',
            'Google/Chrome/Application/chrome.exe',
          ),
          join(
            process.env['PROGRAMFILES(X86)'] ?? 'C:/Program Files (x86)',
            'Google/Chrome/Application/chrome.exe',
          ),
          ...(process.env.LOCALAPPDATA
            ? [join(process.env.LOCALAPPDATA, 'Google/Chrome/Application/chrome.exe')]
            : []),
          'chrome.exe',
        ]
      : [
          'google-chrome-stable',
          'google-chrome',
          'chromium',
          'chromium-browser',
          '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
        ]
  for (const candidate of candidates) {
    const binary = resolveCommand(candidate)
    if (binary) return binary
  }
  throw new Error(
    `Chrome not found. Set CHROME_BIN to a Chrome/Chromium executable. Tried:\n  ${candidates.join('\n  ')}`,
  )
}

export const launchChrome = async ({ port = 9222, headless = true, runDirectory } = {}) => {
  const binary = resolveChrome()
  const testsRoot = resolve(import.meta.dirname, '..', '..', '..', '.tmp', 'tests')
  await mkdir(testsRoot, { recursive: true })
  const ownedRun = runDirectory ?? (await mkdtemp(join(testsRoot, 'proxy-chrome-')))
  const userDataDir = await mkdtemp(join(ownedRun, 'chrome-'))
  const child = spawn(
    binary,
    [
      ...(headless ? ['--headless=new'] : []),
      '--remote-debugging-address=127.0.0.1',
      `--remote-debugging-port=${port}`,
      `--user-data-dir=${userDataDir}`,
      '--no-first-run',
      '--no-default-browser-check',
      '--disable-gpu',
      '--disable-extensions',
      '--disable-background-networking',
      '--disable-component-update',
      '--no-proxy-server',
      '--disable-background-timer-throttling',
      '--disable-renderer-backgrounding',
      'about:blank',
    ],
    { stdio: 'ignore', windowsHide: headless },
  )
  let launchError
  const exited = new Promise((resolve) => {
    child.once('exit', resolve)
    child.once('error', (error) => {
      launchError = error
      resolve()
    })
  })
  let closing
  const close = () =>
    (closing ??= (async () => {
      if (child.pid && child.exitCode === null && child.signalCode === null) {
        if (process.platform === 'win32') {
          await new Promise((resolve, reject) => {
            const killer = spawn('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], {
              stdio: 'ignore',
              windowsHide: true,
            })
            killer.once('error', reject)
            killer.once('exit', resolve)
          })
        } else {
          child.kill('SIGTERM')
        }
        await Promise.race([exited, sleep(3000)])
        if (child.exitCode === null && child.signalCode === null) {
          child.kill('SIGKILL')
          await Promise.race([exited, sleep(3000)])
        }
      }
      await rm(userDataDir, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 })
      if (!runDirectory) await rm(ownedRun, { recursive: true, force: true })
    })())

  try {
    for (let i = 0; i < 100; i++) {
      if (launchError) throw launchError
      if (child.exitCode !== null || child.signalCode !== null) {
        throw new Error(
          `Chrome exited before its debugging endpoint was ready (${child.exitCode ?? child.signalCode})`,
        )
      }
      try {
        const response = await fetch(`http://127.0.0.1:${port}/json/version`, {
          signal: AbortSignal.timeout(1000),
        })
        if (response.ok && (await response.json()).webSocketDebuggerUrl) {
          console.error(
            `Chrome: ${binary} (${headless ? 'headless' : 'headful'}), isolated profile: ${userDataDir}`,
          )
          return { close }
        }
      } catch {
        /* The endpoint is not ready yet. */
      }
      await sleep(100)
    }
    throw new Error(`Chrome debugging endpoint did not respond on port ${port}`)
  } catch (error) {
    await close()
    throw error
  }
}
