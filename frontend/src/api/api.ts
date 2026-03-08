import axios from 'axios'

const http = axios.create({ baseURL: '/api' })

export interface PriceHistory {
  price: number
  checkedAt: string
}

export interface Product {
  id: number
  name: string
  url: string
  imageUrl?: string
  store?: string
  initialPrice?: number
  currentPrice?: number
  targetPrice?: number
  lastCheckedAt?: string
  createdAt: string
  priceHistories?: PriceHistory[]
}

export interface ProductDetail extends Product {
  priceHistories: PriceHistory[]
}

export const getProducts   = ()              => http.get<Product[]>('/products')
export const getProduct    = (id: number)    => http.get<ProductDetail>(`/products/${id}`)
export const createProduct = (url: string, targetPrice?: number) =>
  http.post<{ id: number }>('/products', { url, targetPrice: targetPrice ?? null })
export const checkProduct  = (id: number)    => http.post(`/products/${id}/check`)
export const deleteProduct = (id: number)    => http.delete(`/products/${id}`)
