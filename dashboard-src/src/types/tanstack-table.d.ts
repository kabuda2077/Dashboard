import type { RowData } from '@tanstack/vue-table'

declare module '@tanstack/vue-table' {
  // Declaration merging requires the original generic parameter names.
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  interface ColumnMeta<TData extends RowData, TValue> {
    cellClass?: string
    headerClass?: string
    noCellTitle?: boolean
  }
}
