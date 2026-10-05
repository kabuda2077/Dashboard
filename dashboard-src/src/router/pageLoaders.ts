// The module loader already caches evaluated pages. No speculative page scheduler.
export const loadProxiesPage = () => import('@/views/ProxiesPage.vue')
export const loadConnectionsPage = () => import('@/views/ConnectionsPage.vue')
export const loadOverviewPage = () => import('@/views/OverviewPage.vue')
