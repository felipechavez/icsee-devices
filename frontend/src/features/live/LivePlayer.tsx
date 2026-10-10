import { useEffect, useRef, useState } from 'react'
import { api } from '../../api/client'
import type { Camera } from '../../api/types'
import { useNow } from '../../hooks/useNow'
import { formatClock, formatTime } from '../../lib/format'

type ConnectionStatus = 'connecting' | 'live' | 'error'

const ICE_GATHER_TIMEOUT_MS = 2000
/** Sin imagen pasado este tiempo se muestra el error con "Reintentar" en lugar de quedar colgado. */
const FIRST_FRAME_TIMEOUT_MS = 20_000

/** Espera a que termine la recolección de candidatos ICE (go2rtc no usa trickle). */
function waitForIceGathering(pc: RTCPeerConnection) {
  if (pc.iceGatheringState === 'complete') return Promise.resolve()
  return new Promise<void>((resolve) => {
    const done = () => {
      pc.removeEventListener('icegatheringstatechange', check)
      clearTimeout(timer)
      resolve()
    }
    const check = () => pc.iceGatheringState === 'complete' && done()
    const timer = setTimeout(done, ICE_GATHER_TIMEOUT_MS)
    pc.addEventListener('icegatheringstatechange', check)
  })
}

/**
 * Video en vivo por WebRTC. La oferta SDP pasa por la API (/api/live/{id}/webrtc)
 * y el video llega directo desde go2rtc. Se conecta solo cuando la cámara despierta.
 */
export function LivePlayer({ camera }: { camera: Camera }) {
  const videoRef = useRef<HTMLVideoElement>(null)
  const [attempt, setAttempt] = useState(0)
  // "Detener" aplica solo a la activación actual: se identifica por la hora en que despertó.
  const [stoppedActivation, setStoppedActivation] = useState<string | null>(null)
  const activation = camera.lastChangedAt ?? ''
  const stopped = camera.isAwake && stoppedActivation === activation

  // Cada intento de conexión tiene su clave; un resultado de un intento anterior se ignora.
  const connectionKey = `${camera.id}|${activation}|${attempt}`
  const [result, setResult] = useState<{ key: string; status: ConnectionStatus; error?: string } | null>(null)
  const status: ConnectionStatus = result?.key === connectionKey ? result.status : 'connecting'

  const shouldConnect = camera.isAwake && !stopped

  useEffect(() => {
    if (!shouldConnect) return

    const video = videoRef.current
    const pc = new RTCPeerConnection({ iceServers: [{ urls: 'stun:stun.l.google.com:19302' }] })
    let closed = false
    const report = (status: ConnectionStatus, error?: string) => {
      if (!closed) setResult({ key: connectionKey, status, error })
    }

    pc.addTransceiver('video', { direction: 'recvonly' })
    pc.addTransceiver('audio', { direction: 'recvonly' })
    pc.ontrack = (event) => {
      if (video && event.streams[0]) video.srcObject = event.streams[0]
    }
    // "En vivo" cuando llega el primer cuadro (o la conexión queda establecida, lo que ocurra antes).
    const onPlaying = () => report('live')
    video?.addEventListener('playing', onPlaying)
    const timeout = setTimeout(() => {
      if (pc.connectionState !== 'connected' && !(video && video.readyState >= 2)) {
        report('error', 'No llegó video de la cámara. Si pasa siempre, revisa HOST_IP en .env (la IP de este equipo en la red).')
      }
    }, FIRST_FRAME_TIMEOUT_MS)
    pc.onconnectionstatechange = () => {
      if (pc.connectionState === 'connected') report('live')
      if (pc.connectionState === 'failed' || pc.connectionState === 'disconnected') {
        report('error', 'Se perdió la conexión con el video.')
      }
    }

    const connect = async () => {
      try {
        await pc.setLocalDescription(await pc.createOffer())
        await waitForIceGathering(pc)
        const answer = await api.webrtcAnswer(camera.id, pc.localDescription!.sdp)
        if (closed) return
        await pc.setRemoteDescription({ type: 'answer', sdp: answer })
      } catch (e) {
        report('error', e instanceof Error ? e.message : 'No se pudo iniciar el video.')
      }
    }
    connect()

    return () => {
      closed = true
      clearTimeout(timeout)
      video?.removeEventListener('playing', onPlaying)
      pc.close()
      if (video) video.srcObject = null
    }
  }, [shouldConnect, connectionKey, camera.id])

  const live = shouldConnect && status === 'live'

  return (
    <div className="frame">
      {!live &&
        (camera.lastThumbnailUrl ? (
          <img className="live-still" src={camera.lastThumbnailUrl} alt="Último fotograma" />
        ) : (
          <div className="frame-empty" />
        ))}
      {/* Siempre montado (recibe el stream); invisible hasta que hay imagen. */}
      <video ref={videoRef} autoPlay muted playsInline style={{ visibility: live ? undefined : 'hidden' }} />

      {live && (
        <>
          <div className="frame-tl live-badge">
            EN VIVO · <LiveClock />
          </div>
          <div className="frame-br" style={{ display: 'flex', gap: 8 }}>
            <button className="btn btn-overlay" onClick={() => void videoRef.current?.requestFullscreen()}>
              Pantalla completa
            </button>
            <button className="btn btn-overlay" onClick={() => setStoppedActivation(activation)}>
              Detener
            </button>
          </div>
        </>
      )}

      {shouldConnect && status === 'connecting' && (
        <div className="live-idle">
          <span className="live-badge" data-state="connecting">CONECTANDO…</span>
        </div>
      )}

      {shouldConnect && status === 'error' && (
        <div className="live-idle">
          <strong>No se pudo mostrar el video</strong>
          <p>{result?.error}</p>
          <button className="btn btn-primary btn-lg" onClick={() => setAttempt((n) => n + 1)}>
            Reintentar
          </button>
        </div>
      )}

      {!shouldConnect && (
        <div className="live-idle">
          {!camera.enabled ? (
            <>
              <strong>Cámara pausada</strong>
              <p>No se monitorea ni graba. Actívala en Configuración.</p>
            </>
          ) : stopped ? (
            <>
              <strong>Transmisión detenida</strong>
              <p>La cámara sigue despierta. Mirar en vivo la mantiene encendida y consume batería.</p>
              <button className="btn btn-primary btn-lg" onClick={() => setStoppedActivation(null)}>
                Ver en vivo
              </button>
            </>
          ) : (
            <>
              <strong>La cámara está en reposo</strong>
              <p>Se activa sola al detectar movimiento; el video aparecerá aquí en cuanto despierte.</p>
            </>
          )}
        </div>
      )}

      {!live && camera.lastRecordingAt && (
        <div className="frame-bl live-caption">Último fotograma · {formatTime(camera.lastRecordingAt)}</div>
      )}
    </div>
  )
}

function LiveClock() {
  return <>{formatClock(useNow())}</>
}
