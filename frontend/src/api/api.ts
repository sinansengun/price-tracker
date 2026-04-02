import axios from 'axios'

const http = axios.create({ baseURL: '/api' })

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

export interface Label {
  id: number
  name: string
  color: string
}

// Ham API yanıtı (UserProduct nested yapısı)
export interface UserProductResponse {
  id: number
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
    lastCheckedAt?: string
    createdAt: string
    priceHistories?: PriceHistory[]
  }
  labels?: Label[]
}

// UI'da kullanılan flat yapı (flatten() ile dönüştürülür)
export interface Product {
  id: number          // UserProduct.Id
  name: string
  url: string
  imageUrl?: string
  store?: string
  initialPrice?: number
  currentPrice?: number
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
  http.post<{ id: number }>('/products', { url, targetPrice: targetPrice ?? null })
export const checkProduct  = (id: number)    => http.post(`/products/${id}/check`)
export const deleteProduct = (id: number)    => http.delete(`/products/${id}`)
export const updateTargetPrice = (id: number, targetPrice: number | null) =>
  http.patch(`/products/${id}/target-price`, { targetPrice })

export const getLabels    = ()                                => http.get<Label[]>('/labels')
export const createLabel  = (name: string, color: string)    => http.post<Label>('/labels', { name, color })
export const deleteLabel  = (id: number)                     => http.delete(`/labels/${id}`)
export const addProductLabel    = (productId: number, labelId: number) => http.post(`/products/${productId}/labels/${labelId}`)
export const removeProductLabel = (productId: number, labelId: number) => http.delete(`/products/${productId}/labels/${labelId}`)

// Yardımcı: UserProductResponse → flat Product
export function flattenProduct(up: UserProductResponse): Product {
  return {
    id: up.id,
    name: up.product.name,
    url: up.product.url,
    imageUrl: up.product.imageUrl,
    store: up.product.store,
    initialPrice: up.product.initialPrice,
    currentPrice: up.product.currentPrice,
    targetPrice: up.targetPrice,
    lastCheckedAt: up.product.lastCheckedAt,
    createdAt: up.product.createdAt,
    addedAt: up.addedAt,
    labels: up.labels ?? [],
    priceHistories: up.product.priceHistories ?? [],
  }
}