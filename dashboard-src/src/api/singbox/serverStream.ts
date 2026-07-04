// sing-box persistent server-streaming subscriptions over WebSocket.
//
// ConnectRPC's fetch transport keeps every server-streaming RPC on a long-lived
// HTTP/1.1 connection. Persistent dashboard streams can exhaust the browser's
// per-host connection pool and stall one-shot requests such as latency tests or
// proxy switching. The sing-box daemon also supports grpc-websockets, so the
// persistent subscriptions use that transport while unary calls keep using fetch.
import { getSingboxSecret, getSingboxUrlFromBackend } from '@/helper/utils'
import { activeBackend } from '@/store/setup'
import type { DescMessage, MessageInitShape, MessageShape } from '@bufbuild/protobuf'
import { GrpcWebSocketStream } from './websocket'

export interface ServerStreamMethod<Req extends DescMessage, Res extends DescMessage> {
  readonly name: string
  readonly parent: { readonly typeName: string }
  readonly input: Req
  readonly output: Res
}

export const serverStream = <Req extends DescMessage, Res extends DescMessage>(
  method: ServerStreamMethod<Req, Res>,
  request: MessageInitShape<Req>,
  signal: AbortSignal,
): AsyncIterable<MessageShape<Res>> => ({
  [Symbol.asyncIterator]() {
    const backend = activeBackend.value
    const baseUrl = backend ? getSingboxUrlFromBackend(backend) : ''

    if (!backend || !baseUrl) {
      throw new Error('sing-box backend unavailable')
    }

    const queue: MessageShape<Res>[] = []
    let finished = false
    let failure: string | null = null
    let notify: (() => void) | null = null
    const wake = () => {
      notify?.()
      notify = null
    }

    const stream = new GrpcWebSocketStream({
      baseUrl,
      secret: getSingboxSecret(backend),
      service: method.parent.typeName,
      method: method.name,
      requestSchema: method.input,
      responseSchema: method.output,
      onMessage: (msg) => {
        queue.push(msg)
        wake()
      },
      onEnd: (status, error) => {
        finished = true
        if (error) failure = error
        else if (status && status.code !== 0)
          failure = `grpc-status ${status.code}: ${status.message}`
        wake()
      },
    })
    stream.send(request)
    stream.finishSend()

    const onAbort = () => {
      finished = true
      stream.close()
      wake()
    }
    signal.addEventListener('abort', onAbort, { once: true })

    const finish = (): IteratorResult<MessageShape<Res>> => {
      signal.removeEventListener('abort', onAbort)
      stream.close()
      return { value: undefined, done: true }
    }

    return {
      async next(): Promise<IteratorResult<MessageShape<Res>>> {
        for (;;) {
          if (queue.length) return { value: queue.shift()!, done: false }
          if (finished) {
            const result = finish()
            if (failure && !signal.aborted) throw new Error(failure)
            return result
          }
          await new Promise<void>((resolve) => {
            notify = resolve
          })
        }
      },
      async return(): Promise<IteratorResult<MessageShape<Res>>> {
        finished = true
        return finish()
      },
    }
  },
})
