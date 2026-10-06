import axios from 'axios'
import { tokenStore } from '../auth/tokenStore'
import type { ApiErrorBody } from '../types/api'

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/',
  headers: { Accept: 'application/json' },
})

api.interceptors.request.use((config) => {
  const token = tokenStore.get()
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

api.interceptors.response.use(
  (response) => response,
  (error: unknown) => {
    if (axios.isAxiosError(error) && error.response?.status === 401 && tokenStore.get()) {
      tokenStore.clear()
    }
    return Promise.reject(error)
  },
)

export function apiErrorMessage(error: unknown, fallback: string): string {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as unknown
    if (typeof data === 'string' && data.trim()) return data
    const payload = data && typeof data === 'object' ? data as ApiErrorBody : undefined
    if (payload?.message) return payload.message
    if (payload?.title) return payload.title
    const firstValidationError = payload?.errors && Object.values(payload.errors).flat()[0]
    if (firstValidationError) return firstValidationError
    if (error.response?.status === 413) return 'Dosya boyutu izin verilen sınırı aşıyor.'
  }
  return fallback
}
