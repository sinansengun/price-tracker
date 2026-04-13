import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { login, register, setToken } from '../api/api';
export default function AuthPage({ mode }) {
    const navigate = useNavigate();
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [confirm, setConfirm] = useState('');
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const handleSubmit = async (e) => {
        e.preventDefault();
        setError('');
        if (mode === 'register' && password !== confirm) {
            setError('Şifreler uyuşmuyor.');
            return;
        }
        setLoading(true);
        try {
            const fn = mode === 'login' ? login : register;
            const res = await fn(email.trim().toLowerCase(), password);
            setToken(res.data.token);
            navigate('/');
        }
        catch (err) {
            const msg = err.response?.data?.error
                ?? err.response?.data?.errors?.[0]
                ?? (mode === 'login' ? 'E-posta veya şifre hatalı.' : 'Kayıt başarısız.');
            setError(msg);
        }
        finally {
            setLoading(false);
        }
    };
    return (_jsx("div", { className: "min-h-screen bg-gray-50 flex items-center justify-center px-4", children: _jsxs("div", { className: "w-full max-w-sm bg-white rounded-2xl shadow-sm border border-gray-100 p-8", children: [_jsxs("div", { className: "flex flex-col items-center mb-8", children: [_jsx("div", { className: "w-12 h-12 bg-brand-600 rounded-xl flex items-center justify-center mb-3", children: _jsx("svg", { className: "w-7 h-7 text-white", fill: "none", viewBox: "0 0 24 24", stroke: "currentColor", children: _jsx("path", { strokeLinecap: "round", strokeLinejoin: "round", strokeWidth: 2, d: "M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A2 2 0 013 12V7a2 2 0 012-2z" }) }) }), _jsx("h1", { className: "text-xl font-bold text-gray-900", children: "Price Tracker" }), _jsx("p", { className: "text-sm text-gray-500 mt-1", children: mode === 'login' ? 'Hesabına giriş yap' : 'Yeni hesap oluştur' })] }), _jsxs("form", { onSubmit: handleSubmit, className: "space-y-4", children: [_jsxs("div", { children: [_jsx("label", { className: "block text-sm font-medium text-gray-700 mb-1", children: "E-posta" }), _jsx("input", { type: "email", required: true, value: email, onChange: e => setEmail(e.target.value), className: "w-full border border-gray-200 rounded-xl px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500", placeholder: "ornek@mail.com" })] }), _jsxs("div", { children: [_jsx("label", { className: "block text-sm font-medium text-gray-700 mb-1", children: "\u015Eifre" }), _jsx("input", { type: "password", required: true, minLength: 6, value: password, onChange: e => setPassword(e.target.value), className: "w-full border border-gray-200 rounded-xl px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500", placeholder: "En az 6 karakter" })] }), mode === 'register' && (_jsxs("div", { children: [_jsx("label", { className: "block text-sm font-medium text-gray-700 mb-1", children: "\u015Eifre Tekrar" }), _jsx("input", { type: "password", required: true, value: confirm, onChange: e => setConfirm(e.target.value), className: "w-full border border-gray-200 rounded-xl px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500", placeholder: "\u015Eifreyi tekrar girin" })] })), error && (_jsx("p", { className: "text-sm text-red-600 bg-red-50 border border-red-200 rounded-xl px-3 py-2", children: error })), _jsx("button", { type: "submit", disabled: loading, className: "w-full bg-brand-600 hover:bg-brand-700 disabled:opacity-50 text-white font-medium rounded-xl py-2.5 text-sm transition-colors", children: loading ? 'Lütfen bekleyin…' : mode === 'login' ? 'Giriş Yap' : 'Kayıt Ol' })] }), _jsx("p", { className: "text-center text-sm text-gray-500 mt-6", children: mode === 'login' ? (_jsxs(_Fragment, { children: ["Hesab\u0131n yok mu?", ' ', _jsx(Link, { to: "/register", className: "text-brand-600 font-medium hover:underline", children: "Kay\u0131t Ol" })] })) : (_jsxs(_Fragment, { children: ["Zaten hesab\u0131n var m\u0131?", ' ', _jsx(Link, { to: "/login", className: "text-brand-600 font-medium hover:underline", children: "Giri\u015F Yap" })] })) })] }) }));
}
