import axios from 'axios'

const http = axios.create({ baseURL: import.meta.env.VITE_API_URL ?? '/api' })

// JWT token yönetimi
export const getToken  = () => localStorage.getItem('token')
export const setToken  = (t: string) => localStorage.setItem('token', t)
export const clearToken = () => localStorage.removeItem('token')

// Her istekte Authorization header ekle
http.interceptors.request.use(config => {
  const token = getToken()
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// 401 gelince login'e yönlendir
http.interceptors.response.use(
  r => r,
  err => {
    if (err.response?.status === 401) {
      clearToken()
      window.location.href = '/login'
    }
    return Promise.reject(err)
  }
)

// ── Auth ──────────────────────────────────────────────────────────────────
export const login    = (email: string, password: string) =>
  http.post<{ token: string }>('/auth/login', { email, password })
export const register = (email: string, password: string) =>
  http.post<{ token: string }>('/auth/register', { email, password })

export interface PriceHistory {
  price: number
  checkedAt: string
}

export type ProductPriceStatus = 'available' | 'out_of_stock' | 'price_not_found'
export type UserProductAlertMode = 'automatic' | 'percentage' | 'target_price'

export interface Label {
  id: number
  name: string
  color: string
}

// Ham API yanıtı (UserProduct nested yapısı)
export interface UserProductResponse {
  id: number
  alertMode?: UserProductAlertMode
  discountThresholdPercent?: number
  targetPrice?: number
  addedAt: string
  product: {
    id: number
    name: string
    url: string
    imageUrl?: string
    store?: string
    initialPrice?: number
    currentPrice?: number
    priceStatus?: ProductPriceStatus
    lastCheckedAt?: string
    createdAt: string
    priceHistories?: PriceHistory[]
  }
  labels?: Label[]
}

// UI'da kullanılan flat yapı (flatten() ile dönüştürülür)
export interface Product {
  id: number          // UserProduct.Id
  alertMode: UserProductAlertMode
  discountThresholdPercent?: number
  name: string
  url: string
  imageUrl?: string
  store?: string
  initialPrice?: number
  currentPrice?: number
  priceStatus?: ProductPriceStatus
  targetPrice?: number
  lastCheckedAt?: string
  createdAt: string
  addedAt: string
  labels?: Label[]
  priceHistories?: PriceHistory[]
}

export type ProductDetail = Product

export const getProducts   = ()              => http.get<UserProductResponse[]>('/products')
export const getProduct    = (id: number)    => http.get<UserProductResponse>(`/products/${id}`)
export const createProduct = (url: string, targetPrice?: number) =>
  http.post<{ id: number; message?: string }>('/products', { url, targetPrice: targetPrice ?? null })
export const checkProduct  = (id: number)    => http.post(`/products/${id}/check`)
export const deleteProduct = (id: number)    => http.delete(`/products/${id}`)
export const updateTargetPrice = (id: number, targetPrice: number | null) =>
  http.patch(`/products/${id}/target-price`, { targetPrice })
export const updateAlertSettings = (
  id: number,
  payload: {
    alertMode: UserProductAlertMode
    discountThresholdPercent?: number | null
    targetPrice?: number | null
  }
) => http.patch(`/products/${id}/alert-settings`, payload)

export const getLabels    = ()                                => http.get<Label[]>('/labels')
export const createLabel  = (name: string, color: string)    => http.post<Label>('/labels', { name, color })
export const deleteLabel  = (id: number)                     => http.delete(`/labels/${id}`)
export const addProductLabel    = (productId: number, labelId: number) => http.post(`/products/${productId}/labels/${labelId}`)
export const removeProductLabel = (productId: number, labelId: number) => http.delete(`/products/${productId}/labels/${labelId}`)

// Yardımcı: UserProductResponse → flat Product
export function flattenProduct(up: UserProductResponse): Product {
  return {
    id: up.id,
    alertMode: up.alertMode ?? 'automatic',
    discountThresholdPercent: up.discountThresholdPercent,
    name: up.product.name,
    url: up.product.url,
    imageUrl: up.product.imageUrl,
    store: up.product.store,
    initialPrice: up.product.initialPrice,
    currentPrice: up.product.currentPrice,
    priceStatus: up.product.priceStatus,
    targetPrice: up.targetPrice,
    lastCheckedAt: up.product.lastCheckedAt,
    createdAt: up.product.createdAt,
    addedAt: up.addedAt,
    labels: up.labels ?? [],
    priceHistories: up.product.priceHistories ?? [],
  }
}

function formatAlertNumber(value: number): string {
  const rounded = Math.round(value)
  if (Math.abs(value - rounded) < 0.001) {
    return String(rounded)
  }

  const oneDecimal = Number(value.toFixed(1))
  if (Math.abs(value - oneDecimal) < 0.001) {
    return oneDecimal.toFixed(1).replace('.', ',')
  }

  return value.toFixed(2).replace('.', ',')
}

export function getAlertSummaryLabel(
  product: Pick<Product, 'alertMode' | 'discountThresholdPercent' | 'targetPrice'>
): string {
  switch (product.alertMode) {
    case 'percentage':
      return product.discountThresholdPercent != null
        ? `%${formatAlertNumber(product.discountThresholdPercent)} indirim alarmı`
        : 'Yüzde indirim alarmı'
    case 'target_price':
      return product.targetPrice != null
        ? `${formatAlertNumber(product.targetPrice)} TL hedef fiyat`
        : 'Hedef fiyat alarmı'
    default:
      return 'Otomatik takip'
  }
}

export function getMissingPriceLabel(product: Pick<Product, 'currentPrice' | 'priceStatus' | 'lastCheckedAt'>): string | null {
  if (product.currentPrice != null) return null

  switch (product.priceStatus) {
    case 'out_of_stock':
      return 'Stokta yok'
    case 'price_not_found':
      return 'Fiyat bulunamadı'
    default:
      return product.lastCheckedAt ? 'Fiyat bulunamadı' : 'Fiyat kontrolü bekleniyor'
  }
}