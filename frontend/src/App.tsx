import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import ProductsPage      from './pages/ProductsPage'
import ProductDetailPage from './pages/ProductDetailPage'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/"              element={<ProductsPage />} />
        <Route path="/products/:id"  element={<ProductDetailPage />} />
        <Route path="*"              element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
