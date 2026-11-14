import { useEffect, useState } from 'react'

export type Theme = 'dark' | 'light'

/** Misma clave que lee el script de index.html para aplicar el tema antes de pintar. */
const STORAGE_KEY = 'camguard-theme'

const THEME_COLORS: Record<Theme, string> = { dark: '#0c0d0f', light: '#f5f5f2' }

function readTheme(): Theme {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'light' ? 'light' : 'dark'
  } catch {
    return 'dark'
  }
}

/** Tema Oscuro/Claro por navegador; el oscuro es el predeterminado. */
export function useTheme() {
  const [theme, setTheme] = useState<Theme>(readTheme)

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    // Color de la barra del sistema en la app instalada (el mismo --bg de index.css).
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', THEME_COLORS[theme])
    try {
      localStorage.setItem(STORAGE_KEY, theme)
    } catch {
      // Sin almacenamiento (modo privado): el tema dura lo que la pestaña.
    }
  }, [theme])

  return [theme, setTheme] as const
}
