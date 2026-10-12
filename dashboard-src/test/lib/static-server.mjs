// 伺服 dist 的静态服务器。zashboard 走 hash 路由,所以任何未知路径都回 index.html 即可。
import { createReadStream, existsSync, statSync } from 'node:fs'
import { createServer } from 'node:http'
import { extname, isAbsolute, join, normalize, relative, resolve } from 'node:path'

const MIME = {
  '.css': 'text/css',
  '.html': 'text/html; charset=utf-8',
  '.ico': 'image/x-icon',
  '.js': 'text/javascript',
  '.json': 'application/json',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.ttf': 'font/ttf',
  '.webmanifest': 'application/manifest+json',
  '.webp': 'image/webp',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
}

export const serveDirectory = (root, port = 0) => {
  root = resolve(root)
  const server = createServer((req, res) => {
    let requested
    try {
      requested = decodeURIComponent(new URL(req.url, 'http://localhost').pathname)
    } catch {
      res.writeHead(400)
      return res.end('Malformed path')
    }
    // 挡住 ../ 逃逸:normalize 之后必须还在 root 里
    const candidate = normalize(join(root, requested))
    const withinRoot = relative(root, candidate)
    const file =
      !withinRoot.startsWith('..') &&
      !isAbsolute(withinRoot) &&
      existsSync(candidate) &&
      statSync(candidate).isFile()
        ? candidate
        : join(root, 'index.html')

    res.writeHead(200, { 'content-type': MIME[extname(file)] ?? 'application/octet-stream' })
    createReadStream(file)
      .on('error', () => res.destroy())
      .pipe(res)
  })

  return new Promise((resolve, reject) => {
    server.once('error', reject)
    server.listen(port, '127.0.0.1', () => {
      const actualPort = server.address().port

      resolve({
        url: `http://127.0.0.1:${actualPort}`,
        port: actualPort,
        close: () =>
          new Promise((done) => {
            server.closeAllConnections?.()
            server.close(done)
          }),
      })
    })
  })
}
