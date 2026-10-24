import { el } from './dom.js';

const statusLabels = {
    Created: 'Created',
    RequiresAction: 'Requires action',
    Processing: 'Processing',
    Failed: 'Failed',
    Paid: 'Paid',
    Canceled: 'Canceled',
    PartiallyRefunded: 'Partially refunded',
    Refunded: 'Refunded',
};

export function statusLabel(status) {
    return statusLabels[status] ?? status;
}

export function statusBadge(status) {
    return el('span', { className: 'status' }, statusLabel(status));
}

export function money(amountInMinorUnits, currency) {
    const format = new Intl.NumberFormat(undefined, { style: 'currency', currency: currency.toUpperCase() });
    const fractionDigits = format.resolvedOptions().maximumFractionDigits;
    return format.format(amountInMinorUnits / 10 ** fractionDigits);
}

export function dateTime(isoText) {
    return new Date(isoText).toLocaleString();
}
