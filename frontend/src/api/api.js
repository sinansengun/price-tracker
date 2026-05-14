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
export const getLabels = () => http.get('/labels');
export const createLabel = (name, color) => http.post('/labels', { name, color });
export const deleteLabel = (id) => http.delete(`/labels/${id}`);
export const addProductLabel = (productId, labelId) => http.post(`/products/${productId}/labels/${labelId}`);
export const removeProductLabel = (productId, labelId) => http.delete(`/products/${productId}/labels/${labelId}`);
// Yardımcı: UserProductResponse → flat Product
export function flattenProduct(up) {
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
    };
}
