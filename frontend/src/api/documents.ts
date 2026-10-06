import { api } from './client'
import type { DocumentDetails, DocumentInput, DocumentPage } from '../types/api'

export interface DocumentPageParams {
  page: number
  pageSize: number
}

function documentForm(input: Pick<DocumentInput, 'file'> & Partial<Omit<DocumentInput, 'file'>>): FormData {
  const form = new FormData()
  if (input.title !== undefined) form.append('title', input.title)
  if (input.description !== undefined) form.append('description', input.description)
  if (input.category !== undefined) form.append('category', input.category)
  form.append('file', input.file)
  return form
}

export const documentsApi = {
  async list(params: DocumentPageParams): Promise<DocumentPage> {
    const { data } = await api.get<DocumentPage>('/api/documents', { params })
    return data
  },
  async get(id: string): Promise<DocumentDetails> {
    const { data } = await api.get<DocumentDetails>(`/api/documents/${id}`)
    return data
  },
  async create(input: DocumentInput, onUploadProgress?: (percent: number) => void): Promise<DocumentDetails> {
    const { data } = await api.post<DocumentDetails>('/api/documents', documentForm(input), {
      onUploadProgress: (event) => {
        if (event.total) onUploadProgress?.(Math.round((event.loaded * 100) / event.total))
      },
    })
    return data
  },
  async addVersion(id: string, file: File, onUploadProgress?: (percent: number) => void): Promise<DocumentDetails> {
    const { data } = await api.post<DocumentDetails>(`/api/documents/${id}/versions`, documentForm({ file }), {
      onUploadProgress: (event) => {
        if (event.total) onUploadProgress?.(Math.round((event.loaded * 100) / event.total))
      },
    })
    return data
  },
  async download(id: string, version: number): Promise<{ blob: Blob; fileName: string | null }> {
    const response = await api.get<Blob>(`/api/documents/${id}/versions/${version}/download`, { responseType: 'blob' })
    const disposition = response.headers['content-disposition'] as string | undefined
    const utf8Name = disposition?.match(/filename\*=UTF-8''([^;]+)/i)?.[1]
    const quotedName = disposition?.match(/filename="?([^";]+)"?/i)?.[1]
    return {
      blob: response.data,
      fileName: utf8Name ? decodeURIComponent(utf8Name) : quotedName ?? null,
    }
  },
}

export async function listAllDocuments(): Promise<DocumentPage['items']> {
  const pageSize = 100
  const firstPage = await documentsApi.list({ page: 1, pageSize })
  const items = [...firstPage.items]
  const pages = Math.ceil(firstPage.totalCount / pageSize)
  for (let page = 2; page <= pages; page += 1) {
    const result = await documentsApi.list({ page, pageSize })
    items.push(...result.items)
  }
  return items
}
