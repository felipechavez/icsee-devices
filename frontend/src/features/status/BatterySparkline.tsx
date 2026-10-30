import type { BatteryPoint } from '../../api/types'
import { batteryLevel } from '../../lib/cameraStatus'

const DAYS = 14
const W = 280
const H = 56

/** Batería de los últimos 14 días, en SVG propio; la línea punteada es el umbral de aviso. */
export function BatterySparkline({ points, threshold, now }: { points: BatteryPoint[]; threshold?: number; now: number }) {
  if (points.length < 2) return <div className="spark-empty">Sin historial aún</div>

  const from = now - DAYS * 86_400_000
  const x = (iso: string) => ((new Date(iso).getTime() - from) / (now - from)) * W
  const y = (percent: number) => H - (percent / 100) * (H - 4) - 2
  const path = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.at).toFixed(1)},${y(p.percent).toFixed(1)}`).join(' ')
  const last = points[points.length - 1]

  return (
    <svg
      className="spark"
      viewBox={`0 0 ${W} ${H}`}
      preserveAspectRatio="none"
      data-battery={batteryLevel(last.percent)}
      role="img"
      aria-label={`Batería en los últimos ${DAYS} días: de ${points[0].percent} % a ${last.percent} %`}
    >
      {threshold != null && <line className="spark-threshold" x1={0} x2={W} y1={y(threshold)} y2={y(threshold)} />}
      <path d={path} vectorEffect="non-scaling-stroke" />
    </svg>
  )
}
