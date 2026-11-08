import type { Connection } from '@/types'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ref } from 'vue'

vi.mock('@/store/setup', () => ({ activeBackend: ref({ uuid: 'A' }) }))
vi.mock('@/helper', () => ({
  getConnectionChains: () => ['DIRECT'],
  getConnectionDownload: (connection: Connection) => connection.download,
  getConnectionUpload: (connection: Connection) => connection.upload,
  getConnectionHostname: () => 'example.test',
  getConnectionSourceIP: (connection: Connection) => connection.metadata?.sourceIP || '192.0.2.1',
  getProcessFromConnection: () => 'example.exe',
}))
vi.mock('@/store/settings', () => ({ customBackgroundURL: ref('') }))

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  vi.spyOn(console, 'error').mockImplementation(() => {})
  vi.spyOn(document, 'hidden', 'get').mockReturnValue(false)
  vi.spyOn(document, 'addEventListener')
  vi.spyOn(window, 'addEventListener')
})
afterEach(() => {
  for (const [type, listener] of vi.mocked(document.addEventListener).mock.calls) {
    if (type === 'visibilitychange') document.removeEventListener(type, listener)
  }
  for (const [type, listener] of vi.mocked(window.addEventListener).mock.calls) {
    if (type === 'pagehide') window.removeEventListener(type, listener)
  }
  vi.clearAllTimers()
  vi.useRealTimers()
  vi.restoreAllMocks()
})

const batch = (count: number) =>
  Array.from(
    { length: count },
    (_, id) => ({ id: String(id), download: 1, upload: 1 }) as Connection,
  )

const fixture = async (options: {
  initialize?: boolean
  failRead?: boolean
  holdRead?: boolean
  initialCount?: number
} = {}) => {
  const disk = new Map<string, string>()
  if (options.initialCount) {
    disk.set('connection-history:A-sourceIP', JSON.stringify([
      { key: '192.0.2.1', download: options.initialCount, upload: options.initialCount, count: options.initialCount },
    ]))
  }
  let failRead = options.failRead ?? false
  let failClear = false
  let holdRead = options.holdRead ?? false
  let releaseRead: (() => void) | undefined
  let failA = false
  let holdA = false
  let release: (() => void) | undefined
  vi.spyOn(indexedDB, 'open').mockImplementation((name) => {
    const database = {
      objectStoreNames: { contains: () => true },
      transaction: () => {
        const transaction = {
          error: null,
          oncomplete: null,
          onabort: null,
          onerror: null,
          objectStore: () => ({
            openCursor: () => {
              const entries = [...disk].filter(([key]) => key.startsWith(`${name}:`))
              let index = 0
              const cursor = {
                result: null as unknown,
                error: new Error('initial read failed'),
                onsuccess: null as ((event: Event) => void) | null,
                onerror: null as (() => void) | null,
              }
              const next = () => {
                if (name === 'connection-history' && failRead) {
                  cursor.onerror?.()
                  return
                }
                const entry = entries[index++]
                cursor.result = entry ? {
                  key: entry[0].slice(`${name}:`.length),
                  value: { value: entry[1] },
                  continue: () => queueMicrotask(next),
                } : null
                cursor.onsuccess?.({ target: cursor } as unknown as Event)
                if (!entry) queueMicrotask(() => transaction.oncomplete?.call(transaction, new Event('complete')))
              }
              if (name === 'connection-history' && holdRead) {
                holdRead = false
                releaseRead = next
              } else queueMicrotask(next)
              return cursor
            },
            put: ({ key, value }: { key: string; value: string }) => {
              const result = request(key)
              const finish = () => {
                if (name === 'connection-history' && key.startsWith('A-') && failA) {
                  transaction.onabort?.call(transaction, new Event('abort'))
                } else {
                  disk.set(`${name}:${key}`, value)
                  transaction.oncomplete?.call(transaction, new Event('complete'))
                }
              }
              if (key.startsWith('A-') && holdA) {
                holdA = false
                release = finish
              } else queueMicrotask(finish)
              return result
            },
            clear: () => {
              queueMicrotask(() => {
                if (failClear) {
                  transaction.onabort?.call(transaction, new Event('abort'))
                  return
                }
                for (const key of disk.keys()) if (key.startsWith(`${name}:`)) disk.delete(key)
                transaction.oncomplete?.call(transaction, new Event('complete'))
              })
              return request(undefined)
            },
          }),
        } as unknown as IDBTransaction
        return transaction
      },
    }
    return request(database) as unknown as IDBOpenDBRequest
  })
  function request(result: unknown) {
    const value = { result, onsuccess: null as ((event: Event) => void) | null, onerror: null }
    queueMicrotask(() => value.onsuccess?.({ target: value } as unknown as Event))
    return value
  }
  const history = await import('@/store/conn-history')
  const { activeBackend } = await import('@/store/setup')
  const { flushLocalPersistence } = await import('@/helper/persistenceBarrier')
  const { ConnectionHistoryType } = await import('@/helper/indexeddb')
  const count = () => JSON.parse(disk.get('connection-history:A-sourceIP') ?? '[]')[0]?.count
  const currentCount = () =>
    history.aggregatedDataMap.value[ConnectionHistoryType.SourceIP][0]?.count
  const switchTo = async (uuid: string) => {
    activeBackend.value = { uuid } as typeof activeBackend.value
    await history.initAggregatedDataMap()
  }
  if (options.initialize !== false) {
    await switchTo('A')
    history.saveConnectionHistory(batch(100))
    await flushLocalPersistence()
    expect(count()).toBe(100)
  }
  return {
    history,
    ConnectionHistoryType,
    flushLocalPersistence,
    failRead: (value: boolean) => { failRead = value },
    failClear: (value: boolean) => { failClear = value },
    isReadHeld: () => !!releaseRead,
    releaseRead: () => {
      expect(releaseRead).toBeDefined()
      const finish = releaseRead!
      releaseRead = undefined
      finish()
    },
    count,
    currentCount,
    switchTo,
    fail: (value: boolean) => {
      failA = value
    },
    hold: () => {
      holdA = true
    },
    release: () => {
      expect(release).toBeDefined()
      const finish = release!
      release = undefined
      finish()
    },
    isHeld: () => !!release,
  }
}

it('retains 100 + 20 across a failed old-session save and returns to save 125 truthfully', async () => {
  const context = await fixture()
  context.history.saveConnectionHistory(batch(20))
  context.fail(true)
  await context.switchTo('B')
  expect(context.count()).toBe(100)
  await expect(context.flushLocalPersistence()).rejects.toThrow(/has not been saved/)
  await context.switchTo('A')
  expect(context.currentCount()).toBe(120)
  context.history.saveConnectionHistory(batch(5))
  context.fail(false)
  await context.flushLocalPersistence()
  expect(context.count()).toBe(125)
  await context.switchTo('B')
  await context.switchTo('A')
  expect(context.currentCount()).toBe(125)
  await expect(context.flushLocalPersistence()).resolves.toBeUndefined()
})

it.each([false, true])(
  'an in-flight retry of 120 cannot overwrite the newer 125 snapshot (old commit fails: %s)',
  async (oldFails) => {
    const context = await fixture()
    context.history.saveConnectionHistory(batch(20))
    context.fail(true)
    await context.switchTo('B')
    await context.switchTo('A')
    context.fail(false)
    context.hold()
    const retry = context.flushLocalPersistence()
    await vi.waitFor(() => expect(context.isHeld()).toBe(true))
    context.history.saveConnectionHistory(batch(5))
    const newer = context.flushLocalPersistence()
    context.fail(oldFails)
    context.release()
    context.fail(false)
    await Promise.all([retry, newer])
    expect(context.count()).toBe(125)
    await expect(context.flushLocalPersistence()).resolves.toBeUndefined()
    expect(context.count()).toBe(125)
  },
)

it('retries a failed departed session while another session stays active', async () => {
  const context = await fixture()
  context.history.saveConnectionHistory(batch(20))
  context.fail(true)
  await context.switchTo('B')
  await expect(context.flushLocalPersistence()).rejects.toThrow(/has not been saved/)
  context.fail(false)
  await context.flushLocalPersistence()
  expect(context.count()).toBe(120)
  await context.switchTo('A')
  expect(context.currentCount()).toBe(120)
})

it('an explicit clear cannot resurrect a retained failed snapshot on a later flush', async () => {
  const context = await fixture()
  context.history.saveConnectionHistory(batch(20))
  context.fail(true)
  await context.switchTo('B')
  await context.history.clearConnectionHistory()
  context.fail(false)
  await context.flushLocalPersistence()
  expect(context.count()).toBeUndefined()
  await context.switchTo('A')
  expect(context.currentCount()).toBeUndefined()
})

it('blocks closing after an initialization read failure and replays pending aggregates exactly once on recovery', async () => {
  const context = await fixture({ initialize: false, failRead: true, initialCount: 100 })
  // The stream caller does not await initialization; it must not reject unhandled.
  await expect(context.history.initAggregatedDataMap()).resolves.toBeUndefined()
  context.history.saveConnectionHistory(batch(100))
  await expect(context.flushLocalPersistence()).rejects.toThrow(/has not been saved/)
  expect(context.count()).toBe(100)
  context.history.saveConnectionHistory(batch(20))
  context.failRead(false)
  await context.flushLocalPersistence()
  expect(context.count()).toBe(220)
  expect(context.currentCount()).toBe(220)
  await context.switchTo('B')
  await context.switchTo('A')
  await context.flushLocalPersistence()
  expect(context.currentCount()).toBe(220)
  expect(context.count()).toBe(220)
})

it('the closing barrier waits for an in-flight initialization and saves its queued connections', async () => {
  const context = await fixture({ initialize: false, holdRead: true })
  const initialization = context.history.initAggregatedDataMap()
  context.history.saveConnectionHistory(batch(100))
  let flushed = false
  const closing = context.flushLocalPersistence().then(() => { flushed = true })
  await vi.waitFor(() => expect(context.isReadHeld()).toBe(true))
  expect(flushed).toBe(false)
  expect(context.count()).toBeUndefined()
  context.releaseRead()
  await Promise.all([initialization, closing])
  expect(context.count()).toBe(100)
})

it('retains failed initialization data across repeat initialization and session switches', async () => {
  const context = await fixture({ initialize: false, failRead: true, initialCount: 100 })
  await context.switchTo('A')
  context.history.saveConnectionHistory(batch(100))
  await context.switchTo('A')
  context.history.saveConnectionHistory(batch(20))
  await context.switchTo('B')
  context.history.saveConnectionHistory(batch(5))
  await expect(context.flushLocalPersistence()).rejects.toThrow(/has not been saved/)
  context.failRead(false)
  await context.flushLocalPersistence()
  expect(context.count()).toBe(220)
  expect(context.currentCount()).toBe(5)
  await context.switchTo('A')
  expect(context.currentCount()).toBe(220)
})

it('returning during a stale initialization save does not replay data already owned by its snapshot', async () => {
  const context = await fixture({ initialize: false, failRead: true, initialCount: 100 })
  await context.switchTo('A')
  context.history.saveConnectionHistory(batch(100))
  await context.switchTo('B')
  context.failRead(false)
  context.hold()
  const retry = context.flushLocalPersistence()
  await vi.waitFor(() => expect(context.isHeld()).toBe(true))
  const returning = context.switchTo('A')
  context.history.saveConnectionHistory(batch(5))
  context.release()
  await Promise.all([retry, returning])
  await context.flushLocalPersistence()
  expect(context.count()).toBe(205)
  expect(context.currentCount()).toBe(205)
})

it('clear discards a stale initialization queue while saving records received during clear', async () => {
  const context = await fixture({ initialize: false, holdRead: true, initialCount: 100 })
  const initialization = context.history.initAggregatedDataMap()
  context.history.saveConnectionHistory(batch(100))
  await vi.waitFor(() => expect(context.isReadHeld()).toBe(true))
  const clearing = context.history.clearConnectionHistory()
  context.history.saveConnectionHistory(batch(5))
  context.releaseRead()
  await Promise.all([initialization, clearing])
  await context.flushLocalPersistence()
  expect(context.count()).toBe(5)
  await context.switchTo('B')
  await context.switchTo('A')
  expect(context.currentCount()).toBe(5)
})

it('a failed clear blocks later initialization until retry and keeps only records received after clear', async () => {
  const context = await fixture({ initialize: false, initialCount: 100 })
  await context.switchTo('A')
  context.failClear(true)
  await expect(context.history.clearConnectionHistory()).rejects.toThrow('could not be cleared')
  context.history.saveConnectionHistory(batch(5))
  await context.switchTo('B')
  context.history.saveConnectionHistory(batch(7))
  await expect(context.flushLocalPersistence()).rejects.toThrow(/has not been saved/)
  expect(context.count()).toBe(100)
  context.failClear(false)
  await context.flushLocalPersistence()
  expect(context.count()).toBe(5)
  expect(context.currentCount()).toBe(7)
  await context.switchTo('A')
  expect(context.currentCount()).toBe(5)
})

it('bounds each initialized hidden batch and treats a discarded key as new when it reappears', async () => {
  const context = await fixture({ initialize: false })
  await context.switchTo('A')
  const hidden = vi.spyOn(document, 'hidden', 'get').mockReturnValue(true)
  document.dispatchEvent(new Event('visibilitychange'))
  const connections = batch(2001).map((connection, index) => ({
    ...connection,
    download: index + 1,
    metadata: { sourceIP: `source-${index}` },
  } as Connection))
  context.history.saveConnectionHistory(connections.slice(0, 2000))
  context.history.saveConnectionHistory(connections.slice(2000))
  context.history.saveConnectionHistory([{
    ...connections[0], download: 10_000, upload: 7,
  } as Connection])
  expect(context.history.aggregatedDataMap.value[context.ConnectionHistoryType.SourceIP]).toEqual([])
  expect(context.count()).toBeUndefined()

  hidden.mockReturnValue(false)
  document.dispatchEvent(new Event('visibilitychange'))
  const records = context.history.aggregatedDataMap.value[context.ConnectionHistoryType.SourceIP]
  expect(records).toHaveLength(1501)
  expect(records.find((item) => item.key === 'source-0')).toEqual({
    key: 'source-0', download: 10_000, upload: 7, count: 1,
  })
  expect(records.some((item) => item.key === 'source-500')).toBe(false)
  expect(records.find((item) => item.key === 'source-501')?.download).toBe(502)
  expect(records.find((item) => item.key === 'source-2000')?.download).toBe(2001)
  expect(context.history.aggregatedDataMap.value[context.ConnectionHistoryType.Process][0].count).toBe(2002)
})

it('saves hidden dirty history every 30 seconds, retries a failed save, and refreshes immediately on return', async () => {
  const context = await fixture({ initialize: false })
  await context.switchTo('A')
  const hidden = vi.spyOn(document, 'hidden', 'get').mockReturnValue(true)
  document.dispatchEvent(new Event('visibilitychange'))
  context.history.saveConnectionHistory(batch(20))
  context.fail(true)
  await vi.advanceTimersByTimeAsync(29_999)
  expect(context.count()).toBeUndefined()
  await vi.advanceTimersByTimeAsync(1)
  expect(console.error).toHaveBeenCalledWith(
    expect.stringContaining('Failed to save connection history'), expect.anything(),
  )
  expect(context.count()).toBeUndefined()
  context.fail(false)
  await vi.advanceTimersByTimeAsync(30_000)
  expect(context.count()).toBe(20)
  expect(context.currentCount()).toBeUndefined()

  hidden.mockReturnValue(false)
  document.dispatchEvent(new Event('visibilitychange'))
  expect(context.currentCount()).toBe(20)
  await context.switchTo('B')
  await context.switchTo('A')
  expect(context.currentCount()).toBe(20)
})

it('uses the existing aggregation limit for records queued during failed initialization', async () => {
  const context = await fixture({ initialize: false, failRead: true })
  await context.history.initAggregatedDataMap()
  context.history.saveConnectionHistory(batch(2100).map((connection, index) => ({
    ...connection,
    metadata: { sourceIP: `source-${index}` },
  } as Connection)))
  context.failRead(false)
  await context.flushLocalPersistence()
  const records = context.history.aggregatedDataMap.value[context.ConnectionHistoryType.SourceIP]
  expect(records).toHaveLength(1500)
  expect(records.reduce((sum, item) => sum + item.count, 0)).toBe(1500)
  // Other dimensions still aggregate every connection under their shared key.
  expect(context.history.aggregatedDataMap.value[context.ConnectionHistoryType.Process][0].count).toBe(2100)
})
