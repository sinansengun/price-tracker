import { useEffect, useMemo, useState } from 'react'

import { Product, UserProductAlertMode, updateAlertSettings } from '../api/api'

type Props = {
  product: Product
  onClose: () => void
  onSaved: () => void | Promise<void>
}

function modeCardClass(selected: boolean) {
  return selected
    ? 'border-brand-500 bg-brand-50 shadow-sm'
    : 'border-gray-200 bg-white hover:border-gray-300'
}

export default function AlertSettingsModal({ product, onClose, onSaved }: Props) {
  const [alertMode, setAlertMode] = useState<UserProductAlertMode>(product.alertMode ?? 'automatic')
  const [discountThresholdPercent, setDiscountThresholdPercent] = useState(
    product.discountThresholdPercent != null ? String(product.discountThresholdPercent).replace('.', ',') : ''
  )
  const [targetPrice, setTargetPrice] = useState(
    product.targetPrice != null ? String(product.targetPrice).replace('.', ',') : ''
  )
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    setAlertMode(product.alertMode ?? 'automatic')
    setDiscountThresholdPercent(
      product.discountThresholdPercent != null ? String(product.discountThresholdPercent).replace('.', ',') : ''
    )
    setTargetPrice(product.targetPrice != null ? String(product.targetPrice).replace('.', ',') : '')
    setError('')
  }, [product])

  const helperText = useMemo(() => {
    switch (alertMode) {
      case 'percentage':
        return 'Yüzde alarmı, son 7 günün en düşük fiyatını baz alır.'
      case 'target_price':
        return 'Fiyat belirlediğin tutara indiğinde ya da altına düştüğünde tetiklenir.'
      default:
        return 'Otomatik mod mevcut davranışı korur ve yeterli düşüşlerde bildirir.'
    }
  }, [alertMode])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')

    let parsedDiscountThreshold: number | null = null
    let parsedTargetPrice: number | null = null

    if (alertMode === 'percentage') {
      parsedDiscountThreshold = Number.parseFloat(discountThresholdPercent.replace(',', '.'))
      if (!Number.isFinite(parsedDiscountThreshold)) {
        setError('İndirim yüzdesi gerekli.')
        return
      }
      if (parsedDiscountThreshold <= 0 || parsedDiscountThreshold > 100) {
        setError('İndirim yüzdesi 0 ile 100 arasında olmalı.')
        return
      }
    }

    if (alertMode === 'target_price') {
      parsedTargetPrice = Number.parseFloat(targetPrice.replace(',', '.'))
      if (!Number.isFinite(parsedTargetPrice)) {
        setError('Hedef fiyat gerekli.')
        return
      }
      if (parsedTargetPrice <= 0) {
        setError('Hedef fiyat 0\'dan büyük olmalı.')
        return
      }
    }

    setSaving(true)

    try {
      await updateAlertSettings(product.id, {
        alertMode,
        discountThresholdPercent: parsedDiscountThreshold,
        targetPrice: parsedTargetPrice,
      })
      await onSaved()
      onClose()
    } catch (err: any) {
      setError(err?.response?.data?.error ?? 'Alarm ayarı güncellenemedi.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        className="bg-white rounded-2xl shadow-xl w-full max-w-xl mx-4 p-6"
        onClick={e => e.stopPropagation()}
      >
        <div className="mb-5">
          <h2 className="text-lg font-bold text-gray-900">Alarm Ayarı</h2>
          <p className="mt-1 text-sm text-gray-500">
            Yeni ürünler otomatik takip ile başlar. İstersen yüzde indirim ya da hedef fiyat alarmına geçebilirsin.
          </p>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <button
            type="button"
            onClick={() => setAlertMode('automatic')}
            className={`w-full rounded-xl border p-4 text-left transition-colors ${modeCardClass(alertMode === 'automatic')}`}
          >
            <div className="flex items-start gap-3">
              <span className="mt-0.5 text-brand-600">{alertMode === 'automatic' ? '◉' : '○'}</span>
              <div>
                <div className="text-sm font-semibold text-gray-900">Otomatik takip</div>
                <div className="mt-1 text-sm text-gray-500">Mevcut davranış: fiyat yeni 7 günlük dip seviyeye yeterince indiğinde bildir.</div>
              </div>
            </div>
          </button>

          <button
            type="button"
            onClick={() => setAlertMode('percentage')}
            className={`w-full rounded-xl border p-4 text-left transition-colors ${modeCardClass(alertMode === 'percentage')}`}
          >
            <div className="flex items-start gap-3">
              <span className="mt-0.5 text-brand-600">{alertMode === 'percentage' ? '◉' : '○'}</span>
              <div>
                <div className="text-sm font-semibold text-gray-900">Belirli indirim yüzdesi</div>
                <div className="mt-1 text-sm text-gray-500">7 günlük en düşük fiyata göre istediğin yüzde kadar düşünce bildir.</div>
              </div>
            </div>
          </button>

          {alertMode === 'percentage' && (
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">İndirim yüzdesi</label>
              <div className="relative">
                <input
                  value={discountThresholdPercent}
                  onChange={e => setDiscountThresholdPercent(e.target.value)}
                  placeholder="örn. 12"
                  type="number"
                  min="0"
                  max="100"
                  step="0.1"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 pr-10 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
                />
                <span className="absolute right-3 top-1/2 -translate-y-1/2 text-sm text-gray-400">%</span>
              </div>
            </div>
          )}

          <button
            type="button"
            onClick={() => setAlertMode('target_price')}
            className={`w-full rounded-xl border p-4 text-left transition-colors ${modeCardClass(alertMode === 'target_price')}`}
          >
            <div className="flex items-start gap-3">
              <span className="mt-0.5 text-brand-600">{alertMode === 'target_price' ? '◉' : '○'}</span>
              <div>
                <div className="text-sm font-semibold text-gray-900">Sabit hedef fiyat</div>
                <div className="mt-1 text-sm text-gray-500">Fiyat belirlediğin tutara indiğinde ya da altına düştüğünde bildir.</div>
              </div>
            </div>
          </button>

          {alertMode === 'target_price' && (
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">Hedef fiyat</label>
              <div className="relative">
                <input
                  value={targetPrice}
                  onChange={e => setTargetPrice(e.target.value)}
                  placeholder="örn. 4500"
                  type="number"
                  min="0"
                  step="0.01"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 pr-10 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
                />
                <span className="absolute right-3 top-1/2 -translate-y-1/2 text-sm text-gray-400">₺</span>
              </div>
            </div>
          )}

          <div className="rounded-xl bg-gray-50 px-3 py-2 text-xs text-gray-500">
            {helperText}
          </div>

          {error && <p className="text-sm text-red-600">{error}</p>}

          <div className="flex gap-3 pt-1">
            <button
              type="button"
              onClick={onClose}
              className="flex-1 rounded-lg border border-gray-300 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50"
            >
              İptal
            </button>
            <button
              type="submit"
              disabled={saving}
              className="flex-1 rounded-lg bg-brand-600 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700 disabled:opacity-60"
            >
              {saving ? 'Kaydediliyor...' : 'Kaydet'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}