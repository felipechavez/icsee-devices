import { useEffect, useRef } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'

interface ModalProps {
  onClose: () => void
  children: ReactNode
  className?: string
  label: string
  onKeyDown?: (e: KeyboardEvent<HTMLDialogElement>) => void
}

/** Diálogo nativo: foco atrapado y Esc los maneja el navegador; un clic fuera del contenido cierra. */
export function Modal({ onClose, children, className = 'player', label, onKeyDown }: ModalProps) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    dialog?.showModal()
    return () => dialog?.close()
  }, [])

  return (
    <dialog
      ref={ref}
      className={className}
      aria-label={label}
      onClose={onClose}
      onKeyDown={onKeyDown}
      onClick={(e) => e.target === ref.current && onClose()}
    >
      {children}
    </dialog>
  )
}
