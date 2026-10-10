import type { Recording } from '../../api/types'
import { formatDuration, formatTime } from '../../lib/format'

/** Miniatura del clip, o el motivo por el que aún no hay video. */
export function ClipThumb({ recording }: { recording: Recording }) {
  switch (recording.status) {
    case 'Failed':
      return (
        <div className="clip-note clip-note-failed">
          <strong>No se pudo grabar</strong>
          <span>Cámara sin respuesta</span>
        </div>
      )
    case 'Recording':
    case 'Uploading':
      return (
        <div className="clip-note stripes">
          <strong>{recording.status === 'Recording' ? 'Grabando…' : 'Subiendo…'}</strong>
        </div>
      )
    default:
      return (
        <>
          {recording.thumbnailUrl ? (
            <img src={recording.thumbnailUrl} alt="" loading="lazy" />
          ) : (
            <div className="clip-note">Sin miniatura</div>
          )}
          <span className="clip-dur">{formatDuration(recording.durationSec)}</span>
        </>
      )
  }
}

interface Props {
  recording: Recording
  onOpen: (r: Recording) => void
  /** Texto a la derecha de la hora; por defecto, la cámara. */
  meta?: string
}

/** Los clips en curso no se abren; los fallidos sí, para ver la explicación. */
export function RecordingCard({ recording, onOpen, meta }: Props) {
  const openable = recording.status === 'Uploaded' || recording.status === 'Failed'

  return (
    <button
      type="button"
      className="clip"
      aria-disabled={!openable}
      onClick={() => openable && onOpen(recording)}
    >
      <div className="clip-thumb">
        <ClipThumb recording={recording} />
      </div>
      <div className="clip-meta">
        <span>{formatTime(recording.startedAt)}</span>
        <span>{meta ?? recording.cameraName}</span>
      </div>
    </button>
  )
}
