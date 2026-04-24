import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router-dom'

function StoreBadge({ store, url }: { store: string; url: string }) {
  const domain = (() => { try { return new URL(url).hostname } catch { return '' } })()
  const faviconUrl = domain ? `https://www.google.com/s2/favicons?domain=${domain}&sz=16` : ''
  return (
    <span className="inline-flex items-center gap-1 text-xs font-medium bg-brand-50 text-brand-700 border border-brand-200 px-2 py-0.5 rounded-full">
      {faviconUrl && <img src={faviconUrl} alt="" className="w-4 h-4 rounded-sm" />}
      {store}
    </span>
  )
}
import { LineChart, Line, ResponsiveContainer, Tooltip, YAxis } from 'recharts'
import {
  getProducts, getLabels, createProduct, addProductLabel, removeProductLabel, createLabel, deleteLabel,
  clearToken, flattenProduct,
  Product, Label, PriceHistory
} from '../api/api'

function AddProductModal({ onClose, onAdded }: { onClose: () => void; onAdded: () => void }) {
  const [url, setUrl]               = useState('')
  const [targetPrice, setTargetPrice] = useState('')
  const [loading, setLoading]       = useState(false)
  const [error, setError]           = useState('')
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => { inputRef.current?.focus() }, [])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!url.trim()) { setError('URL gereklidir.'); return }
    setError('')
    setLoading(true)
    try {
      const cleanUrl = url.trim()
        .replace(/\u200b|\u200c|\u200d|\ufeff/g, '')
        .replace(/&amp;/g, '&')
      const tp = targetPrice ? parseFloat(targetPrice.replace(',', '.')) : undefined
      await createProduct(cleanUrl, tp)
      onAdded()
      onClose()
    } catch (err: any) {
      setError(err?.response?.data?.error ?? 'Bir hata oluştu.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        className="bg-white rounded-2xl shadow-xl w-full max-w-lg mx-4 p-6"
        onClick={e => e.stopPropagation()}
      >
        <h2 className="text-lg font-bold text-gray-900 mb-5">Ürün Ekle</h2>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Ürün URL</label>
            <input
              ref={inputRef}
              value={url}
              onChange={e => setUrl(e.target.value)}
              placeholder="https://www.hepsiburada.com/..."
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Hedef Fiyat <span className="text-gray-400 font-normal">(opsiyonel)</span>
            </label>
            <div className="relative">
              <input
                value={targetPrice}
                onChange={e => setTargetPrice(e.target.value)}
                placeholder="örn. 4500"
                type="number"
                min="0"
                className="w-full border border-gray-300 rounded-lg px-3 py-2 pr-10 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
              />
              <span className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 text-sm">₺</span>
            </div>
          </div>
          {error && <p className="text-sm text-red-600">{error}</p>}
          <div className="flex gap-3 pt-1">
            <button
              type="button"
              onClick={onClose}
              className="flex-1 border border-gray-300 rounded-lg py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
            >
              İptal
            </button>
            <button
              type="submit"
              disabled={loading}
              className="flex-1 bg-brand-600 hover:bg-brand-700 text-white rounded-lg py-2 text-sm font-medium transition-colors disabled:opacity-60"
            >
              {loading ? 'Ekleniyor...' : 'Ekle'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

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

function fmtDate(iso: string, includeTime = false) {
  return new Date(iso).toLocaleString('tr-TR', {
    day: 'numeric', month: 'short', year: 'numeric',
    ...(includeTime ? { hour: '2-digit', minute: '2-digit' } : {}),
  })
}

type ChartPoint = {
  dayKey: string
  v: number | null
  checkedAt?: string
}

function localDayKey(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

function buildLast30DaySeries(histories?: PriceHistory[]): ChartPoint[] {
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

  const series: ChartPoint[] = []
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

function LabelDropdown({
  product, allLabels, onProductLabelsChange, onNewLabel
}: {
  product: Product
  allLabels: Label[]
  onProductLabelsChange: (productId: number, labels: Label[]) => void
  onNewLabel: (label: Label) => void
}) {
  const [open, setOpen]     = useState(false)
  const [search, setSearch] = useState('')
  const [color, setColor]   = useState('#6366f1')
  const [saving, setSaving] = useState(false)
  const [pos, setPos]       = useState({ top: 0, left: 0 })
  const btnRef = useRef<HTMLButtonElement>(null)
  const dropRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (
        dropRef.current && !dropRef.current.contains(e.target as Node) &&
        btnRef.current && !btnRef.current.contains(e.target as Node)
      ) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  const handleOpen = (e: React.MouseEvent) => {
    e.stopPropagation()
    if (btnRef.current) {
      const r = btnRef.current.getBoundingClientRect()
      setPos({ top: r.bottom + window.scrollY + 2, left: r.left + window.scrollX })
    }
    setOpen(v => !v)
  }

  const currentLabels = product.labels ?? []

  const handleToggle = async (label: Label) => {
    const has = currentLabels.some(l => l.id === label.id)
    try {
      if (has) {
        await removeProductLabel(product.id, label.id)
        onProductLabelsChange(product.id, currentLabels.filter(l => l.id !== label.id))
      } else {
        await addProductLabel(product.id, label.id)
        onProductLabelsChange(product.id, [...currentLabels, label])
      }
    } catch { /* ignore */ }
  }

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!search.trim()) return
    setSaving(true)
    try {
      const res = await createLabel(search.trim(), color)
      const created = res.data
      onNewLabel(created)
      await addProductLabel(product.id, created.id)
      onProductLabelsChange(product.id, [...currentLabels, created])
      setSearch('')
      setColor('#6366f1')
    } catch { /* ignore */ } finally {
      setSaving(false)
    }
  }

  const filtered = allLabels.filter(l =>
    !search.trim() || l.name.toLowerCase().includes(search.toLowerCase())
  )
  const canCreate = search.trim() && !allLabels.some(l => l.name.toLowerCase() === search.trim().toLowerCase())

  return (
    <>
      <button
        ref={btnRef}
        onClick={handleOpen}
        className="text-[10px] font-bold uppercase tracking-wide text-gray-400 bg-gray-100 hover:bg-gray-200 px-1.5 py-0.5 rounded transition-colors"
      >
        + Label
      </button>

      {open && createPortal(
        <div
          ref={dropRef}
          style={{ position: 'absolute', top: pos.top, left: pos.left }}
          className="z-[9999] w-60 bg-white border border-gray-200 rounded-lg shadow-xl"
          onClick={e => e.stopPropagation()}
        >
          <form onSubmit={handleCreate} className="flex items-center gap-1.5 p-2 border-b border-gray-100">
            <input
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Ara veya oluştur..."
              autoFocus
              className="flex-1 min-w-0 text-xs px-2 py-1.5 border border-gray-200 rounded focus:outline-none focus:ring-2 focus:ring-brand-400"
            />
            <input
              type="color"
              value={color}
              onChange={e => setColor(e.target.value)}
              className="w-7 h-7 rounded border border-gray-200 cursor-pointer p-0.5 shrink-0"
              title="Renk seç"
            />
            {canCreate && (
              <button
                type="submit"
                disabled={saving}
                className="text-xs bg-brand-600 text-white px-2 py-1.5 rounded hover:bg-brand-700 transition-colors disabled:opacity-40 font-medium shrink-0"
              >
                {saving ? '...' : 'Ekle'}
              </button>
            )}
          </form>

          <div className="max-h-48 overflow-y-auto py-1">
            {filtered.map(l => {
              const attached = currentLabels.some(pl => pl.id === l.id)
              return (
                <button
                  key={l.id}
                  onClick={() => handleToggle(l)}
                  className="w-full flex items-center gap-2 px-3 py-1.5 text-left hover:bg-gray-50 transition-colors"
                >
                  <span className="w-3 h-3 rounded-sm shrink-0" style={{ backgroundColor: l.color }} />
                  <span className="text-xs text-gray-700 flex-1 truncate">{l.name}</span>
                  {attached && <span className="text-brand-600 text-xs">✓</span>}
                </button>
              )
            })}
            {filtered.length === 0 && !canCreate && (
              <p className="px-3 py-2 text-xs text-gray-400">Eşleşen label yok</p>
            )}
          </div>
        </div>,
        document.body
      )}
    </>
  )
}

function MiniChart({ points }: { points: ChartPoint[] }) {
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
    <div className="w-full rounded-lg border border-dashed border-gray-300 bg-white/70 px-1 py-1">
      <ResponsiveContainer width="100%" height={48}>
        <LineChart data={points}>
          <YAxis hide domain={[yMin, yMax]} />
          <Line
            type="linear"
            dataKey="v"
            stroke={color}
            strokeWidth={1.8}
            isAnimationActive={false}
            connectNulls={false}
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

function ProductRow({
  product, allLabels, onProductLabelsChange, onNewLabel, onLabelClick
}: {
  product: Product
  allLabels: Label[]
  onProductLabelsChange: (productId: number, labels: Label[]) => void
  onNewLabel: (label: Label) => void
  onLabelClick: (labelId: number) => void
}) {
  const navigate = useNavigate()
  const imgSrc = product.imageUrl?.replace('{size}', '200')

  const histories = product.priceHistories
  const sorted = histories
    ? [...histories].sort((a, b) => new Date(a.checkedAt).getTime() - new Date(b.checkedAt).getTime())
    : []
  const monthSeries = buildLast30DaySeries(sorted)
  const monthWithPrice = monthSeries
    .filter((p): p is ChartPoint & { v: number; checkedAt: string } => p.v != null && !!p.checkedAt)
    .sort((a, b) => new Date(a.checkedAt).getTime() - new Date(b.checkedAt).getTime())
  const first = monthWithPrice[0]
  const last  = monthWithPrice[monthWithPrice.length - 1]

  let pct: number | null = null
  let pctUp = false
  if (first && last && first.v > 0) {
    pct   = ((last.v - first.v) / first.v) * 100
    pctUp = pct > 0
  }

  const periodLabel = 'Son 1 ay'

  return (
    <div
      className="bg-white border border-gray-200 rounded-xl overflow-hidden cursor-pointer hover:shadow-md hover:border-brand-200 transition-all duration-150 group"
      onClick={() => navigate(`/products/${product.id}`)}
    >
      <div className="flex">
        <div
          className="shrink-0 w-24 sm:w-40 h-24 sm:h-32 bg-white flex items-center justify-center p-2 border-r border-gray-100 overflow-hidden"
        >
          {imgSrc
            ? <img src={imgSrc} alt={product.name} className="block max-w-full max-h-full object-contain" />
            : <span className="text-4xl">🛍️</span>}
        </div>

        <div
          className="flex-1 min-w-0 px-3 sm:px-4 py-3 flex flex-col justify-between"
        >
          <p className="text-sm font-semibold text-gray-900 leading-snug line-clamp-2 group-hover:text-brand-700 transition-colors">
            {product.name || 'Yükleniyor...'}
          </p>
          {product.store && (
            <div className="mt-1">
              <StoreBadge store={product.store} url={product.url} />
            </div>
          )}

          <div className="mt-2 space-y-0.5">
            {product.currentPrice != null ? (
              <>
                <p className="text-xl font-bold text-gray-900"><PriceText price={product.currentPrice} /></p>
                {product.initialPrice != null && product.initialPrice !== product.currentPrice && (
                  <p className="text-xs text-gray-400 line-through"><PriceText price={product.initialPrice} /></p>
                )}
              </>
            ) : (
              <p className="text-sm text-gray-400">Fiyat bekleniyor</p>
            )}
          </div>

          <div className="mt-3 flex items-center gap-1.5 flex-wrap py-1.5" onClick={e => e.stopPropagation()}>
            {product.labels?.map(l => (
              <button
                key={l.id}
                onClick={e => { e.stopPropagation(); onLabelClick(l.id) }}
                className="text-[10px] font-bold uppercase tracking-wide px-1.5 py-0.5 rounded cursor-pointer hover:brightness-90 transition-all"
                style={{ backgroundColor: l.color + '1A', color: l.color }}
              >
                {l.name}
              </button>
            ))}
            <LabelDropdown
              product={product}
              allLabels={allLabels}
              onProductLabelsChange={onProductLabelsChange}
              onNewLabel={onNewLabel}
            />
            {product.targetPrice != null && (
              <span className="text-xs text-gray-400">🎯 Hedef: <PriceText price={product.targetPrice} /></span>
            )}
          </div>

          <p className="mt-1 text-xs text-gray-400 hidden sm:block">
            Eklendi: {fmtDate(product.createdAt)}
            {product.lastCheckedAt && <> &nbsp;·&nbsp; Güncellendi: {fmtDate(product.lastCheckedAt, true)}</>}
          </p>
        </div>

        <div
          className="hidden sm:flex shrink-0 w-52 border-l border-gray-100 bg-white flex-col items-center justify-between px-3 py-3 gap-1"
        >
          {pct !== null && (
            <div className={`flex items-center gap-1 text-xs font-bold ${pctUp ? 'text-red-500' : 'text-green-600'}`}>
              <span>{periodLabel}:</span>
              <span>{pctUp ? '▲' : '▼'} %{Math.abs(pct).toFixed(1)}</span>
            </div>
          )}

          <div className="w-full">
            <MiniChart points={monthSeries} />
          </div>

          {first && (
            <p className="text-[10px] text-gray-400 text-center leading-tight">
              {fmtDate(first.checkedAt)}<br />
              <span className="font-medium text-gray-600"><PriceText price={first.v} /></span>
            </p>
          )}

          <button
            className="mt-1 w-full text-xs text-brand-600 border border-brand-300 rounded-lg py-1 hover:bg-brand-50 transition-colors font-medium"
            onClick={e => { e.stopPropagation(); navigate(`/products/${product.id}`) }}
          >
            Detaya Git →
          </button>
        </div>
      </div>

      {/* Mobil grafik şeridi */}
      {monthWithPrice.length >= 1 && (
        <div className="sm:hidden border-t border-gray-100 bg-white px-3 pt-1 pb-2" onClick={e => e.stopPropagation()}>
          <div className="flex items-center justify-between mb-0.5">
            {pct !== null && (
              <span className={`text-[10px] font-bold ${pctUp ? 'text-red-500' : 'text-green-600'}`}>
                {pctUp ? '▲' : '▼'} %{Math.abs(pct).toFixed(1)} {periodLabel && `(${periodLabel})`}
              </span>
            )}
            {first && (
              <span className="text-[10px] text-gray-400">
                Başlangıç: <span className="font-medium text-gray-600"><PriceText price={first.v} /></span>
              </span>
            )}
          </div>
          <MiniChart points={monthSeries} />
        </div>
      )}
    </div>
  )
}

function ProductCard({ product }: { product: Product }) {
  const navigate = useNavigate()
  const imgSrc = product.imageUrl?.replace('{size}', '375')

  const initial = product.initialPrice
  const current = product.currentPrice
  let pct: number | null = null
  let pctUp = false
  if (initial && current && initial > 0) {
    pct   = ((current - initial) / initial) * 100
    pctUp = pct > 0
  }

  return (
    <div
      onClick={() => navigate(`/products/${product.id}`)}
      className="bg-white rounded-2xl shadow-sm border border-gray-100 overflow-hidden cursor-pointer hover:shadow-md hover:-translate-y-0.5 transition-all duration-150"
    >
      <div className="h-44 bg-white flex items-center justify-center overflow-hidden p-4">
        {imgSrc
          ? <img src={imgSrc} alt={product.name} className="block max-h-full max-w-full object-contain" />
          : <span className="text-4xl">🛍️</span>}
      </div>
      <div className="p-4 space-y-2">
        {product.labels && product.labels.length > 0 && (
          <div className="flex flex-wrap gap-1">
            {product.labels.map(l => (
              <span
                key={l.id}
                className="text-[10px] font-bold uppercase tracking-wide px-1.5 py-0.5 rounded"
                style={{ backgroundColor: l.color + '1A', color: l.color }}
              >
                {l.name}
              </span>
            ))}
          </div>
        )}
        <p className="text-sm font-semibold text-gray-900 leading-snug line-clamp-2">
          {product.name || 'Yükleniyor...'}
        </p>
        {product.store && <StoreBadge store={product.store} url={product.url} />}
        <div className="flex items-center gap-2 flex-wrap">
          {current != null
            ? <span className="text-lg font-bold text-gray-900"><PriceText price={current} /></span>
            : <span className="text-sm text-gray-400">Fiyat bekleniyor...</span>}
          {pct !== null && Math.abs(pct) >= 0.01 && (
            <span className={`inline-flex items-center gap-0.5 text-xs font-semibold px-1.5 py-0.5 rounded-full ${pctUp ? 'bg-red-100 text-red-600' : 'bg-green-100 text-green-600'}`}>
              {pctUp ? '▲' : '▼'} {Math.abs(pct).toFixed(1)}%
            </span>
          )}
        </div>
        {initial != null && initial !== current && (
          <p className="text-xs text-gray-400">Başlangıç: <PriceText price={initial} /></p>
        )}
        {product.targetPrice != null && (
          <p className="text-xs text-gray-500">🎯 Hedef: <PriceText price={product.targetPrice} /></p>
        )}
      </div>
    </div>
  )
}

type ViewMode = 'list' | 'grid'

export default function ProductsPage() {
  const [products, setProducts]       = useState<Product[]>([])
  const [labels, setLabels]           = useState<Label[]>([])
  const [activeLabel, setActiveLabel] = useState<number | null>(null)
  const [loading, setLoading]         = useState(true)
  const [showModal, setShowModal]     = useState(false)
  const [view, setView]               = useState<ViewMode>('list')

  const load = async () => {
    try {
      const [prodRes, lblRes] = await Promise.all([getProducts(), getLabels()])
      setProducts(prodRes.data.map(flattenProduct))
      setLabels(lblRes.data)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  const handleProductLabelsChange = (productId: number, newLabels: Label[]) => {
    setProducts(prev => prev.map(p => p.id === productId ? { ...p, labels: newLabels } : p))
  }

  const handleNewLabel = (label: Label) => {
    setLabels(prev => [...prev, label])
  }

  const navigate = useNavigate()

  const handleLogout = () => {
    clearToken()
    navigate('/login')
  }

  const filtered = activeLabel == null
    ? products
    : products.filter(p => p.labels?.some(l => l.id === activeLabel))

  return (
    <div className="min-h-screen">
      <header className="bg-white border-b border-gray-200 sticky top-0 z-10">
        <div className="max-w-5xl mx-auto px-3 sm:px-4 py-3 sm:py-4 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="text-2xl">💰</span>
            <h1 className="text-xl font-bold text-gray-900">Fiyat Takip</h1>
          </div>
          <div className="flex items-center gap-1.5 sm:gap-2">
            <div className="flex items-center border border-gray-200 rounded-xl overflow-hidden">
              <button
                onClick={() => setView('list')}
                title="Liste görünümü"
                className={`px-2.5 sm:px-3 py-2 text-sm transition-colors ${view === 'list' ? 'bg-brand-600 text-white' : 'text-gray-500 hover:bg-gray-50'}`}
              >☰</button>
              <button
                onClick={() => setView('grid')}
                title="Kart görünümü"
                className={`px-2.5 sm:px-3 py-2 text-sm transition-colors ${view === 'grid' ? 'bg-brand-600 text-white' : 'text-gray-500 hover:bg-gray-50'}`}
              >⊞</button>
            </div>
            <button
              onClick={handleLogout}
              className="hidden sm:block text-sm text-gray-500 hover:text-gray-800 px-3 py-2 rounded-xl hover:bg-gray-100 transition-colors"
              title="Çıkış Yap"
            >
              Çıkış
            </button>
            <button
              onClick={() => setShowModal(true)}
              className="bg-brand-600 hover:bg-brand-700 text-white text-sm font-medium px-3 sm:px-4 py-2 rounded-xl transition-colors"
            >
              <span className="hidden sm:inline">+ Ürün Ekle</span>
              <span className="sm:hidden font-bold text-base leading-none">+</span>
            </button>
          </div>
        </div>
      </header>

      <main className="max-w-5xl mx-auto px-3 sm:px-4 py-6 sm:py-8">
        {/* Label filter bar */}
        {!loading && labels.length > 0 && (
          <div className="flex items-center gap-2 flex-wrap mb-6">
            <button
              onClick={() => setActiveLabel(null)}
              className={`text-[10px] font-bold uppercase tracking-wide px-2.5 py-1 rounded transition-colors ${
                activeLabel === null
                  ? 'bg-gray-900 text-white'
                  : 'bg-gray-100 text-gray-500 hover:bg-gray-200'
              }`}
            >
              Tümü
            </button>
            {labels.map(l => (
              <button
                key={l.id}
                onClick={() => setActiveLabel(activeLabel === l.id ? null : l.id)}
                className={`text-[10px] font-bold uppercase tracking-wide px-2.5 py-1 rounded transition-all ${
                  activeLabel === l.id
                    ? 'ring-2 ring-offset-1'
                    : 'hover:brightness-90'
                }`}
                style={{
                  backgroundColor: l.color + (activeLabel === l.id ? '33' : '1A'),
                  color: l.color,
                  ...(activeLabel === l.id ? { ringColor: l.color } as any : {})
                }}
              >
                {l.name}
              </button>
            ))}
          </div>
        )}

        {loading ? (
          <div className="flex justify-center items-center py-24">
            <div className="w-8 h-8 border-4 border-brand-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : products.length === 0 ? (
          <div className="text-center py-24 space-y-3">
            <p className="text-5xl">🔍</p>
            <p className="text-gray-500 font-medium">Henüz takip edilen ürün yok</p>
            <button onClick={() => setShowModal(true)} className="mt-2 text-brand-600 hover:underline text-sm font-medium">
              İlk ürünü ekle →
            </button>
          </div>
        ) : filtered.length === 0 ? (
          <div className="text-center py-24 space-y-2">
            <p className="text-4xl">🏷️</p>
            <p className="text-gray-500 font-medium">Bu label ile eşleşen ürün yok</p>
            <button
              onClick={async () => {
                if (activeLabel == null) return
                await deleteLabel(activeLabel)
                setLabels(prev => prev.filter(l => l.id !== activeLabel))
                setActiveLabel(null)
              }}
              className="mt-1 text-xs text-red-500 hover:text-red-700 hover:underline font-medium transition-colors"
            >
              Label'ı sil
            </button>
          </div>
        ) : view === 'list' ? (
          <div className="space-y-2">
            {filtered.map(p => (
              <ProductRow
                key={p.id}
                product={p}
                allLabels={labels}
                onProductLabelsChange={handleProductLabelsChange}
                onNewLabel={handleNewLabel}
                onLabelClick={setActiveLabel}
              />
            ))}
          </div>
        ) : (
          <div className="grid grid-cols-1 min-[400px]:grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3 sm:gap-4">
            {filtered.map(p => <ProductCard key={p.id} product={p} />)}
          </div>
        )}
      </main>

      {showModal && (
        <AddProductModal onClose={() => setShowModal(false)} onAdded={load} />
      )}
    </div>
  )
}
