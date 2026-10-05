import { CONNECTIONS_TABLE_ACCESSOR_KEY as KEY } from '@/constant'

const supported = new Set<string>(Object.values(KEY))
export const isSupportedConnectionField = (key: string) => supported.has(key)
export const connectionFieldOptions = Object.values(KEY)
export const normalizeConnectionFields = <T extends string>(fields: T[]): T[] =>
  Array.isArray(fields) ? fields.filter(isSupportedConnectionField) : []
export const normalizeConnectionCardLines = (lines: KEY[][]): KEY[][] => {
  if (!Array.isArray(lines)) return [[KEY.Host]]
  const cleaned = lines.map(normalizeConnectionFields)
  const retained = cleaned.filter(
    (line, index) => line.length || (Array.isArray(lines[index]) && lines[index].length === 0),
  )
  return retained.length ? retained : [[KEY.Host]]
}
