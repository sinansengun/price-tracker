import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { useEffect, useRef, useState } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
function StoreBadge({ store, url }) {
    const domain = (() => { try {
        return new URL(url).hostname;
    }
    catch {
        return '';
    } })();
    const faviconUrl = domain ? `https://www.google.com/s2/favicons?domain=${domain}&sz=16` : '';
    return (_jsxs("span", { className: "inline-flex items-center gap-1 text-xs font-medium bg-brand-100 text-brand-700 px-2 py-0.5 rounded-full", children: [faviconUrl && _jsx("img", { src: faviconUrl, alt: "", className: "w-4 h-4 rounded-sm" }), store] }));
}
import { LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid, ResponsiveContainer } from 'recharts';
import { getProduct, checkProduct, deleteProduct, getLabels, createLabel, addProductLabel, removeProductLabel, flattenProduct } from '../api/api';
// ── Helpers ───────────────────────────────────────────────────────────────
function fmt(price) {
    return price.toLocaleString('tr-TR', { minimumFractionDigits: 2 }) + ' ₺';
}
function fmtDate(iso) {
    return new Date(iso).toLocaleString('tr-TR', {
        day: '2-digit', month: 'short', year: 'numeric',
        hour: '2-digit', minute: '2-digit',
    });
}
function fmtDateShort(iso) {
    return new Date(iso).toLocaleDateString('tr-TR', {
        day: '2-digit', month: 'short',
    });
}
// ── Custom chart tooltip ──────────────────────────────────────────────────
function CustomTooltip({ active, payload, label }) {
    if (!active || !payload?.length)
        return null;
    return (_jsxs("div", { className: "bg-white border border-gray-200 rounded-xl shadow-md px-3 py-2 text-sm", children: [_jsx("p", { className: "text-gray-500 text-xs mb-1", children: label }), _jsx("p", { className: "font-bold text-gray-900", children: fmt(payload[0].value) })] }));
}
// ── Page ──────────────────────────────────────────────────────────────────
export default function ProductDetailPage() {
    const { id } = useParams();
    const navigate = useNavigate();
    const [product, setProduct] = useState(null);
    const [flatProduct, setFlatProduct] = useState(null);
    const [allLabels, setAllLabels] = useState([]);
    const [loading, setLoading] = useState(true);
    const [checking, setChecking] = useState(false);
    const [deleting, setDeleting] = useState(false);
    const [error, setError] = useState('');
    const [showLabelPanel, setShowLabelPanel] = useState(false);
    const [newLabelName, setNewLabelName] = useState('');
    const [newLabelColor, setNewLabelColor] = useState('#6366f1');
    const [labelSaving, setLabelSaving] = useState(false);
    const labelDropdownRef = useRef(null);
    // Close dropdown on outside click
    useEffect(() => {
        if (!showLabelPanel)
            return;
        const handler = (e) => {
            if (labelDropdownRef.current && !labelDropdownRef.current.contains(e.target))
                setShowLabelPanel(false);
        };
        document.addEventListener('mousedown', handler);
        return () => document.removeEventListener('mousedown', handler);
    }, [showLabelPanel]);
    const load = async () => {
        try {
            const [prodRes, lblRes] = await Promise.all([getProduct(Number(id)), getLabels()]);
            setProduct(prodRes.data);
            setFlatProduct(flattenProduct(prodRes.data));
            setAllLabels(lblRes.data);
        }
        catch {
            setError('Ürün bulunamadı.');
        }
        finally {
            setLoading(false);
        }
    };
    useEffect(() => { load(); }, [id]);
    const handleCheck = async () => {
        setChecking(true);
        try {
            await checkProduct(Number(id));
            setTimeout(() => { load(); setChecking(false); }, 15000);
        }
        catch {
            setChecking(false);
        }
    };
    const handleToggleLabel = async (label) => {
        if (!product)
            return;
        const has = product.labels?.some((l) => l.id === label.id);
        try {
            if (has) {
                await removeProductLabel(product.id, label.id);
                setProduct(p => p ? { ...p, labels: (p.labels ?? []).filter(l => l.id !== label.id) } : p);
                setFlatProduct(p => p ? { ...p, labels: (p.labels ?? []).filter(l => l.id !== label.id) } : p);
            }
            else {
                await addProductLabel(product.id, label.id);
                setProduct(p => p ? { ...p, labels: [...(p.labels ?? []), label] } : p);
                setFlatProduct(p => p ? { ...p, labels: [...(p.labels ?? []), label] } : p);
            }
        }
        catch { /* ignore */ }
    };
    const handleCreateLabel = async (e) => {
        e.preventDefault();
        if (!newLabelName.trim())
            return;
        setLabelSaving(true);
        try {
            const res = await createLabel(newLabelName.trim(), newLabelColor);
            const created = res.data;
            setAllLabels(prev => [...prev, created]);
            setNewLabelName('');
            setNewLabelColor('#6366f1');
            // Also attach to current product
            if (product) {
                await addProductLabel(product.id, created.id);
                setProduct(p => p ? { ...p, labels: [...(p.labels ?? []), created] } : p);
                setFlatProduct(p => p ? { ...p, labels: [...(p.labels ?? []), created] } : p);
            }
        }
        catch { /* ignore */ }
        finally {
            setLabelSaving(false);
        }
    };
    if (loading) {
        return (_jsx("div", { className: "flex justify-center items-center min-h-screen", children: _jsx("div", { className: "w-8 h-8 border-4 border-brand-500 border-t-transparent rounded-full animate-spin" }) }));
    }
    if (error || !product || !flatProduct) {
        return (_jsxs("div", { className: "flex flex-col items-center justify-center min-h-screen gap-3", children: [_jsx("p", { className: "text-gray-500", children: error || 'Ürün bulunamadı.' }), _jsx(Link, { to: "/", className: "text-brand-600 hover:underline text-sm", children: "\u2190 Geri d\u00F6n" })] }));
    }
    const p = flatProduct;
    const imgSrc = p.imageUrl?.replace('{size}', '375');
    const priceChange = p.initialPrice && p.currentPrice
        ? ((p.currentPrice - p.initialPrice) / p.initialPrice) * 100
        : null;
    const chartData = [...(p?.priceHistories ?? [])]
        .sort((a, b) => new Date(a.checkedAt).getTime() - new Date(b.checkedAt).getTime())
        .map(h => ({ date: fmtDateShort(h.checkedAt), price: h.price, full: h.checkedAt }));
    const minPrice = Math.min(...chartData.map(d => d.price));
    const maxPrice = Math.max(...chartData.map(d => d.price));
    const padding = (maxPrice - minPrice) * 0.1 || 100;
    return (_jsxs("div", { className: "min-h-screen", children: [_jsx("header", { className: "bg-white border-b border-gray-200 sticky top-0 z-10", children: _jsxs("div", { className: "max-w-4xl mx-auto px-4 py-4 flex items-center gap-3", children: [_jsx("button", { onClick: () => navigate(-1), className: "text-gray-500 hover:text-gray-900 transition-colors p-1 -ml-1 rounded-lg hover:bg-gray-100", children: "\u2190" }), _jsx("h1", { className: "text-lg font-bold text-gray-900 truncate", children: p.name })] }) }), _jsxs("main", { className: "max-w-4xl mx-auto px-4 py-8 space-y-6", children: [_jsxs("div", { className: "bg-white rounded-2xl border border-gray-100 shadow-sm p-6 flex gap-6", children: [_jsx("div", { className: "shrink-0 w-32 h-32 bg-gray-50 rounded-xl flex items-center justify-center overflow-hidden", children: imgSrc ? (_jsx("img", { src: imgSrc, alt: p.name, className: "w-full h-full object-contain p-2" })) : (_jsx("span", { className: "text-4xl", children: "\uD83D\uDECD\uFE0F" })) }), _jsxs("div", { className: "flex-1 min-w-0 space-y-3", children: [_jsxs("div", { className: "flex items-start gap-2 flex-wrap", children: [p.store && _jsx(StoreBadge, { store: p.store, url: p.url }), _jsx("a", { href: p.url, target: "_blank", rel: "noopener noreferrer", className: "text-xs text-gray-400 hover:text-brand-600 transition-colors truncate max-w-xs", children: "\u00DCr\u00FCn sayfas\u0131 \u2192" })] }), _jsx("p", { className: "text-base font-semibold text-gray-900 leading-snug", children: p.name }), _jsxs("div", { className: "flex items-center gap-3 flex-wrap", children: [p.currentPrice != null && (_jsx("span", { className: "text-2xl font-bold text-gray-900", children: fmt(p.currentPrice) })), priceChange !== null && Math.abs(priceChange) >= 0.01 && (_jsxs("span", { className: `inline-flex items-center gap-0.5 text-sm font-semibold px-2 py-0.5 rounded-full ${priceChange > 0 ? 'bg-red-100 text-red-600' : 'bg-green-100 text-green-600'}`, children: [priceChange > 0 ? '▲' : '▼', " ", Math.abs(priceChange).toFixed(1), "%"] }))] }), _jsxs("div", { className: "flex gap-6 text-xs text-gray-500 flex-wrap", children: [p.initialPrice != null && (_jsxs("span", { children: ["Ba\u015Flang\u0131\u00E7: ", _jsx("strong", { className: "text-gray-700", children: fmt(p.initialPrice) })] })), p.targetPrice != null && (_jsxs("span", { children: ["\uD83C\uDFAF Hedef: ", _jsx("strong", { className: "text-gray-700", children: fmt(p.targetPrice) })] })), p.lastCheckedAt && (_jsxs("span", { children: ["Son kontrol: ", _jsx("strong", { className: "text-gray-700", children: fmtDate(p.lastCheckedAt) })] }))] }), _jsx("button", { onClick: handleCheck, disabled: checking, className: "inline-flex items-center gap-2 bg-brand-600 hover:bg-brand-700 text-white text-sm font-medium px-4 py-2 rounded-xl transition-colors disabled:opacity-60", children: checking ? (_jsxs(_Fragment, { children: [_jsx("span", { className: "w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" }), "Kontrol ediliyor..."] })) : '🔄 Fiyatı Kontrol Et' }), _jsx("button", { onClick: async () => {
                                            if (!confirm('Bu ürünü silmek istediğinize emin misiniz?'))
                                                return;
                                            setDeleting(true);
                                            await deleteProduct(Number(id));
                                            navigate('/');
                                        }, disabled: deleting, className: "inline-flex items-center gap-2 bg-red-50 hover:bg-red-100 text-red-600 text-sm font-medium px-4 py-2 rounded-xl transition-colors disabled:opacity-60", children: deleting ? 'Siliniyor...' : '🗑 Ürünü Sil' }), _jsx("div", { className: "pt-1", children: _jsxs("div", { className: "flex items-center gap-1.5 flex-wrap relative", children: [(p.labels ?? []).map(l => (_jsxs("span", { className: "inline-flex items-center gap-1 text-[10px] font-bold uppercase tracking-wide px-2 py-1 rounded", style: { backgroundColor: l.color + '1A', color: l.color }, children: [l.name, _jsx("button", { onClick: () => handleToggleLabel(l), className: "ml-0.5 opacity-60 hover:opacity-100 transition-opacity leading-none text-xs", style: { color: l.color }, title: "Label'\u0131 kald\u0131r", children: "\u00D7" })] }, l.id))), _jsxs("div", { className: "relative", ref: labelDropdownRef, children: [_jsx("button", { onClick: () => setShowLabelPanel(v => !v), className: "text-[10px] font-bold uppercase tracking-wide text-gray-400 bg-gray-100 hover:bg-gray-200 px-2 py-1 rounded transition-colors", children: "+ Label" }), showLabelPanel && (_jsxs("div", { className: "absolute left-0 top-full mt-1.5 z-50 w-64 bg-white border border-gray-200 rounded-lg shadow-lg", children: [_jsxs("form", { onSubmit: handleCreateLabel, className: "flex items-center gap-1.5 p-2 border-b border-gray-100", children: [_jsx("input", { value: newLabelName, onChange: e => setNewLabelName(e.target.value), placeholder: "Label ara veya olu\u015Ftur...", autoFocus: true, className: "flex-1 min-w-0 text-xs px-2 py-1.5 border border-gray-200 rounded focus:outline-none focus:ring-2 focus:ring-brand-400" }), _jsx("input", { type: "color", value: newLabelColor, onChange: e => setNewLabelColor(e.target.value), className: "w-7 h-7 rounded border border-gray-200 cursor-pointer p-0.5 shrink-0", title: "Renk se\u00E7" }), _jsx("button", { type: "submit", disabled: labelSaving || !newLabelName.trim(), className: "text-xs bg-brand-600 text-white px-2.5 py-1.5 rounded hover:bg-brand-700 transition-colors disabled:opacity-40 font-medium shrink-0", children: labelSaving ? '...' : 'Ekle' })] }), allLabels.length > 0 && (_jsx("div", { className: "max-h-48 overflow-y-auto py-1", children: allLabels
                                                                        .filter(l => !newLabelName.trim() || l.name.toLowerCase().includes(newLabelName.toLowerCase()))
                                                                        .map(l => {
                                                                        const attached = flatProduct?.labels?.some((pl) => pl.id === l.id);
                                                                        return (_jsxs("button", { onClick: () => handleToggleLabel(l), className: "w-full flex items-center gap-2 px-3 py-1.5 text-left hover:bg-gray-50 transition-colors", children: [_jsx("span", { className: "w-3 h-3 rounded-sm shrink-0", style: { backgroundColor: l.color } }), _jsx("span", { className: "text-xs text-gray-700 flex-1 truncate", children: l.name }), attached && (_jsx("span", { className: "text-brand-600 text-xs", children: "\u2713" }))] }, l.id));
                                                                    }) }))] }))] })] }) })] })] }), chartData.length > 1 && (_jsxs("div", { className: "bg-white rounded-2xl border border-gray-100 shadow-sm p-6", children: [_jsx("h2", { className: "text-base font-bold text-gray-900 mb-4", children: "Fiyat Ge\u00E7mi\u015Fi" }), _jsx(ResponsiveContainer, { width: "100%", height: 220, children: _jsxs(LineChart, { data: chartData, margin: { top: 5, right: 10, left: 10, bottom: 5 }, children: [_jsx(CartesianGrid, { strokeDasharray: "3 3", stroke: "#f0f0f0" }), _jsx(XAxis, { dataKey: "date", tick: { fontSize: 11, fill: '#9ca3af' }, tickLine: false, axisLine: false }), _jsx(YAxis, { domain: [minPrice - padding, maxPrice + padding], tick: { fontSize: 11, fill: '#9ca3af' }, tickLine: false, axisLine: false, tickFormatter: v => v.toLocaleString('tr-TR'), width: 70 }), _jsx(Tooltip, { content: _jsx(CustomTooltip, {}) }), _jsx(Line, { type: "monotone", dataKey: "price", stroke: "#2563eb", strokeWidth: 2, dot: { fill: '#2563eb', r: 3 }, activeDot: { r: 5 } })] }) })] })), (p?.priceHistories ?? []).length > 0 && (_jsxs("div", { className: "bg-white rounded-2xl border border-gray-100 shadow-sm overflow-hidden", children: [_jsx("div", { className: "px-6 py-4 border-b border-gray-100", children: _jsx("h2", { className: "text-base font-bold text-gray-900", children: "Kontrol Kay\u0131tlar\u0131" }) }), _jsx("div", { className: "divide-y divide-gray-50", children: [...(p?.priceHistories ?? [])]
                                    .sort((a, b) => new Date(b.checkedAt).getTime() - new Date(a.checkedAt).getTime())
                                    .map((h, i) => (_jsxs("div", { className: "flex items-center justify-between px-6 py-3", children: [_jsx("span", { className: "text-sm text-gray-500", children: fmtDate(h.checkedAt) }), _jsx("span", { className: "text-sm font-semibold text-gray-900", children: fmt(h.price) })] }, i))) })] }))] })] }));
}
