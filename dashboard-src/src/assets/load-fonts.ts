export const loadFonts = () => {
  const fontMode =
    __FONT__ ||
    ({
      'cdn-fonts': 'cdn',
      MiSans: 'misans',
      SarasaUi: 'sarasa',
      PingFang: 'pingfang',
      FiraSans: 'firasans',
      SystemUI: 'none',
    }[import.meta.env.MODE] ??
      'all')

  if (fontMode === 'cdn') {
    const createLink = (href: string) => {
      const link = document.createElement('link')
      link.rel = 'stylesheet'
      link.href = href
      link.media = 'print'
      link.onload = () => {
        link.media = 'all'
      }
      document.head.appendChild(link)
    }

    createLink('https://unpkg.com/subsetted-fonts@latest/MiSans-VF/MiSans-VF.css')
    createLink('https://unpkg.com/subsetted-fonts@latest/SarasaUiSC-Regular/SarasaUiSC-Regular.css')
    createLink('https://unpkg.com/subsetted-fonts@latest/PingFangSC-Regular/PingFangSC-Regular.css')
    createLink('https://unpkg.com/@fontsource/fira-sans')
  } else if (fontMode === 'misans') {
    import('subsetted-fonts/MiSans-VF/MiSans-VF.css')
  } else if (fontMode === 'sarasa') {
    import('subsetted-fonts/SarasaUiSC-Regular/SarasaUiSC-Regular.css')
  } else if (fontMode === 'pingfang') {
    import('subsetted-fonts/PingFangSC-Regular/PingFangSC-Regular.css')
  } else if (fontMode === 'firasans') {
    import('@fontsource/fira-sans/index.css')
  } else if (fontMode === 'none') {
    // System UI fonts only; nothing bundled.
  } else {
    // 'all' (default): bundle every font.
    import('@fontsource/fira-sans/index.css')
    import('subsetted-fonts/MiSans-VF/MiSans-VF.css')
    import('subsetted-fonts/SarasaUiSC-Regular/SarasaUiSC-Regular.css')
    import('subsetted-fonts/PingFangSC-Regular/PingFangSC-Regular.css')
  }
}
