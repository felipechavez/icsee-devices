import { batteryLevel } from '../lib/cameraStatus'

/** Último porcentaje leído; no se muestra si la cámara nunca lo informó. */
export function BatteryBadge({ percent, overlay = false }: { percent: number | null; overlay?: boolean }) {
  if (percent == null) return null
  return (
    <span
      className={overlay ? 'battery battery-overlay' : 'battery'}
      data-battery={batteryLevel(percent)}
      title="Batería (última lectura)"
    >
      {percent}%
    </span>
  )
}
