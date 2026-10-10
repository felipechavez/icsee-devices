import type { Camera } from '../api/types'
import { cameraStatus } from '../lib/cameraStatus'

/** Estado de la cámara; se actualiza solo vía SignalR (ver EventsHubProvider). */
export function StatusPill({ camera, overlay = false }: { camera: Camera; overlay?: boolean }) {
  const status = cameraStatus(camera)
  return (
    <span className={overlay ? 'pill pill-overlay' : 'pill'} data-status={status.key}>
      <span className="dot" aria-hidden />
      {status.label}
    </span>
  )
}
