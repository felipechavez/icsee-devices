import { createContext, useContext, useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { getAccessToken } from '../auth/oidc'
import type {
  Camera,
  CameraInfoMessage,
  CameraStatusMessage,
  RecordingCompletedMessage,
  RecordingStartedMessage,
} from '../api/types'

export interface HubEvents {
  CameraAwake: CameraStatusMessage
  CameraAsleep: CameraStatusMessage
  CameraInfoUpdated: CameraInfoMessage
  RecordingStarted: RecordingStartedMessage
  RecordingCompleted: RecordingCompletedMessage
}

type HubStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

interface HubContextValue {
  connection: HubConnection | null
  status: HubStatus
}

const HubContext = createContext<HubContextValue>({ connection: null, status: 'disconnected' })

// Reintentos indefinidos: 1 s, 2 s, 4 s… hasta 30 s.
const retryDelay = (attempt: number) => Math.min(30_000, 1000 * 2 ** attempt)

/**
 * Mantiene una sola conexión a /hubs/events y sincroniza la caché de
 * TanStack Query con los eventos del servidor.
 */
export function EventsHubProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [connection, setConnection] = useState<HubConnection | null>(null)
  const [status, setStatus] = useState<HubStatus>('disconnected')

  useEffect(() => {
    const hub = new HubConnectionBuilder()
      .withUrl('/hubs/events', { accessTokenFactory: getAccessToken })
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => retryDelay(ctx.previousRetryCount) })
      .configureLogging(LogLevel.Warning)
      .build()

    // Los handlers no deben devolver nada: SignalR toma cualquier valor devuelto
    // como respuesta al servidor y lo reporta como error.
    const patchCamera = (id: number, patch: Partial<Camera>) => {
      queryClient.setQueryData<Camera[]>(['cameras'], (cameras) =>
        cameras?.map((c) => (c.id === id ? { ...c, ...patch } : c)),
      )
    }

    hub.on('CameraAwake', (msg: CameraStatusMessage) => {
      patchCamera(msg.cameraId, { isAwake: true, lastChangedAt: msg.at })
    })
    hub.on('CameraAsleep', (msg: CameraStatusMessage) => {
      patchCamera(msg.cameraId, { isAwake: false, isRecording: false, lastChangedAt: msg.at })
    })
    hub.on('CameraInfoUpdated', ({ cameraId, ...info }: CameraInfoMessage) => {
      patchCamera(cameraId, info)
    })
    hub.on('RecordingStarted', (msg: RecordingStartedMessage) => {
      patchCamera(msg.cameraId, { isRecording: true, lastRecordingAt: msg.startedAt })
      void queryClient.invalidateQueries({ queryKey: ['recordings'] })
    })
    hub.on('RecordingCompleted', () => {
      // Cambian la miniatura y los conteos de cada cámara, además de las listas.
      void queryClient.invalidateQueries({ queryKey: ['recordings'] })
      void queryClient.invalidateQueries({ queryKey: ['cameras'] })
    })
    hub.onreconnecting(() => setStatus('reconnecting'))
    hub.onreconnected(() => {
      setStatus('connected')
      // Pudimos perder eventos mientras estábamos desconectados.
      queryClient.invalidateQueries({ queryKey: ['cameras'] })
      queryClient.invalidateQueries({ queryKey: ['recordings'] })
    })
    hub.onclose(() => setStatus('disconnected'))

    let cancelled = false
    let attempt = 0
    let timer: ReturnType<typeof setTimeout>

    // withAutomaticReconnect no cubre el primer start(); se reintenta a mano.
    const start = async () => {
      setStatus('connecting')
      try {
        await hub.start()
        if (cancelled) return
        setStatus('connected')
        setConnection(hub)
      } catch {
        if (cancelled) return
        timer = setTimeout(start, retryDelay(attempt++))
      }
    }
    start()

    return () => {
      cancelled = true
      clearTimeout(timer)
      setConnection(null)
      setStatus('disconnected')
      if (hub.state !== HubConnectionState.Disconnected) hub.stop()
    }
  }, [queryClient])

  return <HubContext.Provider value={{ connection, status }}>{children}</HubContext.Provider>
}

/** Suscribe un manejador a un evento del hub mientras el componente esté montado. */
export function useEventsHub<E extends keyof HubEvents>(event: E, handler: (message: HubEvents[E]) => void) {
  const { connection } = useContext(HubContext)
  const handlerRef = useRef(handler)

  useEffect(() => {
    handlerRef.current = handler
  })

  useEffect(() => {
    if (!connection) return
    const listener = (message: HubEvents[E]) => {
      handlerRef.current(message)
    }
    connection.on(event, listener)
    return () => connection.off(event, listener)
  }, [connection, event])
}

export function useHubStatus() {
  return useContext(HubContext).status
}
