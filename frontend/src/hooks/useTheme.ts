import { useEffect, useState } from 'react'

export type Theme = 'dark' | 'light'

/** Misma clave que lee el script de index.html para aplicar el tema antes de pintar. */
const STORAGE_KEY = 'camguard-theme'

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
    try {
      localStorage.setItem(STORAGE_KEY, theme)
    } catch {
      // Sin almacenamiento (modo privado): el tema dura lo que la pestaña.
    }
  }, [theme])

  return [theme, setTheme] as const
}
