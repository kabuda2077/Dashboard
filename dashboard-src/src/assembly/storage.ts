// 组装层 · Clash-compatible /storage/zashboard 设置同步端点。
import {
  deleteStorageAPI as deleteClashStorageAPI,
  getStorageAPI as getClashStorageAPI,
  setStorageAPI as setClashStorageAPI,
} from '@/api/clash'
export const getStorageAPI = () => getClashStorageAPI()

export const setStorageAPI = (value: Record<string, string>) =>
  setClashStorageAPI(value)

export const deleteStorageAPI = () => deleteClashStorageAPI()
