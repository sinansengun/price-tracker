import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { useEffect, useMemo, useState } from 'react';
import { updateAlertSettings } from '../api/api';
function modeCardClass(selected) {
    return selected
        ? 'border-brand-500 bg-brand-50 shadow-sm'
        : 'border-gray-200 bg-white hover:border-gray-300';
}
export default function AlertSettingsModal({ product, onClose, onSaved }) {
    const [alertMode, setAlertMode] = useState(product.alertMode ?? 'automatic');
    const [discountThresholdPercent, setDiscountThresholdPercent] = useState(product.discountThresholdPercent != null ? String(product.discountThresholdPercent).replace('.', ',') : '');
    const [targetPrice, setTargetPrice] = useState(product.targetPrice != null ? String(product.targetPrice).replace('.', ',') : '');
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    useEffect(() => {
        setAlertMode(product.alertMode ?? 'automatic');
        setDiscountThresholdPercent(product.discountThresholdPercent != null ? String(product.discountThresholdPercent).replace('.', ',') : '');
        setTargetPrice(product.targetPrice != null ? String(product.targetPrice).replace('.', ',') : '');
        setError('');
    }, [product]);
    const helperText = useMemo(() => {
        switch (alertMode) {
            case 'percentage':
                return 'Yüzde alarmı, son 7 günün en düşük fiyatını baz alır.';
            case 'target_price':
                return 'Fiyat belirlediğin tutara indiğinde ya da altına düştüğünde tetiklenir.';
            default:
                return 'Otomatik mod mevcut davranışı korur ve yeterli düşüşlerde bildirir.';
        }
    }, [alertMode]);
    const handleSubmit = async (e) => {
        e.preventDefault();
        setError('');
        let parsedDiscountThreshold = null;
        let parsedTargetPrice = null;
        if (alertMode === 'percentage') {
            parsedDiscountThreshold = Number.parseFloat(discountThresholdPercent.replace(',', '.'));
            if (!Number.isFinite(parsedDiscountThreshold)) {
                setError('İndirim yüzdesi gerekli.');
                return;
            }
            if (parsedDiscountThreshold <= 0 || parsedDiscountThreshold > 100) {
                setError('İndirim yüzdesi 0 ile 100 arasında olmalı.');
                return;
            }
        }
        if (alertMode === 'target_price') {
            parsedTargetPrice = Number.parseFloat(targetPrice.replace(',', '.'));
            if (!Number.isFinite(parsedTargetPrice)) {
                setError('Hedef fiyat gerekli.');
                return;
            }
            if (parsedTargetPrice <= 0) {
                setError('Hedef fiyat 0\'dan büyük olmalı.');
                return;
            }
        }
        setSaving(true);
        try {
            await updateAlertSettings(product.id, {
                alertMode,
                discountThresholdPercent: parsedDiscountThreshold,
                targetPrice: parsedTargetPrice,
            });
            await onSaved();
            onClose();
        }
        catch (err) {
            setError(err?.response?.data?.error ?? 'Alarm ayarı güncellenemedi.');
        }
        finally {
            setSaving(false);
        }
    };
    return (_jsx("div", { className: "fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm", onClick: onClose, children: _jsxs("div", { className: "bg-white rounded-2xl shadow-xl w-full max-w-xl mx-4 p-6", onClick: e => e.stopPropagation(), children: [_jsxs("div", { className: "mb-5", children: [_jsx("h2", { className: "text-lg font-bold text-gray-900", children: "Alarm Ayar\u0131" }), _jsx("p", { className: "mt-1 text-sm text-gray-500", children: "Yeni \u00FCr\u00FCnler otomatik takip ile ba\u015Flar. \u0130stersen y\u00FCzde indirim ya da hedef fiyat alarm\u0131na ge\u00E7ebilirsin." })] }), _jsxs("form", { onSubmit: handleSubmit, className: "space-y-4", children: [_jsx("button", { type: "button", onClick: () => setAlertMode('automatic'), className: `w-full rounded-xl border p-4 text-left transition-colors ${modeCardClass(alertMode === 'automatic')}`, children: _jsxs("div", { className: "flex items-start gap-3", children: [_jsx("span", { className: "mt-0.5 text-brand-600", children: alertMode === 'automatic' ? '◉' : '○' }), _jsxs("div", { children: [_jsx("div", { className: "text-sm font-semibold text-gray-900", children: "Otomatik takip" }), _jsx("div", { className: "mt-1 text-sm text-gray-500", children: "Mevcut davran\u0131\u015F: fiyat yeni 7 g\u00FCnl\u00FCk dip seviyeye yeterince indi\u011Finde bildir." })] })] }) }), _jsx("button", { type: "button", onClick: () => setAlertMode('percentage'), className: `w-full rounded-xl border p-4 text-left transition-colors ${modeCardClass(alertMode === 'percentage')}`, children: _jsxs("div", { className: "flex items-start gap-3", children: [_jsx("span", { className: "mt-0.5 text-brand-600", children: alertMode === 'percentage' ? '◉' : '○' }), _jsxs("div", { children: [_jsx("div", { className: "text-sm font-semibold text-gray-900", children: "Belirli indirim y\u00FCzdesi" }), _jsx("div", { className: "mt-1 text-sm text-gray-500", children: "7 g\u00FCnl\u00FCk en d\u00FC\u015F\u00FCk fiyata g\u00F6re istedi\u011Fin y\u00FCzde kadar d\u00FC\u015F\u00FCnce bildir." })] })] }) }), alertMode === 'percentage' && (_jsxs("div", { children: [_jsx("label", { className: "mb-1 block text-sm font-medium text-gray-700", children: "\u0130ndirim y\u00FCzdesi" }), _jsxs("div", { className: "relative", children: [_jsx("input", { value: discountThresholdPercent, onChange: e => setDiscountThresholdPercent(e.target.value), placeholder: "\u00F6rn. 12", type: "number", min: "0", max: "100", step: "0.1", className: "w-full rounded-lg border border-gray-300 px-3 py-2 pr-10 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500" }), _jsx("span", { className: "absolute right-3 top-1/2 -translate-y-1/2 text-sm text-gray-400", children: "%" })] })] })), _jsx("button", { type: "button", onClick: () => setAlertMode('target_price'), className: `w-full rounded-xl border p-4 text-left transition-colors ${modeCardClass(alertMode === 'target_price')}`, children: _jsxs("div", { className: "flex items-start gap-3", children: [_jsx("span", { className: "mt-0.5 text-brand-600", children: alertMode === 'target_price' ? '◉' : '○' }), _jsxs("div", { children: [_jsx("div", { className: "text-sm font-semibold text-gray-900", children: "Sabit hedef fiyat" }), _jsx("div", { className: "mt-1 text-sm text-gray-500", children: "Fiyat belirledi\u011Fin tutara indi\u011Finde ya da alt\u0131na d\u00FC\u015Ft\u00FC\u011F\u00FCnde bildir." })] })] }) }), alertMode === 'target_price' && (_jsxs("div", { children: [_jsx("label", { className: "mb-1 block text-sm font-medium text-gray-700", children: "Hedef fiyat" }), _jsxs("div", { className: "relative", children: [_jsx("input", { value: targetPrice, onChange: e => setTargetPrice(e.target.value), placeholder: "\u00F6rn. 4500", type: "number", min: "0", step: "0.01", className: "w-full rounded-lg border border-gray-300 px-3 py-2 pr-10 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500" }), _jsx("span", { className: "absolute right-3 top-1/2 -translate-y-1/2 text-sm text-gray-400", children: "\u20BA" })] })] })), _jsx("div", { className: "rounded-xl bg-gray-50 px-3 py-2 text-xs text-gray-500", children: helperText }), error && _jsx("p", { className: "text-sm text-red-600", children: error }), _jsxs("div", { className: "flex gap-3 pt-1", children: [_jsx("button", { type: "button", onClick: onClose, className: "flex-1 rounded-lg border border-gray-300 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50", children: "\u0130ptal" }), _jsx("button", { type: "submit", disabled: saving, className: "flex-1 rounded-lg bg-brand-600 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700 disabled:opacity-60", children: saving ? 'Kaydediliyor...' : 'Kaydet' })] })] })] }) }));
}
