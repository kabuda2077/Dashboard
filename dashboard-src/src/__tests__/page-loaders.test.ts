import { loadConnectionsPage, loadOverviewPage, loadProxiesPage } from '@/router/pageLoaders'
import { expect, it } from 'vitest'

it.each([loadConnectionsPage, loadOverviewPage, loadProxiesPage])(
  'loads real route components on demand using the module cache',
  async (load) => {
    const [first, second] = await Promise.all([load(), load()])
    expect(first).toBe(second)
    expect(first.default).toBeDefined()
  },
)
