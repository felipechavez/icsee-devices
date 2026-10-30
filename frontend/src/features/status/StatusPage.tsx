import { Link } from 'react-router-dom'
import type { CameraHealth, HealthIssue, ServiceHealth, SystemStatus } from '../../api/types'
import { BatteryBadge } from '../../components/BatteryBadge'
import { useNow } from '../../hooks/useNow'
import { formatAgo, formatGb } from '../../lib/format'
import { useSettings } from '../settings/useSettings'
import { BatterySparkline } from './BatterySparkline'
import { useSystemStatus } from './useSystemStatus'

type Health = 'ok' | 'warn' | 'error'

function daysLeftLabel(days: number | null) {
  if (days == null) return null
  if (days < 1) return 'Se agota en menos de un día'
  if (days > 60) return 'Alcanza para más de 2 meses'
  return `Alcanza para ~${Math.round(days)} ${Math.round(days) === 1 ? 'día' : 'días'}`
}

function Issues({ issues, now }: { issues: HealthIssue[]; now: number }) {
  if (issues.length === 0) {
    return (
      <div className="issue" data-health="ok">
        <span className="dot" />
        <div>
          <strong>Todo en orden</strong>
          <p>Las cámaras, los servicios y la cola de subida funcionan con normalidad.</p>
        </div>
      </div>
    )
  }
  return (
    <ul className="issue-list">
      {issues.map((i) => (
        <li key={i.key} className="issue" data-health={i.severity === 'Critical' ? 'error' : 'warn'}>
          <span className="dot" />
          <div>
            <strong>{i.title}</strong>
            {i.detail && <p>{i.detail}</p>}
            <span className="issue-meta">
              Desde {formatAgo(i.since, now)}
              {i.cameraId != null && (
                <>
                  {' · '}
                  <Link to={`/?camera=${i.cameraId}`}>Ver cámara</Link>
                </>
              )}
            </span>
          </div>
        </li>
      ))}
    </ul>
  )
}

function CameraCard({ camera, threshold, now }: { camera: CameraHealth; threshold?: number; now: number }) {
  const forecast = daysLeftLabel(camera.batteryDaysLeft)
  return (
    <div className="panel health-cam" data-paused={camera.enabled ? undefined : ''}>
      <div className="health-cam-head">
        <strong>{camera.name}</strong>
        {camera.enabled ? <BatteryBadge percent={camera.batteryPercent} /> : <span className="muted">Pausada</span>}
      </div>
      {camera.batteryPercent != null && (
        <div className="meter">
          <BatterySparkline points={camera.batteryHistory} threshold={threshold} now={now} />
          <div className="axis">
            <span>hace 14 d</span>
            <span>hoy</span>
          </div>
        </div>
      )}
      <div className="rows-compact">
        {camera.batteryPercent != null && (
          <div className="kv">
            <span>Pronóstico</span>
            <span>{forecast ?? 'Faltan lecturas'}</span>
          </div>
        )}
        <div className="kv">
          <span>Última activación</span>
          <span>{camera.lastAwakeAt ? formatAgo(camera.lastAwakeAt, now) : 'Sin registro'}</span>
        </div>
        <div className="kv">
          <span>Activaciones por día</span>
          <span>{camera.activationsPerDay.toLocaleString('es')}</span>
        </div>
        {camera.deviceInfoAt && (
          <div className="kv">
            <span>Batería leída</span>
            <span>{formatAgo(camera.deviceInfoAt, now)}</span>
          </div>
        )}
      </div>
    </div>
  )
}

function serviceRow(s: ServiceHealth, now: number): { label: string; text: string; health: Health } {
  const since = s.since ? formatAgo(s.since, now) : null
  const failing = (what: string) => [what + (since ? ` desde ${since}` : ''), s.detail].filter(Boolean).join(' · ')
  switch (s.name) {
    case 'go2rtc':
      return { label: 'go2rtc', text: s.ok ? 'Responde' : failing('Sin respuesta'), health: s.ok ? 'ok' : 'error' }
    case 'tuya':
      return { label: 'Tuya', text: s.ok ? 'Conectado al Message Service' : failing('Desconectado'), health: s.ok ? 'ok' : 'error' }
    case 'r2':
      return {
        label: 'Subidas a R2',
        text: s.ok ? (since ? `Última subida ${since}` : 'Sin subidas desde que arrancó') : failing('Fallando'),
        health: s.ok ? 'ok' : 'error',
      }
  }
}

function Services({ status, now }: { status: SystemStatus; now: number }) {
  const open = new Set(status.issues.map((i) => i.key))
  const { uploads, disk, storage } = status
  const rows = status.services.map((s) => serviceRow(s, now))

  const queueText =
    uploads.pending === 0
      ? 'Al día'
      : `${uploads.pending} ${uploads.pending === 1 ? 'clip' : 'clips'} por subir` +
        (uploads.oldestPendingAt ? ` · el más antiguo ${formatAgo(uploads.oldestPendingAt, now)}` : '')
  rows.push({
    label: 'Cola de subida',
    text: queueText + (uploads.analyzing > 0 ? ` · ${uploads.analyzing} en análisis` : ''),
    health: open.has('queue') ? 'warn' : 'ok',
  })

  if (disk) {
    rows.push({
      label: 'Disco local',
      text: `${formatGb(disk.freeBytes)} GB libres de ${formatGb(disk.totalBytes)} GB`,
      health: open.has('disk') ? 'error' : 'ok',
    })
  }

  return (
    <div className="panel">
      {rows.map((r) => (
        <div key={r.label} className="svc-row" data-health={r.health}>
          <span className="dot" />
          <span className="svc-label">{r.label}</span>
          <span className="svc-text">{r.text}</span>
        </div>
      ))}
      <div className="panel-foot">
        R2: <span className="mono">{formatGb(storage.usedBytes)} GB</span> en {storage.clips.toLocaleString('es')} clips ·
        costo estimado <span className="mono">US$ {storage.estimatedMonthlyUsd.toFixed(2)}</span>/mes
      </div>
    </div>
  )
}

export function StatusPage() {
  const status = useSystemStatus()
  const settings = useSettings()
  const now = useNow(60_000).getTime()
  const data = status.data

  return (
    <div className="page">
      <div className="head-row">
        <div>
          <h1 className="page-title">Estado</h1>
          {data && <span className="head-sub">Revisado {formatAgo(data.checkedAt, now)}</span>}
        </div>
        <Link className="head-sub" to="/settings">
          Ajustar avisos
        </Link>
      </div>

      {status.error && <p className="error">{status.error.message}</p>}
      {status.isLoading && <p className="muted">Cargando…</p>}
      {data === null && <p className="muted">Recopilando el estado del sistema; la primera revisión toma unos segundos.</p>}

      {data && (
        <>
          <section className="status-section">
            <h2 className="section-title">Problemas</h2>
            <Issues issues={data.issues} now={now} />
          </section>

          {data.cameras.length > 0 && (
            <section className="status-section">
              <h2 className="section-title">Cámaras</h2>
              <div className="health-grid">
                {data.cameras.map((c) => (
                  <CameraCard key={c.id} camera={c} threshold={settings.data?.lowBatteryPercent} now={now} />
                ))}
              </div>
            </section>
          )}

          <section className="status-section">
            <h2 className="section-title">Servicios</h2>
            <Services status={data} now={now} />
          </section>
        </>
      )}
    </div>
  )
}
