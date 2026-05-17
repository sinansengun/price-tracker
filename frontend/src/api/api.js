import axios from 'axios';
const http = axios.create({ baseURL: import.meta.env.VITE_API_URL ?? '/api' });
// JWT token yönetimi
export const getToken = () => localStorage.getItem('token');
export const setToken = (t) => localStorage.setItem('token', t);
export const clearToken = () => localStorage.removeItem('token');
// Her istekte Authorization header ekle
http.interceptors.request.use(config => {
    const token = getToken();
    if (token)
        config.headers.Authorization = `Bearer ${token}`;
    return config;
});
// 401 gelince login'e yönlendir
http.interceptors.response.use(r => r, err => {
    if (err.response?.status === 401) {
        clearToken();
        window.location.href = '/login';
    }
    return Promise.reject(err);
});
// ── Auth ──────────────────────────────────────────────────────────────────
export const login = (email, password) => http.post('/auth/login', { email, password });
export const register = (email, password) => http.post('/auth/register', { email, password });
export const getProducts = () => http.get('/products');
export const getProduct = (id) => http.get(`/products/${id}`);
export const createProduct = (url, targetPrice) => http.post('/products', { url, targetPrice: targetPrice ?? null });
export const checkProduct = (id) => http.post(`/products/${id}/check`);
export const deleteProduct = (id) => http.delete(`/products/${id}`);
export const updateTargetPrice = (id, targetPrice) => http.patch(`/products/${id}/target-price`, { targetPrice });
export const updateAlertSettings = (id, payload) => http.patch(`/products/${id}/alert-settings`, payload);
export const getLabels = () => http.get('/labels');
export const createLabel = (name, color) => http.post('/labels', { name, color });
export const deleteLabel = (id) => http.delete(`/labels/${id}`);
export const addProductLabel = (productId, labelId) => http.post(`/products/${productId}/labels/${labelId}`);
export const removeProductLabel = (productId, labelId) => http.delete(`/products/${productId}/labels/${labelId}`);
// Yardımcı: UserProductResponse → flat Product
export function flattenProduct(up) {
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
    };
}
function formatAlertNumber(value) {
    const rounded = Math.round(value);
    if (Math.abs(value - rounded) < 0.001) {
        return String(rounded);
    }
    const oneDecimal = Number(value.toFixed(1));
    if (Math.abs(value - oneDecimal) < 0.001) {
        return oneDecimal.toFixed(1).replace('.', ',');
    }
    return value.toFixed(2).replace('.', ',');
}
export function getAlertSummaryLabel(product) {
    switch (product.alertMode) {
        case 'percentage':
            return product.discountThresholdPercent != null
                ? `%${formatAlertNumber(product.discountThresholdPercent)} indirim alarmı`
                : 'Yüzde indirim alarmı';
        case 'target_price':
            return product.targetPrice != null
                ? `${formatAlertNumber(product.targetPrice)} TL hedef fiyat`
                : 'Hedef fiyat alarmı';
        default:
            return 'Otomatik takip';
    }
}
export function getMissingPriceLabel(product) {
    if (product.currentPrice != null)
        return null;
    switch (product.priceStatus) {
        case 'out_of_stock':
            return 'Stokta yok';
        case 'price_not_found':
            return 'Fiyat bulunamadı';
        default:
            return product.lastCheckedAt ? 'Fiyat bulunamadı' : 'Fiyat kontrolü bekleniyor';
    }
}
