import { useEffect, useRef, useState } from 'react'
import { useParams, useNavigate, Link } from 'react-router-dom'

function StoreBadge({ store, url }: { store: string; url: string }) {
  const domain = (() => { try { return new URL(url).hostname } catch { return '' } })()
  const faviconUrl = domain ? `https://www.google.com/s2/favicons?domain=${domain}&sz=16` : ''
  return (
    <span className="inline-flex items-center gap-1 text-xs font-medium bg-brand-100 text-brand-700 px-2 py-0.5 rounded-full">
      {faviconUrl && <img src={faviconUrl} alt="" className="w-4 h-4 rounded-sm" />}
      {store}
    </span>
  )
}
import {
  LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid, ResponsiveContainer
} from 'recharts'
import {
  getProduct, checkProduct, deleteProduct, getLabels, createLabel, addProductLabel, removeProductLabel,
  flattenProduct, UserProductResponse, Product, Label
} from '../api/api'

// ── Helpers ───────────────────────────────────────────────────────────────
function fmt(price: number) {
  return price.toLocaleString('tr-TR', { minimumFractionDigits: 2 }) + ' ₺'
}

function fmtDate(iso: string) {
  return new Date(iso).toLocaleString('tr-TR', {
    day: '2-digit', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

function fmtDateShort(iso: string) {
  return new Date(iso).toLocaleDateString('tr-TR', {
    day: '2-digit', month: 'short',
  })
}

// ── Custom chart tooltip ──────────────────────────────────────────────────
function CustomTooltip({ active, payload, label }: any) {
  if (!active || !payload?.length) return null
  return (
    <div className="bg-white border border-gray-200 rounded-xl shadow-md px-3 py-2 text-sm">
      <p className="text-gray-500 text-xs mb-1">{label}</p>
      <p className="font-bold text-gray-900">{fmt(payload[0].value)}</p>
    </div>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────
export default function ProductDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [product, setProduct]   = useState<UserProductResponse | null>(null)
  const [flatProduct, setFlatProduct] = useState<Product | null>(null)
  const [allLabels, setAllLabels] = useState<Label[]>([])
  const [loading, setLoading]   = useState(true)
  const [checking, setChecking] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [error, setError]       = useState('')
  const [showLabelPanel, setShowLabelPanel] = useState(false)
  const [newLabelName, setNewLabelName]     = useState('')
  const [newLabelColor, setNewLabelColor]   = useState('#6366f1')
  const [labelSaving, setLabelSaving]       = useState(false)
  const labelDropdownRef = useRef<HTMLDivElement>(null)

  // Close dropdown on outside click
  useEffect(() => {
    if (!showLabelPanel) return
    const handler = (e: MouseEvent) => {
      if (labelDropdownRef.current && !labelDropdownRef.current.contains(e.target as Node))
        setShowLabelPanel(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [showLabelPanel])

  const load = async () => {
    try {
      const [prodRes, lblRes] = await Promise.all([getProduct(Number(id)), getLabels()])
      setProduct(prodRes.data)
      setFlatProduct(flattenProduct(prodRes.data))
      setAllLabels(lblRes.data)
    } catch {
      setError('Ürün bulunamadı.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [id])

  const handleCheck = async () => {
    setChecking(true)
    try {
      await checkProduct(Number(id))
      setTimeout(() => { load(); setChecking(false) }, 15000)
    } catch {
      setChecking(false)
    }
  }

  const handleToggleLabel = async (label: Label) => {
    if (!product) return
    const has = (product as any).labels?.some((l: Label) => l.id === label.id)
    try {
      if (has) {
        await removeProductLabel(product.id, label.id)
        setProduct(p => p ? { ...p, labels: (p.labels ?? []).filter(l => l.id !== label.id) } as any : p)
        setFlatProduct(p => p ? { ...p, labels: (p.labels ?? []).filter(l => l.id !== label.id) } : p)
      } else {
        await addProductLabel(product.id, label.id)
        setProduct(p => p ? { ...p, labels: [...(p.labels ?? []), label] } as any : p)
        setFlatProduct(p => p ? { ...p, labels: [...(p.labels ?? []), label] } : p)
      }
    } catch { /* ignore */ }
  }

  const handleCreateLabel = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!newLabelName.trim()) return
    setLabelSaving(true)
    try {
      const res = await createLabel(newLabelName.trim(), newLabelColor)
      const created = res.data
      setAllLabels(prev => [...prev, created])
      setNewLabelName('')
      setNewLabelColor('#6366f1')
      // Also attach to current product
      if (product) {
        await addProductLabel(product.id, created.id)
        setProduct(p => p ? { ...p, labels: [...(p.labels ?? []), created] } as any : p)
        setFlatProduct(p => p ? { ...p, labels: [...(p.labels ?? []), created] } : p)
      }
    } catch { /* ignore */ } finally {
      setLabelSaving(false)
    }
  }

  if (loading) {
    return (
      <div className="flex justify-center items-center min-h-screen">
        <div className="w-8 h-8 border-4 border-brand-500 border-t-transparent rounded-full animate-spin" />
      </div>
    )
  }

  if (error || !product || !flatProduct) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen gap-3">
        <p className="text-gray-500">{error || 'Ürün bulunamadı.'}</p>
        <Link to="/" className="text-brand-600 hover:underline text-sm">← Geri dön</Link>
      </div>
    )
  }

  const p = flatProduct
  const imgSrc = p.imageUrl?.replace('{size}', '375')

  const priceChange = p.initialPrice && p.currentPrice
    ? ((p.currentPrice - p.initialPrice) / p.initialPrice) * 100
    : null

  const chartData = [...(p?.priceHistories ?? [])]
    .sort((a, b) => new Date(a.checkedAt).getTime() - new Date(b.checkedAt).getTime())
    .map(h => ({ date: fmtDateShort(h.checkedAt), price: h.price, full: h.checkedAt }))

  const minPrice = Math.min(...chartData.map(d => d.price))
  const maxPrice = Math.max(...chartData.map(d => d.price))
  const padding  = (maxPrice - minPrice) * 0.1 || 100

  return (
    <div className="min-h-screen">
      {/* Header */}
      <header className="bg-white border-b border-gray-200 sticky top-0 z-10">
        <div className="max-w-4xl mx-auto px-4 py-4 flex items-center gap-3">
          <button
            onClick={() => navigate(-1)}
            className="text-gray-500 hover:text-gray-900 transition-colors p-1 -ml-1 rounded-lg hover:bg-gray-100"
          >
            ←
          </button>
          <h1 className="text-lg font-bold text-gray-900 truncate">{p.name}</h1>
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-8 space-y-6">

        {/* Product info */}
        <div className="bg-white rounded-2xl border border-gray-100 shadow-sm p-6 flex gap-6">
          <div className="shrink-0 w-32 h-32 bg-gray-50 rounded-xl flex items-center justify-center overflow-hidden">
            {imgSrc ? (
              <img src={imgSrc} alt={p.name} className="w-full h-full object-contain p-2" />
            ) : (
              <span className="text-4xl">🛍️</span>
            )}
          </div>

          <div className="flex-1 min-w-0 space-y-3">
            <div className="flex items-start gap-2 flex-wrap">
              {p.store && <StoreBadge store={p.store} url={p.url} />}
              <a
                href={p.url}
                target="_blank"
                rel="noopener noreferrer"
                className="text-xs text-gray-400 hover:text-brand-600 transition-colors truncate max-w-xs"
              >
                Ürün sayfası →
              </a>
            </div>

            <p className="text-base font-semibold text-gray-900 leading-snug">{p.name}</p>

            {/* Prices */}
            <div className="flex items-center gap-3 flex-wrap">
              {p.currentPrice != null && (
                <span className="text-2xl font-bold text-gray-900">{fmt(p.currentPrice)}</span>
              )}
              {priceChange !== null && Math.abs(priceChange) >= 0.01 && (
                <span
                  className={`inline-flex items-center gap-0.5 text-sm font-semibold px-2 py-0.5 rounded-full ${
                    priceChange > 0 ? 'bg-red-100 text-red-600' : 'bg-green-100 text-green-600'
                  }`}
                >
                  {priceChange > 0 ? '▲' : '▼'} {Math.abs(priceChange).toFixed(1)}%
                </span>
              )}
            </div>

            <div className="flex gap-6 text-xs text-gray-500 flex-wrap">
              {p.initialPrice != null && (
                <span>Başlangıç: <strong className="text-gray-700">{fmt(p.initialPrice)}</strong></span>
              )}
              {p.targetPrice != null && (
                <span>🎯 Hedef: <strong className="text-gray-700">{fmt(p.targetPrice)}</strong></span>
              )}
              {p.lastCheckedAt && (
                <span>Son kontrol: <strong className="text-gray-700">{fmtDate(p.lastCheckedAt)}</strong></span>
              )}
            </div>

            <button
              onClick={handleCheck}
              disabled={checking}
              className="inline-flex items-center gap-2 bg-brand-600 hover:bg-brand-700 text-white text-sm font-medium px-4 py-2 rounded-xl transition-colors disabled:opacity-60"
            >
              {checking ? (
                <>
                  <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
                  Kontrol ediliyor...
                </>
              ) : '🔄 Fiyatı Kontrol Et'}
            </button>

            <button
              onClick={async () => {
                if (!confirm('Bu ürünü silmek istediğinize emin misiniz?')) return
                setDeleting(true)
                await deleteProduct(Number(id))
                navigate('/')
              }}
              disabled={deleting}
              className="inline-flex items-center gap-2 bg-red-50 hover:bg-red-100 text-red-600 text-sm font-medium px-4 py-2 rounded-xl transition-colors disabled:opacity-60"
            >
              {deleting ? 'Siliniyor...' : '🗑 Ürünü Sil'}
            </button>

            {/* Labels */}
            <div className="pt-1">
              <div className="flex items-center gap-1.5 flex-wrap relative">
                {(p.labels ?? []).map(l => (
                  <span
                    key={l.id}
                    className="inline-flex items-center gap-1 text-[10px] font-bold uppercase tracking-wide px-2 py-1 rounded"
                    style={{ backgroundColor: l.color + '1A', color: l.color }}
                  >
                    {l.name}
                    <button
                      onClick={() => handleToggleLabel(l)}
                      className="ml-0.5 opacity-60 hover:opacity-100 transition-opacity leading-none text-xs"
                      style={{ color: l.color }}
                      title="Label'ı kaldır"
                    >×</button>
                  </span>
                ))}

                {/* Inline "+ Label" trigger */}
                <div className="relative" ref={labelDropdownRef}>
                  <button
                    onClick={() => setShowLabelPanel(v => !v)}
                    className="text-[10px] font-bold uppercase tracking-wide text-gray-400 bg-gray-100 hover:bg-gray-200 px-2 py-1 rounded transition-colors"
                  >
                    + Label
                  </button>

                  {/* Dropdown popover — JIRA style inline */}
                  {showLabelPanel && (
                    <div className="absolute left-0 top-full mt-1.5 z-50 w-64 bg-white border border-gray-200 rounded-lg shadow-lg">
                      {/* Search / create input */}
                      <form onSubmit={handleCreateLabel} className="flex items-center gap-1.5 p-2 border-b border-gray-100">
                        <input
                          value={newLabelName}
                          onChange={e => setNewLabelName(e.target.value)}
                          placeholder="Label ara veya oluştur..."
                          autoFocus
                          className="flex-1 min-w-0 text-xs px-2 py-1.5 border border-gray-200 rounded focus:outline-none focus:ring-2 focus:ring-brand-400"
                        />
                        <input
                          type="color"
                          value={newLabelColor}
                          onChange={e => setNewLabelColor(e.target.value)}
                          className="w-7 h-7 rounded border border-gray-200 cursor-pointer p-0.5 shrink-0"
                          title="Renk seç"
                        />
                        <button
                          type="submit"
                          disabled={labelSaving || !newLabelName.trim()}
                          className="text-xs bg-brand-600 text-white px-2.5 py-1.5 rounded hover:bg-brand-700 transition-colors disabled:opacity-40 font-medium shrink-0"
                        >
                          {labelSaving ? '...' : 'Ekle'}
                        </button>
                      </form>

                      {/* Existing labels list */}
                      {allLabels.length > 0 && (
                        <div className="max-h-48 overflow-y-auto py-1">
                          {allLabels
                            .filter(l => !newLabelName.trim() || l.name.toLowerCase().includes(newLabelName.toLowerCase()))
                            .map(l => {
                              const attached = flatProduct?.labels?.some((pl: Label) => pl.id === l.id)
                              return (
                                <button
                                  key={l.id}
                                  onClick={() => handleToggleLabel(l)}
                                  className="w-full flex items-center gap-2 px-3 py-1.5 text-left hover:bg-gray-50 transition-colors"
                                >
                                  <span
                                    className="w-3 h-3 rounded-sm shrink-0"
                                    style={{ backgroundColor: l.color }}
                                  />
                                  <span className="text-xs text-gray-700 flex-1 truncate">{l.name}</span>
                                  {attached && (
                                    <span className="text-brand-600 text-xs">✓</span>
                                  )}
                                </button>
                              )
                            })}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Price chart */}
        {chartData.length > 1 && (
          <div className="bg-white rounded-2xl border border-gray-100 shadow-sm p-6">
            <h2 className="text-base font-bold text-gray-900 mb-4">Fiyat Geçmişi</h2>
            <ResponsiveContainer width="100%" height={220}>
              <LineChart data={chartData} margin={{ top: 5, right: 10, left: 10, bottom: 5 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
                <XAxis
                  dataKey="date"
                  tick={{ fontSize: 11, fill: '#9ca3af' }}
                  tickLine={false}
                  axisLine={false}
                />
                <YAxis
                  domain={[minPrice - padding, maxPrice + padding]}
                  tick={{ fontSize: 11, fill: '#9ca3af' }}
                  tickLine={false}
                  axisLine={false}
                  tickFormatter={v => v.toLocaleString('tr-TR')}
                  width={70}
                />
                <Tooltip content={<CustomTooltip />} />
                <Line
                  type="monotone"
                  dataKey="price"
                  stroke="#2563eb"
                  strokeWidth={2}
                  dot={{ fill: '#2563eb', r: 3 }}
                  activeDot={{ r: 5 }}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        )}

        {/* Price history table */}
        {(p?.priceHistories ?? []).length > 0 && (
          <div className="bg-white rounded-2xl border border-gray-100 shadow-sm overflow-hidden">
            <div className="px-6 py-4 border-b border-gray-100">
              <h2 className="text-base font-bold text-gray-900">Kontrol Kayıtları</h2>
            </div>
            <div className="divide-y divide-gray-50">
              {[...(p?.priceHistories ?? [])]
                .sort((a, b) => new Date(b.checkedAt).getTime() - new Date(a.checkedAt).getTime())
                .map((h, i) => (
                  <div key={i} className="flex items-center justify-between px-6 py-3">
                    <span className="text-sm text-gray-500">{fmtDate(h.checkedAt)}</span>
                    <span className="text-sm font-semibold text-gray-900">{fmt(h.price)}</span>
                  </div>
                ))}
            </div>
          </div>
        )}

      </main>
    </div>
  )
}
