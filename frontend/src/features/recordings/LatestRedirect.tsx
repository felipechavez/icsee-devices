import { Navigate } from 'react-router-dom'
import { localDayKey } from '../../lib/format'
import { useLatestRecordings } from './useRecordings'

/**
 * /latest (atajo "Último clip" de la app instalada): abre el clip más reciente en Grabaciones, en su día.
 * Grabaciones abre ?clip= aunque no esté en la lista cargada (como los enlaces de ntfy).
 */
export function LatestRedirect() {
  const latest = useLatestRecordings(1)

  if (latest.isPending) return <p className="muted">Buscando el último clip…</p>
  const clip = latest.data?.[0]
  if (!clip) return <Navigate to="/recordings" replace />

  const params = new URLSearchParams({ day: localDayKey(clip.startedAt), clip: clip.id })
  return <Navigate to={`/recordings?${params}`} replace />
}
