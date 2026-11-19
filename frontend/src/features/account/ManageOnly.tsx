import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { useMe } from './useMe'

/** Página que no ve un usuario de solo lectura: lo lleva al inicio. */
export function ManageOnly({ children }: { children: ReactNode }) {
  const me = useMe()
  if (!me.data) return me.error ? <p className="error">{me.error.message}</p> : null
  return me.data.isReadOnly ? <Navigate to="/" replace /> : children
}
