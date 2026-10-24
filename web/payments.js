import { listPayments } from './api.js';
import { dateTime, money, statusBadge } from './format.js';
import { onHub, onReconnected } from './live.js';
import { el } from './dom.js';

export function renderPayments(view, signal) {
    const rows = el('tbody');
    const loadMore = el('button', { type: 'button', hidden: true }, 'Load more');
    const message = el('p');
    const shown = new Map();
    let nextCursor = null;
    let generation = 0;

    async function loadPage() {
        const current = generation;
        loadMore.disabled = true;
        message.textContent = '';
        try {
            const page = await listPayments(nextCursor);
            if (current !== generation) {
                return;
            }
            page.items.forEach(payment => show(payment, false));
            nextCursor = page.nextCursor;
            loadMore.textContent = 'Load more';
            loadMore.hidden = nextCursor === null;
            message.className = '';
            if (rows.childElementCount === 0) {
                message.textContent = 'No payments yet.';
            }
        } catch (error) {
            if (current !== generation) {
                return;
            }
            message.className = 'error';
            message.textContent = `Could not load the payments: ${error.message}`;
            loadMore.textContent = 'Retry';
            loadMore.hidden = false;
        } finally {
            if (current === generation) {
                loadMore.disabled = false;
            }
        }
    }

    function reload() {
        generation++;
        shown.clear();
        rows.replaceChildren();
        nextCursor = null;
        loadPage();
    }

    function show(payment, pushed) {
        const updatedAt = Date.parse(payment.updatedAt);
        const existing = shown.get(payment.paymentIntentId);
        if (existing && existing.updatedAt > updatedAt) {
            return;
        }

        const row = paymentRow(payment);
        shown.set(payment.paymentIntentId, { row, updatedAt });
        if (pushed) {
            existing?.row.remove();
            rows.prepend(row);
            if (message.className !== 'error') {
                message.textContent = '';
            }
        } else if (existing) {
            existing.row.replaceWith(row);
        } else {
            rows.append(row);
        }
    }

    loadMore.addEventListener('click', loadPage);
    onHub('PaymentUpdated', payment => show(payment, true), signal);
    onReconnected(reload, signal);

    view.append(
        el('h1', {}, 'Payments'),
        el('table', {},
            el('thead', {}, el('tr', {},
                el('th', {}, 'Payment'),
                el('th', {}, 'Status'),
                el('th', { className: 'number' }, 'Amount'),
                el('th', { className: 'number' }, 'Refunded'),
                el('th', {}, 'Last update'))),
            rows),
        message,
        loadMore);

    loadPage();
}

function paymentRow(payment) {
    return el('tr', {},
        el('td', {}, el('a', { href: `#/payments/${encodeURIComponent(payment.paymentIntentId)}` }, payment.paymentIntentId)),
        el('td', {}, statusBadge(payment.status)),
        el('td', { className: 'number' }, money(payment.amount, payment.currency)),
        el('td', { className: 'number' }, money(payment.amountRefunded, payment.currency)),
        el('td', { title: payment.updatedAt }, dateTime(payment.updatedAt)));
}
