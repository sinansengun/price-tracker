import { useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { login, register, setToken } from '../api/api'

type Mode = 'login' | 'register'

export default function AuthPage({ mode }: { mode: Mode }) {
  const navigate = useNavigate()
  const [email, setEmail]       = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm]   = useState('')
  const [error, setError]       = useState('')
  const [loading, setLoading]   = useState(false)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    if (mode === 'register' && password !== confirm) {
      setError('Şifreler uyuşmuyor.')
      return
    }
    setLoading(true)
    try {
      const fn = mode === 'login' ? login : register
      const res = await fn(email.trim().toLowerCase(), password)
      setToken(res.data.token)
      navigate('/')
    } catch (err: any) {
      const msg = err.response?.data?.error
        ?? err.response?.data?.errors?.[0]
        ?? (mode === 'login' ? 'E-posta veya şifre hatalı.' : 'Kayıt başarısız.')
      setError(msg)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen bg-gray-50 flex items-center justify-center px-4">
      <div className="w-full max-w-sm bg-white rounded-2xl shadow-sm border border-gray-100 p-8">
        {/* Logo */}
        <div className="flex flex-col items-center mb-8">
          <div className="w-12 h-12 bg-brand-600 rounded-xl flex items-center justify-center mb-3">
            <svg className="w-7 h-7 text-white" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A2 2 0 013 12V7a2 2 0 012-2z" />
            </svg>
          </div>
          <h1 className="text-xl font-bold text-gray-900">Price Tracker</h1>
          <p className="text-sm text-gray-500 mt-1">
            {mode === 'login' ? 'Hesabına giriş yap' : 'Yeni hesap oluştur'}
          </p>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">E-posta</label>
            <input
              type="email"
              required
              value={email}
              onChange={e => setEmail(e.target.value)}
              className="w-full border border-gray-200 rounded-xl px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
              placeholder="ornek@mail.com"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Şifre</label>
            <input
              type="password"
              required
              minLength={6}
              value={password}
              onChange={e => setPassword(e.target.value)}
              className="w-full border border-gray-200 rounded-xl px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
              placeholder="En az 6 karakter"
            />
          </div>

          {mode === 'register' && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Şifre Tekrar</label>
              <input
                type="password"
                required
                value={confirm}
                onChange={e => setConfirm(e.target.value)}
                className="w-full border border-gray-200 rounded-xl px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
                placeholder="Şifreyi tekrar girin"
              />
            </div>
          )}

          {error && (
            <p className="text-sm text-red-600 bg-red-50 border border-red-200 rounded-xl px-3 py-2">{error}</p>
          )}

          <button
            type="submit"
            disabled={loading}
            className="w-full bg-brand-600 hover:bg-brand-700 disabled:opacity-50 text-white font-medium rounded-xl py-2.5 text-sm transition-colors"
          >
            {loading ? 'Lütfen bekleyin…' : mode === 'login' ? 'Giriş Yap' : 'Kayıt Ol'}
          </button>
        </form>

        <p className="text-center text-sm text-gray-500 mt-6">
          {mode === 'login' ? (
            <>Hesabın yok mu?{' '}
              <Link to="/register" className="text-brand-600 font-medium hover:underline">Kayıt Ol</Link>
            </>
          ) : (
            <>Zaten hesabın var mı?{' '}
              <Link to="/login" className="text-brand-600 font-medium hover:underline">Giriş Yap</Link>
            </>
          )}
        </p>
      </div>
    </div>
  )
}
