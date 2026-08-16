// 组装层 · Clash-compatible overview 门面。
import * as clash from './clash'

export const fetchMemoryAPI = <T>() => clash.fetchMemoryAPI<T>()

export const fetchTrafficAPI = <T>() => clash.fetchTrafficAPI<T>()
