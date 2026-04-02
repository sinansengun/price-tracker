import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { getToken } from './api/api'
import AuthPage      from './pages/AuthPage'
import ProductsPage      from './pages/ProductsPage'
import ProductDetailPage from './pages/ProductDetailPage'

function RequireAuth({ children }: { children: React.ReactNode }) {
  return getToken() ? <>{children}</> : <Navigate to="/login" replace />
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login"    element={<AuthPage mode="login" />} />
        <Route path="/register" element={<AuthPage mode="register" />} />
        <Route path="/" element={
          <RequireAuth><ProductsPage /></RequireAuth>
        } />
        <Route path="/products/:id" element={
          <RequireAuth><ProductDetailPage /></RequireAuth>
        } />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
