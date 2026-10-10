import { useCallback, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { ToastContext } from './toastContext'

/** Aviso breve abajo al centro ("Cambios guardados"). */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<{ message: string; id: number } | null>(null)
  const show = useCallback((message: string) => setToast({ message, id: Date.now() }), [])

  useEffect(() => {
    if (!toast) return
    const timer = setTimeout(() => setToast(null), 2200)
    return () => clearTimeout(timer)
  }, [toast])

  return (
    <ToastContext.Provider value={show}>
      {children}
      {toast && (
        <div key={toast.id} className="toast" role="status">
          {toast.message}
        </div>
      )}
    </ToastContext.Provider>
  )
}
