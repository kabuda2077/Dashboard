import { CONNECTIONS_TABLE_ACCESSOR_KEY as KEY } from '@/constant'

// These keys remain recognizable for settings imported from the former native API.
// The current Clash API adapter never supplies values for them.
const retiredFields = new Set<string>([KEY.Protocol, KEY.OutboundType, KEY.FromOutbound])
export const isSupportedConnectionField = (key: string) => !retiredFields.has(key)
export const connectionFieldOptions = Object.values(KEY).filter(isSupportedConnectionField)

export const normalizeConnectionFields = <T extends string>(fields: T[]): T[] =>
  fields.filter(isSupportedConnectionField)

export const normalizeConnectionCardLines = (lines: KEY[][]): KEY[][] => {
  const cleaned = lines.map(normalizeConnectionFields)
  // Preserve deliberately empty editable rows, remove rows emptied by retirement.
  const retained = cleaned.filter((line, index) => line.length || !lines[index]?.length)
  return retained.length ? retained : [[KEY.Host]]
}
