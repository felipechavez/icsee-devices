import { useState } from 'react'
import { api } from '../../api/client'
import type { Recording } from '../../api/types'
import { Modal } from '../../components/Modal'
import { formatDay, formatSize, formatTime } from '../../lib/format'
import { useDeleteRecording, usePlaybackUrl } from './useRecordings'

interface Props {
  recordingId: string
  /** Datos del clip si ya están en la lista; un enlace de ntfy puede abrir uno que no esté cargado. */
  recording?: Recording
  /** Lista visible (orden descendente) para "Anterior / Siguiente". */
  siblings?: Recording[]
  onNavigate?: (id: string) => void
  onClose: () => void
}

const openable = (r: Recording) => r.status === 'Uploaded' || r.status === 'Failed'

export function ClipPlayerModal({ recordingId, recording, siblings = [], onNavigate, onClose }: Props) {
  const failed = recording?.status === 'Failed'
  const playback = usePlaybackUrl(recordingId, !failed)
  const remove = useDeleteRecording()
  const [downloadError, setDownloadError] = useState<string | null>(null)

  const nav = siblings.filter(openable)
  const index = nav.findIndex((r) => r.id === recordingId)
  // La lista viene de la más reciente a la más antigua: "Anterior" es la siguiente en la lista.
  const older = index >= 0 ? nav[index + 1] : undefined
  const newer = index > 0 ? nav[index - 1] : undefined

  const download = async () => {
    setDownloadError(null)
    try {
      const { url } = await api.playbackUrl(recordingId, true)
      window.location.assign(url)
    } catch (e) {
      setDownloadError(e instanceof Error ? e.message : 'No se pudo descargar.')
    }
  }

  const handleDelete = () => {
    if (!confirm('¿Eliminar este clip? No se puede deshacer.')) return
    // Tras borrar se pasa al clip vecino, o se cierra si no hay.
    const next = older ?? newer
    remove.mutate(recordingId, {
      onSuccess: () => {
        if (next && onNavigate) onNavigate(next.id)
        else onClose()
      },
    })
  }

  const error = playback.error?.message ?? remove.error?.message ?? downloadError

  return (
    <Modal
      label="Reproductor de clip"
      onClose={onClose}
      onKeyDown={(e) => {
        if (e.key === 'ArrowLeft' && older) onNavigate?.(older.id)
        if (e.key === 'ArrowRight' && newer) onNavigate?.(newer.id)
      }}
    >
      <div className="player-inner">
        <div className="player-head">
          <div>
            <span className="player-time">{recording ? formatTime(recording.startedAt) : 'Clip'}</span>
            {recording && (
              <span className="player-sub">
                {recording.cameraName} · {formatDay(recording.startedAt)}
              </span>
            )}
          </div>
          <div className="player-nav">
            {onNavigate && (
              <>
                <button className="btn btn-solid" disabled={!older} onClick={() => older && onNavigate(older.id)}>
                  ‹ Anterior
                </button>
                <button className="btn btn-solid" disabled={!newer} onClick={() => newer && onNavigate(newer.id)}>
                  Siguiente ›
                </button>
              </>
            )}
            <button className="btn btn-primary" onClick={onClose} autoFocus>
              Cerrar
            </button>
          </div>
        </div>

        <div className="frame">
          {failed ? (
            <div className="player-failed">
              <strong>No se pudo grabar este clip</strong>
              <span>La cámara detectó movimiento pero no respondió a tiempo. Revisa la señal Wi-Fi o la batería.</span>
            </div>
          ) : playback.data ? (
            <video key={playback.data.url} src={playback.data.url} controls autoPlay playsInline />
          ) : (
            <div className="player-loading">{playback.isLoading ? 'Cargando…' : ''}</div>
          )}
        </div>

        <div className="player-foot">
          {error ? (
            <span className="error">{error}</span>
          ) : (
            recording?.sizeBytes != null && <span className="muted">{formatSize(recording.sizeBytes)}</span>
          )}
          {!failed && (
            <button className="btn" onClick={download}>
              Descargar
            </button>
          )}
          <button className="btn btn-danger" onClick={handleDelete} disabled={remove.isPending}>
            {remove.isPending ? 'Eliminando…' : 'Eliminar'}
          </button>
        </div>
      </div>
    </Modal>
  )
}
