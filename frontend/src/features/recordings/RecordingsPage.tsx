import { useSearchParams } from 'react-router-dom'
import { Segmented } from '../../components/Segmented'
import { dayLabel, daysBetween, shiftDay, todayKey } from '../../lib/format'
import { useCameras } from '../cameras/useCameras'
import { useSettings } from '../settings/useSettings'
import { ClipPlayerModal } from './ClipPlayerModal'
import { DayTimeline } from './DayTimeline'
import { RecordingList } from './RecordingList'
import { useRecordingList, useRecordingSummary, useTimeline } from './useRecordings'

type Filter = 'all' | 'failed'

export function RecordingsPage() {
  const [params, setParams] = useSearchParams()
  const today = todayKey()
  const day = params.get('day') ?? today
  const clipId = params.get('clip') // enlace directo desde la notificación ntfy
  const cameraParam = params.get('camera')
  const cameraId = cameraParam ? Number(cameraParam) : null
  const filter: Filter = params.get('status') === 'failed' ? 'failed' : 'all'

  const cameras = useCameras()
  const settings = useSettings()
  const summary = useRecordingSummary()
  const timeline = useTimeline(day, cameraId)
  const list = useRecordingList(day, cameraId, filter === 'failed' ? 'Failed' : null)

  const recordings = list.data?.pages.flatMap((p) => p.items) ?? []
  const openClip = recordings.find((r) => r.id === clipId)
  const marks = timeline.data ?? []
  const failedCount = marks.filter((m) => m.status === 'Failed').length
  const retentionDays = settings.data?.retentionDays ?? 30
  const isToday = day >= today
  const isOldest = daysBetween(day, today) >= retentionDays - 1

  const update = (changes: Record<string, string | null>) =>
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      for (const [key, value] of Object.entries(changes)) {
        if (value) next.set(key, value)
        else next.delete(key)
      }
      return next
    })

  const goToDay = (next: string) => update({ day: next === today ? null : next })

  return (
    <div className="page" style={{ gap: 24 }}>
      <div className="toolbar">
        <div className="toolbar-title">
          <h1 className="page-title">Grabaciones</h1>
          <p className="head-sub">
            {summary.data ? `${summary.data.totalCount} clips guardados` : 'Clips guardados'}
            {settings.data && ` · se conservan ${settings.data.retentionDays} días`}
          </p>
        </div>
        <div className="toolbar-actions">
          <Segmented<Filter>
            label="Filtro"
            value={filter}
            onChange={(value) => update({ status: value === 'failed' ? 'failed' : null })}
            options={[
              { value: 'all', label: `Todos · ${marks.length}` },
              { value: 'failed', label: `Fallidos · ${failedCount}` },
            ]}
          />
          <div className="daynav">
            <button type="button" aria-label="Día anterior" disabled={isOldest} onClick={() => goToDay(shiftDay(day, -1))}>
              ‹
            </button>
            <span>{dayLabel(day)}</span>
            <button type="button" aria-label="Día siguiente" disabled={isToday} onClick={() => goToDay(shiftDay(day, 1))}>
              ›
            </button>
          </div>
        </div>
      </div>

      {cameras.data && cameras.data.length > 1 && (
        <div className="chips" role="group" aria-label="Cámara">
          <button type="button" className="chip" aria-pressed={cameraId === null} onClick={() => update({ camera: null })}>
            Todas
          </button>
          {cameras.data.map((c) => (
            <button
              key={c.id}
              type="button"
              className="chip"
              aria-pressed={cameraId === c.id}
              onClick={() => update({ camera: String(c.id) })}
            >
              {c.name}
            </button>
          ))}
        </div>
      )}

      <DayTimeline items={marks} onOpen={(id) => update({ clip: id })} />

      {list.isLoading && <p className="muted">Cargando…</p>}
      {list.error && <p className="error">{list.error.message}</p>}
      {list.data && recordings.length === 0 && <div className="empty">No hay clips para mostrar este día.</div>}
      {recordings.length > 0 && <RecordingList recordings={recordings} onOpen={(r) => update({ clip: r.id })} />}

      {list.hasNextPage && (
        <div className="load-more">
          <button className="btn btn-solid" onClick={() => list.fetchNextPage()} disabled={list.isFetchingNextPage}>
            {list.isFetchingNextPage ? 'Cargando…' : 'Cargar más'}
          </button>
        </div>
      )}

      {clipId && (
        <ClipPlayerModal
          recordingId={clipId}
          recording={openClip}
          siblings={recordings}
          onNavigate={(id) => update({ clip: id })}
          onClose={() => update({ clip: null })}
        />
      )}
    </div>
  )
}
