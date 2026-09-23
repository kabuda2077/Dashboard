// No store imports: safe to use from storage wrappers and startup code.
export const DASHBOARD_SETTINGS_CHANGED = 'dashboard-settings-changed'
export const notifyDashboardSettingsChanged = (key: string | null) => {
  if (key === null || key.startsWith('config/')) {
    window.dispatchEvent(new Event(DASHBOARD_SETTINGS_CHANGED))
  }
}
