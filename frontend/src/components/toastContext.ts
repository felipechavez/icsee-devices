import { createContext, useContext } from 'react'

export const ToastContext = createContext<(message: string) => void>(() => {})

/** Muestra un aviso breve abajo al centro (ver ToastProvider). */
export const useToast = () => useContext(ToastContext)
