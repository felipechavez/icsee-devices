import { useState } from 'react'
import { Link } from 'react-router-dom'
import type { Camera } from '../../api/types'
import { BatteryBadge } from '../../components/BatteryBadge'
import { StatusPill } from '../../components/StatusPill'
import { useCanManage } from '../account/useMe'
import { useCameras } from '../cameras/useCameras'
import { LivePlayer } from './LivePlayer'

function summary(cameras: Camera[]) {
  const awake = cameras.filter((c) => c.enabled && c.isAwake).length
  return [
    cameras.length === 1 ? '1 cámara' : `${cameras.length} cámaras`,
    awake === 1 ? '1 despierta' : `${awake} despiertas`,
  ].join(' · ')
}

function LiveTile({ camera, generation }: { camera: Camera; generation: number }) {
  return (
    <div className="mosaic-tile" data-paused={!camera.enabled || undefined}>
      <div className="mosaic-head">
        <strong>{camera.name}</strong>
        <StatusPill camera={camera} />
        <BatteryBadge percent={camera.batteryPercent} />
        <Link className="btn btn-link mosaic-detail" to={`/?camera=${camera.id}`}>
          Detalle
        </Link>
      </div>
      {/* "Detener todo" cambia la generación: se remonta cada player y vuelve a reposo. */}
      <LivePlayer key={`${camera.id}-${generation}`} camera={camera} autoConnect={false} compact />
    </div>
  )
}

/**
 * Mosaico en vivo bajo demanda: ninguna cámara se conecta sola (tampoco una iCSee ya despierta), así mirar el
 * mosaico no mantiene despierta ninguna cámara a batería. Cada mosaico se conecta al tocar "Ver en vivo".
 */
export function LiveMosaicPage() {
  const cameras = useCameras()
  const canManage = useCanManage()
  const [generation, setGeneration] = useState(0)

  const all = cameras.data ?? []
  // Pausadas al final.
  const ordered = [...all.filter((c) => c.enabled), ...all.filter((c) => !c.enabled)]

  return (
    <div className="page" style={{ gap: 16 }}>
      <div className="head-row">
        <div>
          <h1 className="page-title">En vivo</h1>
          {all.length > 0 && <span className="head-sub">{summary(all)}</span>}
        </div>
        {all.length > 0 && (
          <button className="btn" onClick={() => setGeneration((g) => g + 1)}>
            Detener todo
          </button>
        )}
      </div>

      {cameras.isLoading && <p className="muted">Cargando cámaras…</p>}
      {cameras.error && <p className="error">{cameras.error.message}</p>}
      {cameras.data && all.length === 0 && (
        <div className="empty">
          Aún no hay cámaras.{canManage && <> Agrégalas en <Link to="/settings">Configuración</Link>.</>}
        </div>
      )}

      <div className="mosaic" data-single={ordered.length === 1 || undefined}>
        {ordered.map((camera) => (
          <LiveTile key={camera.id} camera={camera} generation={generation} />
        ))}
      </div>
    </div>
  )
}
