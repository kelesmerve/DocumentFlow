import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return <section className="not-found"><p className="eyebrow">404 · BULUNAMADI</p><h1>Bu sayfa mevcut değil</h1><p>Adres değişmiş veya kaldırılmış olabilir.</p><Link className="button button-secondary" to="/">Genel bakışa dön</Link></section>
}
