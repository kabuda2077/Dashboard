declare const __APP_VERSION__: string
declare const __COMMIT_ID__: string
// Build-time font selection: all | cdn | firasans | misans | pingfang | sarasa | none
declare const __FONT__: string

declare module 'vue-virtual-scroller'
declare interface Navigator {
  standalone?: boolean
}

type ToolTipParams = {
  data: {
    value: number
    name: number
  }
  seriesName: string
  color: string
}
