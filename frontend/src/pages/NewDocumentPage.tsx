import { useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, LoaderCircle, Upload } from 'lucide-react'
import { Link, useNavigate } from 'react-router-dom'
import { documentsApi } from '../api/documents'
import { apiErrorMessage } from '../api/client'
import { FilePicker } from '../components/FilePicker'
import { PageHeading } from '../components/Ui'

export function NewDocumentPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [title, setTitle] = useState('')
  const [category, setCategory] = useState('')
  const [description, setDescription] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState('')
  const [uploadProgress, setUploadProgress] = useState<number | null>(null)
  const createDocument = useMutation({
    mutationFn: () => {
      if (!file) throw new Error('Yüklemek için bir dosya seçin.')
      return documentsApi.create({ title: title.trim(), category: category.trim(), description: description.trim(), file }, setUploadProgress)
    },
    onSuccess: async (created) => {
      await queryClient.invalidateQueries({ queryKey: ['documents'] })
      navigate(`/documents/${created.document.id}`)
    },
  })

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFileError(file ? '' : 'Bir dosya seçmeniz gerekiyor.')
    if (!file) return
    createDocument.mutate()
  }

  return <>
    <Link className="back-link" to="/documents"><ArrowLeft size={16} />Belgelerim</Link>
    <PageHeading title="Yeni belge" description="Belge bilgilerini tamamlayın ve ilk dosya sürümünü yükleyin." />
    <form className="document-form" onSubmit={handleSubmit}>
      <div className="form-section-heading"><span className="section-index">01</span><div><h2>Belge bilgileri</h2><p>Başlık ve kategori, belgenizi bulmayı kolaylaştırır.</p></div></div>
      <div className="form-grid">
        <div className="field"><label htmlFor="title">Başlık <span aria-hidden="true">*</span></label><input id="title" name="title" maxLength={200} value={title} onChange={(event) => setTitle(event.target.value)} placeholder="Örn. Tedarikçi sözleşmesi" required /></div>
        <div className="field"><label htmlFor="category">Kategori <span aria-hidden="true">*</span></label><input id="category" name="category" maxLength={100} value={category} onChange={(event) => setCategory(event.target.value)} placeholder="Örn. Hukuk, İnsan Kaynakları" required /></div>
        <div className="field field-full"><label htmlFor="description">Açıklama <span className="optional-label">İsteğe bağlı</span></label><textarea id="description" name="description" maxLength={2000} rows={4} value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Belge hakkında kısa bir açıklama" /></div>
      </div>
      <div className="form-section-heading upload-section-heading"><span className="section-index">02</span><div><h2>İlk dosya sürümü</h2><p>Dosyanız güvenli biçimde saklanır ve SHA-256 özeti kaydedilir.</p></div></div>
      <div className="field"><label htmlFor="file">Dosya <span aria-hidden="true">*</span></label><FilePicker file={file} onChange={(next) => { setFile(next); setFileError('') }} error={fileError} disabled={createDocument.isPending} /></div>
      {createDocument.isError && <div className="form-error" role="alert">{apiErrorMessage(createDocument.error, 'Belge oluşturulamadı. Bilgilerinizi ve dosyanızı kontrol edin.')}</div>}
      {createDocument.isPending && <div className="upload-progress" role="status"><div className="progress-copy"><span>{uploadProgress === null ? 'Dosya hazırlanıyor…' : 'Belge yükleniyor'}</span>{uploadProgress !== null && <span>%{uploadProgress}</span>}</div><div className="progress-track"><span style={{ width: `${uploadProgress ?? 8}%` }} /></div></div>}
      <div className="form-actions"><Link className="button button-quiet" to="/documents">Vazgeç</Link><button className="button button-primary" type="submit" disabled={createDocument.isPending}>{createDocument.isPending ? <LoaderCircle size={16} className="spin" /> : <Upload size={16} />}{createDocument.isPending ? 'Yükleniyor…' : 'Belgeyi oluştur'}</button></div>
    </form>
  </>
}
