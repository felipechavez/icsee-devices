import { useEffect, useRef, useState } from 'react'
import { api } from '../../api/client'
import type { Camera } from '../../api/types'
import { useNow } from '../../hooks/useNow'
import { formatClock, formatTime } from '../../lib/format'

type ConnectionStatus = 'connecting' | 'live' | 'error'

const ICE_GATHER_TIMEOUT_MS = 2000
/** Sin imagen pasado este tiempo se muestra el error con "Reintentar" en lugar de quedar colgado. */
const FIRST_FRAME_TIMEOUT_MS = 20_000
/** Tuya tarda bastante más: despierta el dispositivo y negocia el video por su nube. */
const TUYA_FIRST_FRAME_TIMEOUT_MS = 90_000

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
 * y el video llega directo desde go2rtc. Se conecta solo cuando la cámara despierta; una
 * cámara Tuya también a demanda ("Ver en vivo" la despierta).
 */
export function LivePlayer({ camera }: { camera: Camera }) {
  const frameRef = useRef<HTMLDivElement>(null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const [attempt, setAttempt] = useState(0)
  const tuya = camera.kind === 'Tuya'
  // Vivo a demanda de una cámara Tuya en reposo: go2rtc pide el video a Tuya y eso la despierta.
  const [onDemand, setOnDemand] = useState(false)
  // "Detener" aplica solo a la activación actual: se identifica por la hora en que despertó.
  const [stoppedActivation, setStoppedActivation] = useState<string | null>(null)
  const activation = camera.lastChangedAt ?? ''
  const stopped = camera.isAwake && stoppedActivation === activation

  const demanded = tuya && onDemand && camera.enabled

  // Cada intento de conexión tiene su clave; un resultado de un intento anterior se ignora.
  // A demanda no depende de la activación: si la cámara "despierta" mientras se mira, no se corta.
  const connectionKey = demanded ? `${camera.id}|demand|${attempt}` : `${camera.id}|${activation}|${attempt}`
  const [result, setResult] = useState<{ key: string; status: ConnectionStatus; error?: string } | null>(null)
  const status: ConnectionStatus = result?.key === connectionKey ? result.status : 'connecting'

  const shouldConnect = demanded || (camera.isAwake && !stopped)
  const firstFrameTimeout = tuya ? TUYA_FIRST_FRAME_TIMEOUT_MS : FIRST_FRAME_TIMEOUT_MS

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
    // En Tuya la conexión con go2rtc se establece mucho antes de que el dispositivo despierte y mande
    // imagen: ahí solo cuenta el primer cuadro.
    const onPlaying = () => report('live')
    video?.addEventListener('playing', onPlaying)
    const timeout = setTimeout(() => {
      const hasFrame = !!video && video.readyState >= 2
      if (!hasFrame && (tuya || pc.connectionState !== 'connected')) {
        // Sin conexión con go2rtc el problema es de red (HOST_IP), no del dispositivo.
        report(
          'error',
          pc.connectionState !== 'connected'
            ? 'No se pudo conectar con el video. Si pasa siempre, revisa HOST_IP en .env (la IP de este equipo en la red) o ejecuta scripts/up.ps1.'
            : 'La cámara Tuya no entregó video. Puede estar sin conexión o con poca batería.',
        )
      }
    }, firstFrameTimeout)
    pc.onconnectionstatechange = () => {
      if (pc.connectionState === 'connected' && !tuya) report('live')
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
  }, [shouldConnect, connectionKey, camera.id, firstFrameTimeout])

  const live = shouldConnect && status === 'live'

  const stop = () => {
    setOnDemand(false)
    if (camera.isAwake) setStoppedActivation(activation)
  }

  // Se amplía el cuadro y no el <video>: conserva el giro por CSS y los botones (para "Minimizar").
  const [fullscreen, setFullscreen] = useState(false)
  useEffect(() => {
    const onChange = () => setFullscreen(document.fullscreenElement === frameRef.current)
    document.addEventListener('fullscreenchange', onChange)
    return () => document.removeEventListener('fullscreenchange', onChange)
  }, [])
  const toggleFullscreen = () =>
    void (fullscreen ? document.exitFullscreen() : frameRef.current?.requestFullscreen())

  return (
    <div className="frame" ref={frameRef} data-rotation={camera.rotation || undefined}>
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
            <button className="btn btn-overlay" onClick={toggleFullscreen}>
              {fullscreen ? 'Minimizar' : 'Pantalla completa'}
            </button>
            <button className="btn btn-overlay" onClick={stop}>
              Detener
            </button>
          </div>
        </>
      )}

      {shouldConnect && status === 'connecting' && (
        <div className="live-idle">
          <span className="live-badge" data-state="connecting">CONECTANDO…</span>
          {tuya && <p>Despertando la cámara: puede tardar hasta un minuto.</p>}
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
          ) : tuya ? (
            <>
              <strong>La cámara está en reposo</strong>
              <p>Graba sola con el movimiento, el timbre o las alarmas. Ver en vivo la despierta: tarda hasta un minuto y gasta batería.</p>
              <button className="btn btn-primary btn-lg" onClick={() => setOnDemand(true)}>
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
