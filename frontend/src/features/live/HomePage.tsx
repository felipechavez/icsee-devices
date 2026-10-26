import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import type { Camera, Recording } from '../../api/types'
import { BatteryBadge } from '../../components/BatteryBadge'
import { StatusPill } from '../../components/StatusPill'
import { useNow } from '../../hooks/useNow'
import { batteryLevel, cameraStatus, LOW_BATTERY } from '../../lib/cameraStatus'
import { formatAgo, formatGb, formatTime, hourBuckets, localDayKey, todayKey } from '../../lib/format'
import { useCameras } from '../cameras/useCameras'
import { ClipPlayerModal } from '../recordings/ClipPlayerModal'
import { RecordingCard } from '../recordings/RecordingCard'
import { useLatestRecordings, useTimeline } from '../recordings/useRecordings'
import { LivePlayer } from './LivePlayer'

function summary(cameras: Camera[]) {
  const recording = cameras.filter((c) => cameraStatus(c).key === 'recording').length
  const lowBattery = cameras.filter((c) => c.batteryPercent != null && c.batteryPercent < LOW_BATTERY).length
  return [
    cameras.length === 1 ? '1 cámara' : `${cameras.length} cámaras`,
    recording > 0 && `${recording} grabando`,
    lowBattery > 0 && `${lowBattery} con batería baja`,
  ]
    .filter(Boolean)
    .join(' · ')
}

function CameraCard({ camera, onOpen }: { camera: Camera; onOpen: () => void }) {
  const status = cameraStatus(camera)
  return (
    <button type="button" className="cam-card" data-status={status.key} onClick={onOpen}>
      <div className="frame">
        {camera.lastThumbnailUrl ? <img src={camera.lastThumbnailUrl} alt="" /> : <div className="frame-empty" />}
        <div className="frame-shade" />
        <span className="frame-tl">
          <StatusPill camera={camera} overlay />
        </span>
        <span className="frame-tr">
          <BatteryBadge percent={camera.batteryPercent} overlay />
        </span>
        <div className="frame-bl cam-caption">
          <strong>{camera.name}</strong>
          <span>{[camera.model, camera.ip].filter(Boolean).join(' · ')}</span>
        </div>
      </div>
      <div className="cam-meta">
        <span>
          Último movimiento{' '}
          <span className="mono">{camera.lastRecordingAt ? formatTime(camera.lastRecordingAt) : '—'}</span>
        </span>
        {camera.clipsToday != null && <span>{camera.clipsToday === 1 ? '1 clip hoy' : `${camera.clipsToday} clips hoy`}</span>}
      </div>
    </button>
  )
}

function StatePanel({ camera }: { camera: Camera }) {
  const now = useNow(30_000)
  const storage =
    camera.storageTotalBytes != null && camera.storageUsedBytes != null
      ? { used: camera.storageUsedBytes, total: camera.storageTotalBytes }
      : null

  return (
    <div className="panel">
      <div className="panel-label">Estado</div>
      {camera.batteryPercent != null && (
        <div className="meter" data-battery={batteryLevel(camera.batteryPercent)}>
          <div className="kv">
            <span>Batería</span>
            <span>{camera.batteryPercent}%</span>
          </div>
          <div className="meter-bar">
            <div style={{ width: `${camera.batteryPercent}%` }} />
          </div>
        </div>
      )}
      {storage && (
        <div className="meter">
          <div className="kv">
            <span>Almacenamiento</span>
            <span>
              {formatGb(storage.used)} / {formatGb(storage.total)} GB
            </span>
          </div>
          <div className="meter-bar">
            <div style={{ width: `${Math.min(100, (storage.used / storage.total) * 100)}%` }} />
          </div>
        </div>
      )}
      <div className="kv">
        <span>Red</span>
        <span>{camera.ip}</span>
      </div>
      <p className="panel-note">
        {camera.deviceInfoAt
          ? `Leído ${formatAgo(camera.deviceInfoAt, now.getTime())}, la última vez que despertó.`
          : 'Batería y almacenamiento se leen la próxima vez que la cámara despierte.'}
      </p>
    </div>
  )
}

function ActivityPanel({ camera }: { camera: Camera }) {
  const timeline = useTimeline(todayKey(), camera.id)
  const items = (timeline.data ?? []).filter((i) => i.status !== 'Discarded')
  const counts = hourBuckets(items)
  const max = Math.max(...counts, 1)

  return (
    <div className="panel panel-grow">
      <div className="head-row">
        <div className="panel-label">Actividad hoy</div>
        <span className="head-sub" style={{ fontSize: 13 }}>
          {items.length === 1 ? '1 clip' : `${items.length} clips`}
        </span>
      </div>
      <div className="hist">
        {counts.map((count, hour) => (
          <div
            key={hour}
            data-on={count > 0 || undefined}
            style={{ height: `${(count / max) * 100}%` }}
            title={`${hour}:00 · ${count} clips`}
          />
        ))}
      </div>
      <div className="axis">
        <span>00</span>
        <span>06</span>
        <span>12</span>
        <span>18</span>
        <span>24</span>
      </div>
      <div className="panel-foot">
        {camera.lastRecordingAt ? (
          <>
            Último movimiento {localDayKey(camera.lastRecordingAt) === todayKey() ? 'a las' : 'el'}{' '}
            <span className="mono">
              {localDayKey(camera.lastRecordingAt) === todayKey()
                ? formatTime(camera.lastRecordingAt)
                : new Date(camera.lastRecordingAt).toLocaleString('es')}
            </span>
          </>
        ) : (
          'Todavía no hay movimientos registrados.'
        )}
      </div>
    </div>
  )
}

function CameraDetail({ camera, onBack }: { camera: Camera; onBack?: () => void }) {
  return (
    <div className="single">
      <section className="single-main">
        <div className="single-head">
          <div>
            {onBack && (
              <button className="btn" onClick={onBack}>
                ‹ Todas
              </button>
            )}
            <h1>{camera.name}</h1>
            {camera.model && <span className="head-sub" style={{ fontSize: 13 }}>{camera.model}</span>}
          </div>
          <StatusPill camera={camera} />
        </div>
        <LivePlayer camera={camera} />
      </section>
      <aside className="aside">
        <div className="aside-spacer" />
        <StatePanel camera={camera} />
        <ActivityPanel camera={camera} />
      </aside>
    </div>
  )
}

export function HomePage() {
  const cameras = useCameras()
  const [params, setParams] = useSearchParams()
  const [open, setOpen] = useState<Recording | null>(null)
  const now = useNow(60_000)

  const all = cameras.data ?? []
  const focusId = params.get('camera')
  const focused = all.length === 1 ? all[0] : all.find((c) => String(c.id) === focusId)
  const latest = useLatestRecordings(5, all.length > 1 ? (focused?.id ?? null) : null)
  const recent = latest.data ?? []

  const focus = (id: number | null) => setParams(id === null ? {} : { camera: String(id) })

  return (
    <div className="page">
      {cameras.isLoading && <p className="muted">Cargando cámaras…</p>}
      {cameras.error && <p className="error">{cameras.error.message}</p>}
      {cameras.data && all.length === 0 && (
        <div className="empty">
          Aún no hay cámaras. Agrégalas en <Link to="/settings">Configuración</Link>.
        </div>
      )}

      {focused ? (
        <CameraDetail camera={focused} onBack={all.length > 1 ? () => focus(null) : undefined} />
      ) : (
        all.length > 0 && (
          <section className="page" style={{ gap: 16 }}>
            <div className="head-row">
              <div>
                <h1 className="page-title">Cámaras</h1>
                <span className="head-sub">{summary(all)}</span>
              </div>
              <Link className="btn" to="/settings">
                Administrar
              </Link>
            </div>
            <div className="cam-grid">
              {all.map((camera) => (
                <CameraCard key={camera.id} camera={camera} onOpen={() => focus(camera.id)} />
              ))}
            </div>
          </section>
        )
      )}

      {all.length > 0 && (
        <section className="page" style={{ gap: 14 }}>
          <div className="head-row">
            <h2 className="section-title">Últimos clips</h2>
            <Link className="btn btn-link" to="/recordings">
              Ver todos →
            </Link>
          </div>
          {latest.data && recent.length === 0 && <div className="empty">Todavía no hay clips.</div>}
          <div className="clip-grid">
            {recent.map((r) => (
              <RecordingCard
                key={r.id}
                recording={r}
                onOpen={setOpen}
                meta={
                  localDayKey(r.startedAt) === todayKey()
                    ? `${r.cameraName} · ${formatAgo(r.startedAt, now.getTime())}`
                    : r.cameraName
                }
              />
            ))}
          </div>
        </section>
      )}

      {open && (
        <ClipPlayerModal
          recordingId={open.id}
          recording={recent.find((r) => r.id === open.id) ?? open}
          siblings={recent}
          onNavigate={(id) => setOpen(recent.find((r) => r.id === id) ?? null)}
          onClose={() => setOpen(null)}
        />
      )}
    </div>
  )
}
