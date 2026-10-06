import { api } from './client'
import type { LoginResponse, User } from '../types/api'

export const authApi = {
  async login(email: string, password: string): Promise<LoginResponse> {
    const { data } = await api.post<LoginResponse>('/api/auth/login', { email, password })
    return data
  },
  async me(): Promise<User> {
    const { data } = await api.get<User>('/api/auth/me')
    return data
  },
}
