import { FileUp, X } from 'lucide-react'
import { useState } from 'react'
import { formatFileSize } from '../utils/format'
import { validateDocumentFile } from '../utils/fileValidation'

export function FilePicker({ file, onChange, error, disabled = false, id = 'file' }: {
  file: File | null
  onChange: (file: File | null) => void
  error?: string
  disabled?: boolean
  id?: string
}) {
  const [selectionError, setSelectionError] = useState('')

  function chooseFile(next: File | undefined) {
    if (!next) return
    const validationError = validateDocumentFile(next)
    setSelectionError(validationError ?? '')
    if (!validationError) onChange(next)
  }

  return <div className="file-picker-block">
    {file ? <div className="selected-file"><span className="file-icon"><FileUp size={18} /></span><div className="selected-file-copy"><strong>{file.name}</strong><span>{formatFileSize(file.size)}</span></div><button type="button" className="icon-button" onClick={() => onChange(null)} disabled={disabled} aria-label="Seçili dosyayı kaldır"><X size={17} /></button></div> :
      <label className={`file-dropzone${disabled ? ' is-disabled' : ''}`} htmlFor={id}>
        <span className="file-drop-icon"><FileUp size={19} /></span><span><strong>Dosya seçin</strong><small>PDF veya DOCX · En fazla 10 MB</small></span>
        <input id={id} type="file" accept=".pdf,.docx,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document" onChange={(event) => chooseFile(event.target.files?.[0])} disabled={disabled} />
      </label>}
    {(selectionError || error) && <p className="field-error" role="alert">{selectionError || error}</p>}
  </div>
}
