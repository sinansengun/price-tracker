import { Fragment as _Fragment, jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { getToken } from './api/api';
import AuthPage from './pages/AuthPage';
import ProductsPage from './pages/ProductsPage';
import ProductDetailPage from './pages/ProductDetailPage';
function RequireAuth({ children }) {
    return getToken() ? _jsx(_Fragment, { children: children }) : _jsx(Navigate, { to: "/login", replace: true });
}
export default function App() {
    return (_jsx(BrowserRouter, { children: _jsxs(Routes, { children: [_jsx(Route, { path: "/login", element: _jsx(AuthPage, { mode: "login" }) }), _jsx(Route, { path: "/register", element: _jsx(AuthPage, { mode: "register" }) }), _jsx(Route, { path: "/", element: _jsx(RequireAuth, { children: _jsx(ProductsPage, {}) }) }), _jsx(Route, { path: "/products/:id", element: _jsx(RequireAuth, { children: _jsx(ProductDetailPage, {}) }) }), _jsx(Route, { path: "*", element: _jsx(Navigate, { to: "/", replace: true }) })] }) }));
}
