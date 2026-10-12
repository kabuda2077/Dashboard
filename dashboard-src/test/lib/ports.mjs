// 端口一律临时分配:上一次跑崩了留下的进程不该让下一次跑不起来。
import { createServer } from 'node:net'

export const findFreePort = () =>
  new Promise((resolve, reject) => {
    const probe = createServer()

    probe.on('error', reject)
    probe.listen(0, '127.0.0.1', () => {
      const { port } = probe.address()

      probe.close(() => resolve(port))
    })
  })
