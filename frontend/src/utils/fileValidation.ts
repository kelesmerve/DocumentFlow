const maxSize = 10 * 1024 * 1024
const mimeTypes: Record<string, string> = {
  pdf: 'application/pdf',
  docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
}

export function validateDocumentFile(file: File): string | null {
  const extension = file.name.split('.').pop()?.toLowerCase()
  if (!extension || !(extension in mimeTypes)) return 'Yalnızca PDF veya DOCX dosyaları desteklenir.'
  if (file.size === 0) return 'Boş bir dosya yükleyemezsiniz.'
  if (file.size > maxSize) return 'Dosya boyutu 10 MB sınırını aşamaz.'
  if (file.type && file.type !== mimeTypes[extension]) return 'Dosya uzantısı ile içerik türü eşleşmiyor.'
  return null
}
