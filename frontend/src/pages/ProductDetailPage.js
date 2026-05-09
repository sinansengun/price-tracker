import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
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
    return (_jsxs("span", { className: "inline-flex items-center gap-1.5 text-sm font-medium bg-brand-100 text-brand-700 px-2.5 py-1 rounded-full", children: [faviconUrl && _jsx("img", { src: faviconUrl, alt: "", className: "w-5 h-5 rounded-sm" }), store] }));
}
import { LineChart, Line, ResponsiveContainer, Tooltip, YAxis } from 'recharts';
import { getProduct, checkProduct, deleteProduct, getLabels, createLabel, addProductLabel, removeProductLabel, flattenProduct, getProductScrapeErrors } from '../api/api';
// ── Helpers ───────────────────────────────────────────────────────────────
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
function fmtDate(iso) {
    return new Date(iso).toLocaleString('tr-TR', {
        day: '2-digit', month: 'short', year: 'numeric',
        hour: '2-digit', minute: '2-digit',
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
function DetailMiniChart({ points }) {
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
    return (_jsx("div", { className: "w-full rounded-lg border border-dashed border-gray-300 bg-white/70 px-2 py-2", children: _jsx(ResponsiveContainer, { width: "100%", height: 108, children: _jsxs(LineChart, { data: points, children: [_jsx(YAxis, { hide: true, domain: [yMin, yMax] }), _jsx(Line, { type: "linear", dataKey: "v", stroke: color, strokeWidth: 1.8, isAnimationActive: false, connectNulls: true, dot: false, activeDot: false }), _jsx(Tooltip, { content: ({ active, payload }) => active && payload?.length && payload[0].value != null
                            ? _jsx("div", { className: "bg-white border border-gray-200 rounded px-2 py-1 text-xs shadow", children: fmt(payload[0].value) })
                            : null })] }) }) }));
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
    const [showActionMenu, setShowActionMenu] = useState(false);
    const [showLabelPanel, setShowLabelPanel] = useState(false);
    const [scrapeErrors, setScrapeErrors] = useState([]);
    const [newLabelName, setNewLabelName] = useState('');
    const [newLabelColor, setNewLabelColor] = useState('#6366f1');
    const [labelSaving, setLabelSaving] = useState(false);
    const actionMenuRef = useRef(null);
    const labelDropdownRef = useRef(null);
    // Close menus on outside click
    useEffect(() => {
        if (!showLabelPanel && !showActionMenu)
            return;
        const handler = (e) => {
            if (actionMenuRef.current && !actionMenuRef.current.contains(e.target)) {
                setShowActionMenu(false);
            }
            if (labelDropdownRef.current && !labelDropdownRef.current.contains(e.target))
                setShowLabelPanel(false);
        };
        document.addEventListener('mousedown', handler);
        return () => document.removeEventListener('mousedown', handler);
    }, [showLabelPanel, showActionMenu]);
    const load = async () => {
        try {
            const [prodRes, lblRes] = await Promise.all([getProduct(Number(id)), getLabels()]);
            setProduct(prodRes.data);
            setFlatProduct(flattenProduct(prodRes.data));
            setAllLabels(lblRes.data);
            try {
                const scrapeRes = await getProductScrapeErrors(Number(id), 10);
                setScrapeErrors(scrapeRes.data);
            }
            catch {
                setScrapeErrors([]);
            }
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
    const handleDelete = async () => {
        if (!confirm('Bu ürünü silmek istediğinize emin misiniz?'))
            return;
        setDeleting(true);
        await deleteProduct(Number(id));
        navigate('/');
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
    const detailChartPoints = buildLast30DaySeries(p?.priceHistories);
    const hasChartData = detailChartPoints.some(point => point.v != null);
    return (_jsxs("div", { className: "min-h-screen", children: [_jsx("header", { className: "bg-white border-b border-gray-200 sticky top-0 z-10", children: _jsxs("div", { className: "max-w-4xl mx-auto px-4 py-4 flex items-center gap-3", children: [_jsx("button", { onClick: () => navigate(-1), className: "text-gray-500 hover:text-gray-900 transition-colors p-1 -ml-1 rounded-lg hover:bg-gray-100", children: "\u2190" }), _jsx("h1", { className: "text-lg font-bold text-gray-900 truncate", children: p.name })] }) }), _jsxs("main", { className: "max-w-4xl mx-auto px-4 py-8 space-y-6", children: [_jsxs("div", { className: "bg-white rounded-2xl border border-gray-100 shadow-sm p-6 flex flex-col sm:flex-row gap-6", children: [_jsx("div", { className: "shrink-0 w-full sm:w-64 h-72 sm:h-64 bg-gray-50 rounded-xl flex items-center justify-center overflow-hidden", children: imgSrc ? (_jsx("img", { src: imgSrc, alt: p.name, className: "w-full h-full object-contain p-4" })) : (_jsx("span", { className: "text-4xl", children: "\uD83D\uDECD\uFE0F" })) }), _jsxs("div", { className: "flex-1 min-w-0 space-y-3 sm:min-h-48 flex flex-col relative pr-10", children: [_jsxs("div", { className: "absolute right-0 top-0", ref: actionMenuRef, children: [_jsx("button", { onClick: () => setShowActionMenu(v => !v), className: "h-8 w-8 rounded-lg border border-gray-200 text-gray-500 hover:text-gray-800 hover:bg-gray-100 transition-colors", title: "Aksiyonlar", children: "\u22EF" }), showActionMenu && (_jsxs("div", { className: "absolute right-0 mt-1.5 z-50 w-48 bg-white border border-gray-200 rounded-lg shadow-lg py-1", children: [_jsx("button", { onClick: () => { setShowActionMenu(false); handleCheck(); }, disabled: checking, className: "w-full px-3 py-2 text-left text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-50", children: checking ? 'Kontrol ediliyor...' : '🔄 Fiyatı Kontrol Et' }), _jsx("button", { onClick: () => { setShowActionMenu(false); handleDelete(); }, disabled: deleting, className: "w-full px-3 py-2 text-left text-sm text-red-600 hover:bg-red-50 disabled:opacity-50", children: deleting ? 'Siliniyor...' : '🗑 Ürünü Sil' })] }))] }), _jsxs("div", { className: "flex items-start gap-2 flex-wrap", children: [p.store && _jsx(StoreBadge, { store: p.store, url: p.url }), _jsx("a", { href: p.url, target: "_blank", rel: "noopener noreferrer", className: "text-xs text-gray-400 hover:text-brand-600 transition-colors truncate max-w-xs", children: "\u00DCr\u00FCn sayfas\u0131 \u2192" })] }), _jsx("p", { className: "text-base font-semibold text-gray-900 leading-snug", children: p.name }), _jsxs("div", { className: "flex items-center gap-3 flex-wrap", children: [p.currentPrice != null && (_jsx("span", { className: "text-2xl font-bold text-gray-900", children: _jsx(PriceText, { price: p.currentPrice }) })), priceChange !== null && Math.abs(priceChange) >= 0.01 && (_jsxs("span", { className: `inline-flex items-center gap-0.5 text-sm font-semibold px-2 py-0.5 rounded-full ${priceChange > 0 ? 'bg-red-100 text-red-600' : 'bg-green-100 text-green-600'}`, children: [priceChange > 0 ? '▲' : '▼', " ", Math.abs(priceChange).toFixed(1), "%"] }))] }), _jsxs("div", { className: "flex flex-col items-start gap-1 text-xs text-gray-500", children: [_jsxs("span", { children: ["Fiyat: ", _jsx("strong", { className: "text-gray-700", children: p.currentPrice != null ? _jsx(PriceText, { price: p.currentPrice }) : '-' })] }), _jsxs("span", { children: ["Ekleme tarihi: ", _jsx("strong", { className: "text-gray-700", children: fmtDate(p.createdAt) })] }), _jsxs("span", { children: ["Hedef: ", _jsx("strong", { className: "text-gray-700", children: p.targetPrice != null ? _jsx(PriceText, { price: p.targetPrice }) : '-' })] })] }), _jsx("div", { className: "pt-1", children: _jsxs("div", { className: "flex items-center gap-1.5 flex-wrap relative", children: [(p.labels ?? []).map(l => (_jsxs("span", { className: "inline-flex items-center gap-1 text-[10px] font-bold uppercase tracking-wide px-2 py-1 rounded", style: { backgroundColor: l.color + '1A', color: l.color }, children: [l.name, _jsx("button", { onClick: () => handleToggleLabel(l), className: "ml-0.5 opacity-60 hover:opacity-100 transition-opacity leading-none text-xs", style: { color: l.color }, title: "Label'\u0131 kald\u0131r", children: "\u00D7" })] }, l.id))), _jsxs("div", { className: "relative", ref: labelDropdownRef, children: [_jsx("button", { onClick: () => setShowLabelPanel(v => !v), className: "text-[10px] font-bold uppercase tracking-wide text-gray-400 bg-gray-100 hover:bg-gray-200 px-2 py-1 rounded transition-colors", children: "+ Label" }), showLabelPanel && (_jsxs("div", { className: "absolute left-0 top-full mt-1.5 z-50 w-64 bg-white border border-gray-200 rounded-lg shadow-lg", children: [_jsxs("form", { onSubmit: handleCreateLabel, className: "flex items-center gap-1.5 p-2 border-b border-gray-100", children: [_jsx("input", { value: newLabelName, onChange: e => setNewLabelName(e.target.value), placeholder: "Label ara veya olu\u015Ftur...", autoFocus: true, className: "flex-1 min-w-0 text-xs px-2 py-1.5 border border-gray-200 rounded focus:outline-none focus:ring-2 focus:ring-brand-400" }), _jsx("input", { type: "color", value: newLabelColor, onChange: e => setNewLabelColor(e.target.value), className: "w-7 h-7 rounded border border-gray-200 cursor-pointer p-0.5 shrink-0", title: "Renk se\u00E7" }), _jsx("button", { type: "submit", disabled: labelSaving || !newLabelName.trim(), className: "text-xs bg-brand-600 text-white px-2.5 py-1.5 rounded hover:bg-brand-700 transition-colors disabled:opacity-40 font-medium shrink-0", children: labelSaving ? '...' : 'Ekle' })] }), allLabels.length > 0 && (_jsx("div", { className: "max-h-48 overflow-y-auto py-1", children: allLabels
                                                                        .filter(l => !newLabelName.trim() || l.name.toLowerCase().includes(newLabelName.toLowerCase()))
                                                                        .map(l => {
                                                                        const attached = flatProduct?.labels?.some((pl) => pl.id === l.id);
                                                                        return (_jsxs("button", { onClick: () => handleToggleLabel(l), className: "w-full flex items-center gap-2 px-3 py-1.5 text-left hover:bg-gray-50 transition-colors", children: [_jsx("span", { className: "w-3 h-3 rounded-sm shrink-0", style: { backgroundColor: l.color } }), _jsx("span", { className: "text-xs text-gray-700 flex-1 truncate", children: l.name }), attached && (_jsx("span", { className: "text-brand-600 text-xs", children: "\u2713" }))] }, l.id));
                                                                    }) }))] }))] })] }) }), hasChartData && (_jsxs("div", { className: "mt-2 w-full sm:w-80 self-start", children: [_jsxs("div", { className: "mb-1 flex items-center justify-between gap-2", children: [_jsx("h3", { className: "text-sm font-bold text-gray-900 text-left", children: "Fiyat Ge\u00E7mi\u015Fi" }), _jsx("p", { className: "text-[10px] font-semibold uppercase tracking-wide text-gray-400 text-right", children: "Son 1 ay" })] }), _jsx(DetailMiniChart, { points: detailChartPoints })] }))] })] }), _jsxs("div", { className: "bg-white rounded-2xl border border-gray-100 shadow-sm p-6", children: [_jsxs("div", { className: "mb-3 flex items-center justify-between gap-2", children: [_jsx("h2", { className: "text-sm font-bold text-gray-900", children: "Son Scrape Hatalar\u0131" }), _jsx("p", { className: "text-[10px] font-semibold uppercase tracking-wide text-gray-400", children: "Son 10 kay\u0131t" })] }), scrapeErrors.length === 0 ? (_jsx("p", { className: "text-xs text-gray-500", children: "Son d\u00F6nemde scrape hatas\u0131 g\u00F6r\u00FCnm\u00FCyor." })) : (_jsx("div", { className: "space-y-2", children: scrapeErrors.map(item => (_jsxs("div", { className: "rounded-lg border border-red-100 bg-red-50 px-3 py-2", children: [_jsxs("div", { className: "flex items-center justify-between gap-3", children: [_jsx("p", { className: "text-xs font-semibold text-red-700", children: item.reason }), _jsx("p", { className: "text-[11px] text-red-600", children: fmtDate(item.attemptedAt) })] }), _jsx("p", { className: "mt-1 text-xs text-red-700", children: item.message }), _jsxs("div", { className: "mt-1 flex items-center gap-2 text-[11px] text-red-500", children: [item.scraper && _jsxs("span", { children: ["scraper: ", item.scraper] }), item.checkRunId && _jsxs("span", { children: ["run: ", item.checkRunId.slice(0, 8)] }), item.issueUrl && (_jsx("a", { href: item.issueUrl, target: "_blank", rel: "noopener noreferrer", className: "ml-auto hover:underline", children: "Sentry \u2192" }))] })] }, item.eventId + item.attemptedAt))) }))] })] })] }));
}
