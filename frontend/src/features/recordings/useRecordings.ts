import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { RecordingStatus } from '../../api/types'
import { localDayRange } from '../../lib/format'

const PAGE_SIZE = 48

/** Historial paginado de un día; el hub invalida ['recordings'] al llegar RecordingCompleted. */
export function useRecordingList(day: string, cameraId: number | null, status: RecordingStatus | null) {
  return useInfiniteQuery({
    queryKey: ['recordings', 'list', day, cameraId, status],
    queryFn: ({ pageParam }) =>
      api.recordings({
        ...localDayRange(day),
        cameraId: cameraId ?? undefined,
        status: status ?? undefined,
        page: pageParam,
        pageSize: PAGE_SIZE,
      }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasMore ? last.page + 1 : undefined),
  })
}

/** Todas las marcas del día (sin paginar) para la línea de tiempo y el histograma. */
export function useTimeline(day: string, cameraId: number | null = null) {
  return useQuery({
    queryKey: ['recordings', 'timeline', day, cameraId],
    queryFn: () => api.timeline({ ...localDayRange(day), cameraId: cameraId ?? undefined }),
  })
}

export function useRecordingSummary() {
  return useQuery({ queryKey: ['recordings', 'summary'], queryFn: api.recordingSummary })
}

export function useLatestRecordings(count: number, cameraId: number | null = null) {
  return useQuery({
    queryKey: ['recordings', 'latest', count, cameraId],
    queryFn: () => api.recordings({ page: 1, pageSize: count, cameraId: cameraId ?? undefined }),
    select: (page) => page.items,
  })
}

export function usePlaybackUrl(id: string, enabled = true) {
  // Las URLs prefirmadas caducan en minutos: no se reutilizan entre aperturas.
  return useQuery({
    queryKey: ['playback', id],
    queryFn: () => api.playbackUrl(id),
    gcTime: 0,
    staleTime: Infinity,
    enabled,
  })
}

export function useDeleteRecording() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: api.deleteRecording,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['recordings'] })
      void queryClient.invalidateQueries({ queryKey: ['cameras'] })
    },
  })
}
