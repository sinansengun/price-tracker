import { useEffect, useRef, useState } from 'react'
import { useParams, useNavigate, Link } from 'react-router-dom'

function StoreBadge({ store, url }: { store: string; url: string }) {
  const domain = (() => { try { return new URL(url).hostname } catch { return '' } })()
  const faviconUrl = domain ? `https://www.google.com/s2/favicons?domain=${domain}&sz=16` : ''
  return (
    <span className="inline-flex items-center gap-1.5 text-sm font-medium bg-brand-100 text-brand-700 px-2.5 py-1 rounded-full">
      {faviconUrl && <img src={faviconUrl} alt="" className="w-5 h-5 rounded-sm" />}
      {store}
    </span>
  )
}
import {
  LineChart, Line, ResponsiveContainer, Tooltip, YAxis
} from 'recharts'
import {
  getProduct, checkProduct, deleteProduct, getLabels, createLabel, addProductLabel, removeProductLabel,
  flattenProduct, UserProductResponse, Product, Label, getMissingPriceLabel
} from '../api/api'

// ── Helpers ───────────────────────────────────────────────────────────────
function fmt(price: number) {
  return price.toLocaleString('tr-TR', { minimumFractionDigits: 2 }) + ' ₺'
}

function PriceText({ price, className = '' }: { price: number; className?: string }) {
  const formatted = price.toLocaleString('tr-TR', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
  const [mainPart, decimalPart = '00'] = formatted.split(',')

  return (
    <span className={className}>
      <span>{mainPart}</span>
      <span className="ml-0.5 align-bottom text-[0.62em]">,{decimalPart} ₺</span>
    </span>
  )
}

function priceStateClass(product: Pick<Product, 'priceStatus'>) {
  return product.priceStatus === 'out_of_stock'
    ? 'bg-amber-100 text-amber-700'
    : 'bg-slate-100 text-slate-600'
}

function fmtDate(iso: string) {
  return new Date(iso).toLocaleString('tr-TR', {
    day: '2-digit', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

type DetailChartPoint = {
  dayKey: string
  v: number | null
  checkedAt?: string
}

function localDayKey(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

function buildLast30DaySeries(histories?: Product['priceHistories']): DetailChartPoint[] {
  const end = new Date()
  end.setHours(0, 0, 0, 0)
  const startMs = end.getTime() - 29 * 86_400_000
  const endExclusive = end.getTime() + 86_400_000

  const byDay = new Map<string, { price: number; checkedAt: string; t: number }>()

  for (const h of histories ?? []) {
    const t = new Date(h.checkedAt).getTime()
    if (Number.isNaN(t) || t < startMs || t >= endExclusive) continue
    const key = localDayKey(new Date(t))
    const prev = byDay.get(key)
    if (!prev || t > prev.t) {
      byDay.set(key, { price: h.price, checkedAt: h.checkedAt, t })
    }
  }

  const series: DetailChartPoint[] = []
  for (let i = 0; i < 30; i++) {
    const day = new Date(startMs + i * 86_400_000)
    const key = localDayKey(day)
    const found = byDay.get(key)
    series.push({
      dayKey: key,
      v: found?.price ?? null,
      checkedAt: found?.checkedAt,
    })
  }

  return series
}

function DetailMiniChart({ points }: { points: DetailChartPoint[] }) {
  const prices = points
    .map(p => p.v)
    .filter((v): v is number => v != null)

  if (prices.length === 0) return null

  const flat = Math.min(...prices) === Math.max(...prices)
  const color = flat ? '#94a3b8' : '#2563eb'
  const firstPrice = prices[0]
  const maxDeviation = prices.reduce((max, p) => Math.max(max, Math.abs(p - firstPrice)), 0)
  const basePadding = Math.max(firstPrice * 0.03, 1)
  const halfRange = Math.max(maxDeviation * 1.15, basePadding)
  const yMin = firstPrice - halfRange
  const yMax = firstPrice + halfRange

  return (
    <div className="w-full rounded-lg border border-dashed border-gray-300 bg-white/70 px-2 py-2">
      <ResponsiveContainer width="100%" height={108}>
        <LineChart data={points}>
          <YAxis hide domain={[yMin, yMax]} />
          <Line
            type="linear"
            dataKey="v"
            stroke={color}
            strokeWidth={1.8}
            isAnimationActive={false}
            connectNulls={true}
            dot={false}
            activeDot={false}
          />
          <Tooltip
            content={({ active, payload }) =>
              active && payload?.length && payload[0].value != null
                ? <div className="bg-white border border-gray-200 rounded px-2 py-1 text-xs shadow">{fmt(payload[0].value as number)}</div>
                : null
            }
          />
        </LineChart>
      </ResponsiveContainer>
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
  const [showActionMenu, setShowActionMenu] = useState(false)
  const [showLabelPanel, setShowLabelPanel] = useState(false)
  const [newLabelName, setNewLabelName]     = useState('')
  const [newLabelColor, setNewLabelColor]   = useState('#6366f1')
  const [labelSaving, setLabelSaving]       = useState(false)
  const actionMenuRef = useRef<HTMLDivElement>(null)
  const labelDropdownRef = useRef<HTMLDivElement>(null)

  // Close menus on outside click
  useEffect(() => {
    if (!showLabelPanel && !showActionMenu) return
    const handler = (e: MouseEvent) => {
      if (actionMenuRef.current && !actionMenuRef.current.contains(e.target as Node)) {
        setShowActionMenu(false)
      }
      if (labelDropdownRef.current && !labelDropdownRef.current.contains(e.target as Node))
        setShowLabelPanel(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [showLabelPanel, showActionMenu])

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

  const handleDelete = async () => {
    if (!confirm('Bu ürünü silmek istediğinize emin misiniz?')) return
    setDeleting(true)
    await deleteProduct(Number(id))
    navigate('/')
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
  const missingPriceLabel = getMissingPriceLabel(p)

  const detailChartPoints = buildLast30DaySeries(p?.priceHistories)
  const hasChartData = detailChartPoints.some(point => point.v != null)

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
        <div className="bg-white rounded-2xl border border-gray-100 shadow-sm p-6 flex flex-col sm:flex-row gap-6">
          <div className="shrink-0 w-full sm:w-64 h-72 sm:h-64 bg-gray-50 rounded-xl flex items-center justify-center overflow-hidden">
            {imgSrc ? (
              <img src={imgSrc} alt={p.name} className="w-full h-full object-contain p-4" />
            ) : (
              <span className="text-4xl">🛍️</span>
            )}
          </div>

          <div className="flex-1 min-w-0 space-y-3 sm:min-h-48 flex flex-col relative pr-10">
            <div className="absolute right-0 top-0" ref={actionMenuRef}>
              <button
                onClick={() => setShowActionMenu(v => !v)}
                className="h-8 w-8 rounded-lg border border-gray-200 text-gray-500 hover:text-gray-800 hover:bg-gray-100 transition-colors"
                title="Aksiyonlar"
              >
                ⋯
              </button>
              {showActionMenu && (
                <div className="absolute right-0 mt-1.5 z-50 w-48 bg-white border border-gray-200 rounded-lg shadow-lg py-1">
                  <button
                    onClick={() => { setShowActionMenu(false); handleCheck() }}
                    disabled={checking}
                    className="w-full px-3 py-2 text-left text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                  >
                    {checking ? 'Kontrol ediliyor...' : '🔄 Fiyatı Kontrol Et'}
                  </button>
                  <button
                    onClick={() => { setShowActionMenu(false); handleDelete() }}
                    disabled={deleting}
                    className="w-full px-3 py-2 text-left text-sm text-red-600 hover:bg-red-50 disabled:opacity-50"
                  >
                    {deleting ? 'Siliniyor...' : '🗑 Ürünü Sil'}
                  </button>
                </div>
              )}
            </div>

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
                <span className="text-2xl font-bold text-gray-900"><PriceText price={p.currentPrice} /></span>
              )}
              {p.currentPrice == null && missingPriceLabel && (
                <span className={`inline-flex items-center rounded-full px-3 py-1 text-sm font-semibold ${priceStateClass(p)}`}>
                  {missingPriceLabel}
                </span>
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

            <div className="flex flex-col items-start gap-1 text-xs text-gray-500">
              <span>
                Fiyat: <strong className="text-gray-700">{p.currentPrice != null ? <PriceText price={p.currentPrice} /> : (missingPriceLabel ?? '-')}</strong>
              </span>
              <span>
                Ekleme tarihi: <strong className="text-gray-700">{fmtDate(p.createdAt)}</strong>
              </span>
              <span>
                Hedef: <strong className="text-gray-700">{p.targetPrice != null ? <PriceText price={p.targetPrice} /> : '-'}</strong>
              </span>
            </div>

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

            {hasChartData && (
              <div className="mt-2 w-full sm:w-80 self-start">
                <div className="mb-1 flex items-center justify-between gap-2">
                  <h3 className="text-sm font-bold text-gray-900 text-left">Fiyat Geçmişi</h3>
                  <p className="text-[10px] font-semibold uppercase tracking-wide text-gray-400 text-right">Son 1 ay</p>
                </div>
                <DetailMiniChart points={detailChartPoints} />
              </div>
            )}
          </div>
        </div>

      </main>
    </div>
  )
}
