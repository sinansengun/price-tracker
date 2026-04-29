import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate } from 'react-router-dom';
function StoreBadge({ store, url }) {
    const domain = (() => { try {
        return new URL(url).hostname;
    }
    catch {
        return '';
    } })();
    const faviconUrl = domain ? `https://www.google.com/s2/favicons?domain=${domain}&sz=16` : '';
    return (_jsxs("span", { className: "inline-flex items-center gap-1 text-xs font-medium bg-brand-50 text-brand-700 border border-brand-200 px-2 py-0.5 rounded-full", children: [faviconUrl && _jsx("img", { src: faviconUrl, alt: "", className: "w-4 h-4 rounded-sm" }), store] }));
}
import { LineChart, Line, ResponsiveContainer, Tooltip, YAxis } from 'recharts';
import { getProducts, getLabels, createProduct, addProductLabel, removeProductLabel, createLabel, deleteLabel, clearToken, flattenProduct } from '../api/api';
function AddProductModal({ onClose, onAdded }) {
    const [url, setUrl] = useState('');
    const [targetPrice, setTargetPrice] = useState('');
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState('');
    const inputRef = useRef(null);
    useEffect(() => { inputRef.current?.focus(); }, []);
    const handleSubmit = async (e) => {
        e.preventDefault();
        if (!url.trim()) {
            setError('URL gereklidir.');
            return;
        }
        setError('');
        setLoading(true);
        try {
            const cleanUrl = url.trim()
                .replace(/\u200b|\u200c|\u200d|\ufeff/g, '')
                .replace(/&amp;/g, '&');
            const tp = targetPrice ? parseFloat(targetPrice.replace(',', '.')) : undefined;
            await createProduct(cleanUrl, tp);
            onAdded();
            onClose();
        }
        catch (err) {
            setError(err?.response?.data?.error ?? 'Bir hata oluştu.');
        }
        finally {
            setLoading(false);
        }
    };
    return (_jsx("div", { className: "fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm", onClick: onClose, children: _jsxs("div", { className: "bg-white rounded-2xl shadow-xl w-full max-w-lg mx-4 p-6", onClick: e => e.stopPropagation(), children: [_jsx("h2", { className: "text-lg font-bold text-gray-900 mb-5", children: "\u00DCr\u00FCn Ekle" }), _jsxs("form", { onSubmit: handleSubmit, className: "space-y-4", children: [_jsxs("div", { children: [_jsx("label", { className: "block text-sm font-medium text-gray-700 mb-1", children: "\u00DCr\u00FCn URL" }), _jsx("input", { ref: inputRef, value: url, onChange: e => setUrl(e.target.value), placeholder: "https://www.hepsiburada.com/...", className: "w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500" })] }), _jsxs("div", { children: [_jsxs("label", { className: "block text-sm font-medium text-gray-700 mb-1", children: ["Hedef Fiyat ", _jsx("span", { className: "text-gray-400 font-normal", children: "(opsiyonel)" })] }), _jsxs("div", { className: "relative", children: [_jsx("input", { value: targetPrice, onChange: e => setTargetPrice(e.target.value), placeholder: "\u00F6rn. 4500", type: "number", min: "0", className: "w-full border border-gray-300 rounded-lg px-3 py-2 pr-10 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500" }), _jsx("span", { className: "absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 text-sm", children: "\u20BA" })] })] }), error && _jsx("p", { className: "text-sm text-red-600", children: error }), _jsxs("div", { className: "flex gap-3 pt-1", children: [_jsx("button", { type: "button", onClick: onClose, className: "flex-1 border border-gray-300 rounded-lg py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors", children: "\u0130ptal" }), _jsx("button", { type: "submit", disabled: loading, className: "flex-1 bg-brand-600 hover:bg-brand-700 text-white rounded-lg py-2 text-sm font-medium transition-colors disabled:opacity-60", children: loading ? 'Ekleniyor...' : 'Ekle' })] })] })] }) }));
}
function fmt(price) {
    return price.toLocaleString('tr-TR', { minimumFractionDigits: 2 }) + ' ₺';
}
function PriceText({ price, className = '' }) {
    const formatted = price.toLocaleString('tr-TR', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    });
    const [mainPart, decimalPart = '00'] = formatted.split(',');
    return (_jsxs("span", { className: className, children: [_jsx("span", { children: mainPart }), _jsxs("span", { className: "ml-0.5 align-bottom text-[0.62em]", children: [",", decimalPart, " \u20BA"] })] }));
}
function fmtDate(iso, includeTime = false) {
    return new Date(iso).toLocaleString('tr-TR', {
        day: 'numeric', month: 'short', year: 'numeric',
        ...(includeTime ? { hour: '2-digit', minute: '2-digit' } : {}),
    });
}
function localDayKey(date) {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}
function buildLast30DaySeries(histories) {
    const end = new Date();
    end.setHours(0, 0, 0, 0);
    const startMs = end.getTime() - 29 * 86400000;
    const endExclusive = end.getTime() + 86400000;
    const byDay = new Map();
    for (const h of histories ?? []) {
        const t = new Date(h.checkedAt).getTime();
        if (Number.isNaN(t) || t < startMs || t >= endExclusive)
            continue;
        const key = localDayKey(new Date(t));
        const prev = byDay.get(key);
        if (!prev || t > prev.t) {
            byDay.set(key, { price: h.price, checkedAt: h.checkedAt, t });
        }
    }
    const series = [];
    for (let i = 0; i < 30; i++) {
        const day = new Date(startMs + i * 86400000);
        const key = localDayKey(day);
        const found = byDay.get(key);
        series.push({
            dayKey: key,
            v: found?.price ?? null,
            checkedAt: found?.checkedAt,
        });
    }
    return series;
}
function LabelDropdown({ product, allLabels, onProductLabelsChange, onNewLabel }) {
    const [open, setOpen] = useState(false);
    const [search, setSearch] = useState('');
    const [color, setColor] = useState('#6366f1');
    const [saving, setSaving] = useState(false);
    const [pos, setPos] = useState({ top: 0, left: 0 });
    const btnRef = useRef(null);
    const dropRef = useRef(null);
    useEffect(() => {
        if (!open)
            return;
        const handler = (e) => {
            if (dropRef.current && !dropRef.current.contains(e.target) &&
                btnRef.current && !btnRef.current.contains(e.target))
                setOpen(false);
        };
        document.addEventListener('mousedown', handler);
        return () => document.removeEventListener('mousedown', handler);
    }, [open]);
    const handleOpen = (e) => {
        e.stopPropagation();
        if (btnRef.current) {
            const r = btnRef.current.getBoundingClientRect();
            setPos({ top: r.bottom + window.scrollY + 2, left: r.left + window.scrollX });
        }
        setOpen(v => !v);
    };
    const currentLabels = product.labels ?? [];
    const handleToggle = async (label) => {
        const has = currentLabels.some(l => l.id === label.id);
        try {
            if (has) {
                await removeProductLabel(product.id, label.id);
                onProductLabelsChange(product.id, currentLabels.filter(l => l.id !== label.id));
            }
            else {
                await addProductLabel(product.id, label.id);
                onProductLabelsChange(product.id, [...currentLabels, label]);
            }
        }
        catch { /* ignore */ }
    };
    const handleCreate = async (e) => {
        e.preventDefault();
        if (!search.trim())
            return;
        setSaving(true);
        try {
            const res = await createLabel(search.trim(), color);
            const created = res.data;
            onNewLabel(created);
            await addProductLabel(product.id, created.id);
            onProductLabelsChange(product.id, [...currentLabels, created]);
            setSearch('');
            setColor('#6366f1');
        }
        catch { /* ignore */ }
        finally {
            setSaving(false);
        }
    };
    const filtered = allLabels.filter(l => !search.trim() || l.name.toLowerCase().includes(search.toLowerCase()));
    const canCreate = search.trim() && !allLabels.some(l => l.name.toLowerCase() === search.trim().toLowerCase());
    return (_jsxs(_Fragment, { children: [_jsx("button", { ref: btnRef, onClick: handleOpen, className: "text-[10px] font-bold uppercase tracking-wide text-gray-400 bg-gray-100 hover:bg-gray-200 px-1.5 py-0.5 rounded transition-colors", children: "+ Label" }), open && createPortal(_jsxs("div", { ref: dropRef, style: { position: 'absolute', top: pos.top, left: pos.left }, className: "z-[9999] w-60 bg-white border border-gray-200 rounded-lg shadow-xl", onClick: e => e.stopPropagation(), children: [_jsxs("form", { onSubmit: handleCreate, className: "flex items-center gap-1.5 p-2 border-b border-gray-100", children: [_jsx("input", { value: search, onChange: e => setSearch(e.target.value), placeholder: "Ara veya olu\u015Ftur...", autoFocus: true, className: "flex-1 min-w-0 text-xs px-2 py-1.5 border border-gray-200 rounded focus:outline-none focus:ring-2 focus:ring-brand-400" }), _jsx("input", { type: "color", value: color, onChange: e => setColor(e.target.value), className: "w-7 h-7 rounded border border-gray-200 cursor-pointer p-0.5 shrink-0", title: "Renk se\u00E7" }), canCreate && (_jsx("button", { type: "submit", disabled: saving, className: "text-xs bg-brand-600 text-white px-2 py-1.5 rounded hover:bg-brand-700 transition-colors disabled:opacity-40 font-medium shrink-0", children: saving ? '...' : 'Ekle' }))] }), _jsxs("div", { className: "max-h-48 overflow-y-auto py-1", children: [filtered.map(l => {
                                const attached = currentLabels.some(pl => pl.id === l.id);
                                return (_jsxs("button", { onClick: () => handleToggle(l), className: "w-full flex items-center gap-2 px-3 py-1.5 text-left hover:bg-gray-50 transition-colors", children: [_jsx("span", { className: "w-3 h-3 rounded-sm shrink-0", style: { backgroundColor: l.color } }), _jsx("span", { className: "text-xs text-gray-700 flex-1 truncate", children: l.name }), attached && _jsx("span", { className: "text-brand-600 text-xs", children: "\u2713" })] }, l.id));
                            }), filtered.length === 0 && !canCreate && (_jsx("p", { className: "px-3 py-2 text-xs text-gray-400", children: "E\u015Fle\u015Fen label yok" }))] })] }), document.body)] }));
}
function MiniChart({ points }) {
    const prices = points
        .map(p => p.v)
        .filter((v) => v != null);
    if (prices.length === 0)
        return null;
    const flat = Math.min(...prices) === Math.max(...prices);
    const color = flat ? '#94a3b8' : '#2563eb';
    const firstPrice = prices[0];
    const maxDeviation = prices.reduce((max, p) => Math.max(max, Math.abs(p - firstPrice)), 0);
    const basePadding = Math.max(firstPrice * 0.03, 1);
    const halfRange = Math.max(maxDeviation * 1.15, basePadding);
    const yMin = firstPrice - halfRange;
    const yMax = firstPrice + halfRange;
    return (_jsx("div", { className: "w-full rounded-lg border border-dashed border-gray-300 bg-white/70 px-1 py-1", children: _jsx(ResponsiveContainer, { width: "100%", height: 48, children: _jsxs(LineChart, { data: points, children: [_jsx(YAxis, { hide: true, domain: [yMin, yMax] }), _jsx(Line, { type: "linear", dataKey: "v", stroke: color, strokeWidth: 1.8, isAnimationActive: false, connectNulls: true, dot: false, activeDot: false }), _jsx(Tooltip, { content: ({ active, payload }) => active && payload?.length && payload[0].value != null
                            ? _jsx("div", { className: "bg-white border border-gray-200 rounded px-2 py-1 text-xs shadow", children: fmt(payload[0].value) })
                            : null })] }) }) }));
}
function ProductRow({ product, allLabels, onProductLabelsChange, onNewLabel, onLabelClick }) {
    const navigate = useNavigate();
    const imgSrc = product.imageUrl?.replace('{size}', '200');
    const histories = product.priceHistories;
    const sorted = histories
        ? [...histories].sort((a, b) => new Date(a.checkedAt).getTime() - new Date(b.checkedAt).getTime())
        : [];
    const monthSeries = buildLast30DaySeries(sorted);
    const monthWithPrice = monthSeries
        .filter((p) => p.v != null && !!p.checkedAt)
        .sort((a, b) => new Date(a.checkedAt).getTime() - new Date(b.checkedAt).getTime());
    const first = monthWithPrice[0];
    const last = monthWithPrice[monthWithPrice.length - 1];
    let pct = null;
    let pctUp = false;
    if (first && last && first.v > 0) {
        pct = ((last.v - first.v) / first.v) * 100;
        pctUp = pct > 0;
    }
    const periodLabel = 'Son 1 ay';
    return (_jsxs("div", { className: "bg-white border border-gray-200 rounded-xl overflow-hidden cursor-pointer hover:shadow-md hover:border-brand-200 transition-all duration-150 group", onClick: () => navigate(`/products/${product.id}`), children: [_jsxs("div", { className: "flex", children: [_jsx("div", { className: "shrink-0 w-24 sm:w-40 h-24 sm:h-32 bg-white flex items-center justify-center p-2 border-r border-gray-100 overflow-hidden", children: imgSrc
                            ? _jsx("img", { src: imgSrc, alt: product.name, className: "block max-w-full max-h-full object-contain" })
                            : _jsx("span", { className: "text-4xl", children: "\uD83D\uDECD\uFE0F" }) }), _jsxs("div", { className: "flex-1 min-w-0 px-3 sm:px-4 py-3 flex flex-col justify-between", children: [_jsx("p", { className: "text-sm font-semibold text-gray-900 leading-snug line-clamp-2 group-hover:text-brand-700 transition-colors", children: product.name || 'Yükleniyor...' }), product.store && (_jsx("div", { className: "mt-1", children: _jsx(StoreBadge, { store: product.store, url: product.url }) })), _jsx("div", { className: "mt-2 space-y-0.5", children: product.currentPrice != null ? (_jsxs(_Fragment, { children: [_jsx("p", { className: "text-xl font-bold text-gray-900", children: _jsx(PriceText, { price: product.currentPrice }) }), product.initialPrice != null && product.initialPrice !== product.currentPrice && (_jsx("p", { className: "text-xs text-gray-400 line-through", children: _jsx(PriceText, { price: product.initialPrice }) }))] })) : (_jsx("p", { className: "text-sm text-gray-400", children: "Fiyat bekleniyor" })) }), _jsxs("div", { className: "mt-3 flex items-center gap-1.5 flex-wrap py-1.5", onClick: e => e.stopPropagation(), children: [product.labels?.map(l => (_jsx("button", { onClick: e => { e.stopPropagation(); onLabelClick(l.id); }, className: "text-[10px] font-bold uppercase tracking-wide px-1.5 py-0.5 rounded cursor-pointer hover:brightness-90 transition-all", style: { backgroundColor: l.color + '1A', color: l.color }, children: l.name }, l.id))), _jsx(LabelDropdown, { product: product, allLabels: allLabels, onProductLabelsChange: onProductLabelsChange, onNewLabel: onNewLabel }), product.targetPrice != null && (_jsxs("span", { className: "text-xs text-gray-400", children: ["\uD83C\uDFAF Hedef: ", _jsx(PriceText, { price: product.targetPrice })] }))] }), _jsxs("p", { className: "mt-1 text-xs text-gray-400 hidden sm:block", children: ["Eklendi: ", fmtDate(product.createdAt), product.lastCheckedAt && _jsxs(_Fragment, { children: [" \u00A0\u00B7\u00A0 G\u00FCncellendi: ", fmtDate(product.lastCheckedAt, true)] })] })] }), _jsxs("div", { className: "hidden sm:flex shrink-0 w-52 border-l border-gray-100 bg-white flex-col items-center justify-between px-3 py-3 gap-1", children: [pct !== null && (_jsxs("div", { className: `flex items-center gap-1 text-xs font-bold ${pctUp ? 'text-red-500' : 'text-green-600'}`, children: [_jsxs("span", { children: [periodLabel, ":"] }), _jsxs("span", { children: [pctUp ? '▲' : '▼', " %", Math.abs(pct).toFixed(1)] })] })), _jsx("div", { className: "w-full", children: _jsx(MiniChart, { points: monthSeries }) }), first && (_jsxs("p", { className: "text-[10px] text-gray-400 text-center leading-tight", children: [fmtDate(first.checkedAt), _jsx("br", {}), _jsx("span", { className: "font-medium text-gray-600", children: _jsx(PriceText, { price: first.v }) })] })), _jsx("button", { className: "mt-1 w-full text-xs text-brand-600 border border-brand-300 rounded-lg py-1 hover:bg-brand-50 transition-colors font-medium", onClick: e => { e.stopPropagation(); navigate(`/products/${product.id}`); }, children: "Detaya Git \u2192" })] })] }), monthWithPrice.length >= 1 && (_jsxs("div", { className: "sm:hidden border-t border-gray-100 bg-white px-3 pt-1 pb-2", onClick: e => e.stopPropagation(), children: [_jsxs("div", { className: "flex items-center justify-between mb-0.5", children: [pct !== null && (_jsxs("span", { className: `text-[10px] font-bold ${pctUp ? 'text-red-500' : 'text-green-600'}`, children: [pctUp ? '▲' : '▼', " %", Math.abs(pct).toFixed(1), " ", periodLabel && `(${periodLabel})`] })), first && (_jsxs("span", { className: "text-[10px] text-gray-400", children: ["Ba\u015Flang\u0131\u00E7: ", _jsx("span", { className: "font-medium text-gray-600", children: _jsx(PriceText, { price: first.v }) })] }))] }), _jsx(MiniChart, { points: monthSeries })] }))] }));
}
function ProductCard({ product }) {
    const navigate = useNavigate();
    const imgSrc = product.imageUrl?.replace('{size}', '375');
    const initial = product.initialPrice;
    const current = product.currentPrice;
    let pct = null;
    let pctUp = false;
    if (initial && current && initial > 0) {
        pct = ((current - initial) / initial) * 100;
        pctUp = pct > 0;
    }
    return (_jsxs("div", { onClick: () => navigate(`/products/${product.id}`), className: "bg-white rounded-2xl shadow-sm border border-gray-100 overflow-hidden cursor-pointer hover:shadow-md hover:-translate-y-0.5 transition-all duration-150", children: [_jsx("div", { className: "h-44 bg-white flex items-center justify-center overflow-hidden p-4", children: imgSrc
                    ? _jsx("img", { src: imgSrc, alt: product.name, className: "block max-h-full max-w-full object-contain" })
                    : _jsx("span", { className: "text-4xl", children: "\uD83D\uDECD\uFE0F" }) }), _jsxs("div", { className: "p-4 space-y-2", children: [product.labels && product.labels.length > 0 && (_jsx("div", { className: "flex flex-wrap gap-1", children: product.labels.map(l => (_jsx("span", { className: "text-[10px] font-bold uppercase tracking-wide px-1.5 py-0.5 rounded", style: { backgroundColor: l.color + '1A', color: l.color }, children: l.name }, l.id))) })), _jsx("p", { className: "text-sm font-semibold text-gray-900 leading-snug line-clamp-2", children: product.name || 'Yükleniyor...' }), product.store && _jsx(StoreBadge, { store: product.store, url: product.url }), _jsxs("div", { className: "flex items-center gap-2 flex-wrap", children: [current != null
                                ? _jsx("span", { className: "text-lg font-bold text-gray-900", children: _jsx(PriceText, { price: current }) })
                                : _jsx("span", { className: "text-sm text-gray-400", children: "Fiyat bekleniyor..." }), pct !== null && Math.abs(pct) >= 0.01 && (_jsxs("span", { className: `inline-flex items-center gap-0.5 text-xs font-semibold px-1.5 py-0.5 rounded-full ${pctUp ? 'bg-red-100 text-red-600' : 'bg-green-100 text-green-600'}`, children: [pctUp ? '▲' : '▼', " ", Math.abs(pct).toFixed(1), "%"] }))] }), initial != null && initial !== current && (_jsxs("p", { className: "text-xs text-gray-400", children: ["Ba\u015Flang\u0131\u00E7: ", _jsx(PriceText, { price: initial })] })), product.targetPrice != null && (_jsxs("p", { className: "text-xs text-gray-500", children: ["\uD83C\uDFAF Hedef: ", _jsx(PriceText, { price: product.targetPrice })] }))] })] }));
}
export default function ProductsPage() {
    const [products, setProducts] = useState([]);
    const [labels, setLabels] = useState([]);
    const [activeLabel, setActiveLabel] = useState(null);
    const [loading, setLoading] = useState(true);
    const [showModal, setShowModal] = useState(false);
    const [view, setView] = useState('list');
    const load = async () => {
        try {
            const [prodRes, lblRes] = await Promise.all([getProducts(), getLabels()]);
            setProducts(prodRes.data.map(flattenProduct));
            setLabels(lblRes.data);
        }
        finally {
            setLoading(false);
        }
    };
    useEffect(() => { load(); }, []);
    const handleProductLabelsChange = (productId, newLabels) => {
        setProducts(prev => prev.map(p => p.id === productId ? { ...p, labels: newLabels } : p));
    };
    const handleNewLabel = (label) => {
        setLabels(prev => [...prev, label]);
    };
    const navigate = useNavigate();
    const handleLogout = () => {
        clearToken();
        navigate('/login');
    };
    const filtered = activeLabel == null
        ? products
        : products.filter(p => p.labels?.some(l => l.id === activeLabel));
    return (_jsxs("div", { className: "min-h-screen", children: [_jsx("header", { className: "bg-white border-b border-gray-200 sticky top-0 z-10", children: _jsxs("div", { className: "max-w-5xl mx-auto px-3 sm:px-4 py-3 sm:py-4 flex items-center justify-between", children: [_jsxs("div", { className: "flex items-center gap-2", children: [_jsx("span", { className: "text-2xl", children: "\uD83D\uDCB0" }), _jsx("h1", { className: "text-xl font-bold text-gray-900", children: "Fiyat Takip" })] }), _jsxs("div", { className: "flex items-center gap-1.5 sm:gap-2", children: [_jsxs("div", { className: "flex items-center border border-gray-200 rounded-xl overflow-hidden", children: [_jsx("button", { onClick: () => setView('list'), title: "Liste g\u00F6r\u00FCn\u00FCm\u00FC", className: `px-2.5 sm:px-3 py-2 text-sm transition-colors ${view === 'list' ? 'bg-brand-600 text-white' : 'text-gray-500 hover:bg-gray-50'}`, children: "\u2630" }), _jsx("button", { onClick: () => setView('grid'), title: "Kart g\u00F6r\u00FCn\u00FCm\u00FC", className: `px-2.5 sm:px-3 py-2 text-sm transition-colors ${view === 'grid' ? 'bg-brand-600 text-white' : 'text-gray-500 hover:bg-gray-50'}`, children: "\u229E" })] }), _jsx("button", { onClick: handleLogout, className: "hidden sm:block text-sm text-gray-500 hover:text-gray-800 px-3 py-2 rounded-xl hover:bg-gray-100 transition-colors", title: "\u00C7\u0131k\u0131\u015F Yap", children: "\u00C7\u0131k\u0131\u015F" }), _jsxs("button", { onClick: () => setShowModal(true), className: "bg-brand-600 hover:bg-brand-700 text-white text-sm font-medium px-3 sm:px-4 py-2 rounded-xl transition-colors", children: [_jsx("span", { className: "hidden sm:inline", children: "+ \u00DCr\u00FCn Ekle" }), _jsx("span", { className: "sm:hidden font-bold text-base leading-none", children: "+" })] })] })] }) }), _jsxs("main", { className: "max-w-5xl mx-auto px-3 sm:px-4 py-6 sm:py-8", children: [!loading && labels.length > 0 && (_jsxs("div", { className: "flex items-center gap-2 flex-wrap mb-6", children: [_jsx("button", { onClick: () => setActiveLabel(null), className: `text-[10px] font-bold uppercase tracking-wide px-2.5 py-1 rounded transition-colors ${activeLabel === null
                                    ? 'bg-gray-900 text-white'
                                    : 'bg-gray-100 text-gray-500 hover:bg-gray-200'}`, children: "T\u00FCm\u00FC" }), labels.map(l => (_jsx("button", { onClick: () => setActiveLabel(activeLabel === l.id ? null : l.id), className: `text-[10px] font-bold uppercase tracking-wide px-2.5 py-1 rounded transition-all ${activeLabel === l.id
                                    ? 'ring-2 ring-offset-1'
                                    : 'hover:brightness-90'}`, style: {
                                    backgroundColor: l.color + (activeLabel === l.id ? '33' : '1A'),
                                    color: l.color,
                                    ...(activeLabel === l.id ? { ringColor: l.color } : {})
                                }, children: l.name }, l.id)))] })), loading ? (_jsx("div", { className: "flex justify-center items-center py-24", children: _jsx("div", { className: "w-8 h-8 border-4 border-brand-500 border-t-transparent rounded-full animate-spin" }) })) : products.length === 0 ? (_jsxs("div", { className: "text-center py-24 space-y-3", children: [_jsx("p", { className: "text-5xl", children: "\uD83D\uDD0D" }), _jsx("p", { className: "text-gray-500 font-medium", children: "Hen\u00FCz takip edilen \u00FCr\u00FCn yok" }), _jsx("button", { onClick: () => setShowModal(true), className: "mt-2 text-brand-600 hover:underline text-sm font-medium", children: "\u0130lk \u00FCr\u00FCn\u00FC ekle \u2192" })] })) : filtered.length === 0 ? (_jsxs("div", { className: "text-center py-24 space-y-2", children: [_jsx("p", { className: "text-4xl", children: "\uD83C\uDFF7\uFE0F" }), _jsx("p", { className: "text-gray-500 font-medium", children: "Bu label ile e\u015Fle\u015Fen \u00FCr\u00FCn yok" }), _jsx("button", { onClick: async () => {
                                    if (activeLabel == null)
                                        return;
                                    await deleteLabel(activeLabel);
                                    setLabels(prev => prev.filter(l => l.id !== activeLabel));
                                    setActiveLabel(null);
                                }, className: "mt-1 text-xs text-red-500 hover:text-red-700 hover:underline font-medium transition-colors", children: "Label'\u0131 sil" })] })) : view === 'list' ? (_jsx("div", { className: "space-y-2", children: filtered.map(p => (_jsx(ProductRow, { product: p, allLabels: labels, onProductLabelsChange: handleProductLabelsChange, onNewLabel: handleNewLabel, onLabelClick: setActiveLabel }, p.id))) })) : (_jsx("div", { className: "grid grid-cols-1 min-[400px]:grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3 sm:gap-4", children: filtered.map(p => _jsx(ProductCard, { product: p }, p.id)) }))] }), showModal && (_jsx(AddProductModal, { onClose: () => setShowModal(false), onAdded: load }))] }));
}
