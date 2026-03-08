import { useEffect, useState } from 'react'
import { useParams, useNavigate, Link } from 'react-router-dom'
import {
  LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid, ResponsiveContainer
} from 'recharts'
import { getProduct, checkProduct, ProductDetail } from '../api/api'

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
  const [product, setProduct] = useState<ProductDetail | null>(null)
  const [loading, setLoading]   = useState(true)
  const [checking, setChecking] = useState(false)
  const [error, setError]       = useState('')

  const load = async () => {
    try {
      const res = await getProduct(Number(id))
      setProduct(res.data)
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
      // Fiyat kontrolü arka planda yapılıyor, 15sn bekleyip yenile
      setTimeout(() => { load(); setChecking(false) }, 15000)
    } catch {
      setChecking(false)
    }
  }

  if (loading) {
    return (
      <div className="flex justify-center items-center min-h-screen">
        <div className="w-8 h-8 border-4 border-brand-500 border-t-transparent rounded-full animate-spin" />
      </div>
    )
  }

  if (error || !product) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen gap-3">
        <p className="text-gray-500">{error || 'Ürün bulunamadı.'}</p>
        <Link to="/" className="text-brand-600 hover:underline text-sm">← Geri dön</Link>
      </div>
    )
  }

  const imgSrc = product.imageUrl?.replace('{size}', '375')

  const priceChange = product.initialPrice && product.currentPrice
    ? ((product.currentPrice - product.initialPrice) / product.initialPrice) * 100
    : null

  const chartData = [...product.priceHistories]
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
          <h1 className="text-lg font-bold text-gray-900 truncate">{product.name}</h1>
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-8 space-y-6">

        {/* Product info */}
        <div className="bg-white rounded-2xl border border-gray-100 shadow-sm p-6 flex gap-6">
          <div className="shrink-0 w-32 h-32 bg-gray-50 rounded-xl flex items-center justify-center overflow-hidden">
            {imgSrc ? (
              <img src={imgSrc} alt={product.name} className="w-full h-full object-contain p-2" />
            ) : (
              <span className="text-4xl">🛍️</span>
            )}
          </div>

          <div className="flex-1 min-w-0 space-y-3">
            <div className="flex items-start gap-2 flex-wrap">
              {product.store && (
                <span className="text-xs font-medium bg-brand-100 text-brand-700 px-2 py-0.5 rounded-full">
                  {product.store}
                </span>
              )}
              <a
                href={product.url}
                target="_blank"
                rel="noopener noreferrer"
                className="text-xs text-gray-400 hover:text-brand-600 transition-colors truncate max-w-xs"
              >
                Ürün sayfası →
              </a>
            </div>

            <p className="text-base font-semibold text-gray-900 leading-snug">{product.name}</p>

            {/* Prices */}
            <div className="flex items-center gap-3 flex-wrap">
              {product.currentPrice != null && (
                <span className="text-2xl font-bold text-gray-900">{fmt(product.currentPrice)}</span>
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
              {product.initialPrice != null && (
                <span>Başlangıç: <strong className="text-gray-700">{fmt(product.initialPrice)}</strong></span>
              )}
              {product.targetPrice != null && (
                <span>🎯 Hedef: <strong className="text-gray-700">{fmt(product.targetPrice)}</strong></span>
              )}
              {product.lastCheckedAt && (
                <span>Son kontrol: <strong className="text-gray-700">{fmtDate(product.lastCheckedAt)}</strong></span>
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
        {product.priceHistories.length > 0 && (
          <div className="bg-white rounded-2xl border border-gray-100 shadow-sm overflow-hidden">
            <div className="px-6 py-4 border-b border-gray-100">
              <h2 className="text-base font-bold text-gray-900">Kontrol Kayıtları</h2>
            </div>
            <div className="divide-y divide-gray-50">
              {[...product.priceHistories]
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
