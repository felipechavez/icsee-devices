import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'

/** Usuario de la sesión: no cambia sin volver a iniciar sesión (los roles vienen en el token). */
export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: api.me, staleTime: Infinity })
}
