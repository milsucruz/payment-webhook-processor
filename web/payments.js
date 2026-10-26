import { listPayments } from './api.js';
import { dateTime, money, statusBadge } from './format.js';
import { onHub, onReconnected } from './live.js';
import { PageSize, pager } from './pager.js';
import { el, pageHeader } from './dom.js';

export function renderPayments(view, signal) {
    const rows = el('tbody');
    const message = el('p', { className: 'card-message' });
    const retry = el('button', { type: 'button', hidden: true }, 'Retry');
    const goToFirst = el('button', { type: 'button', className: 'link-button' }, 'Go to page 1');
    const notice = el('p', { className: 'notice', hidden: true }, 'Payments were updated on page 1. ', goToFirst);
    const footer = el('div', { className: 'card-footer' });
    const shown = new Map();
    const cursors = [null];
    let current = 1;
    let generation = 0;

    async function loadPage(page) {
        const loading = ++generation;
        current = page;
        retry.hidden = true;
        notice.hidden = true;
        message.className = 'card-message muted';
        message.textContent = 'Loading…';
        try {
            const result = await listPayments(cursors[page - 1], PageSize);
            if (loading !== generation) {
                return;
            }
            if (cursors[page] !== result.nextCursor) {
                cursors.length = page;
                if (result.nextCursor !== null) {
                    cursors.push(result.nextCursor);
                }
            }
            shown.clear();
            rows.replaceChildren();
            result.items.forEach(payment => show(payment, false));
            message.className = 'card-message muted';
            message.textContent = rows.childElementCount > 0 ? '' : page === 1 ? 'No payments yet.' : 'No payments on this page anymore.';
            footer.replaceChildren(pager(current, cursors.length, loadPage));
        } catch (error) {
            if (loading !== generation) {
                return;
            }
            message.className = 'card-message error';
            message.textContent = `Could not load the payments: ${error.message}`;
            retry.hidden = false;
        }
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
            if (!message.classList.contains('error')) {
                message.textContent = '';
            }
        } else if (existing) {
            existing.row.replaceWith(row);
        } else {
            rows.append(row);
        }
    }

    function pushed(payment) {
        if (current === 1) {
            show(payment, true);
        } else {
            notice.hidden = false;
        }
    }

    retry.addEventListener('click', () => loadPage(current));
    goToFirst.addEventListener('click', () => loadPage(1));
    onHub('PaymentUpdated', pushed, signal);
    onReconnected(() => loadPage(current), signal);

    view.append(
        pageHeader('Payments', `Most recently updated first, ${PageSize} per page`),
        el('section', { className: 'card' },
            notice,
            el('div', { className: 'table-wrap' }, el('table', {},
                el('thead', {}, el('tr', {},
                    el('th', {}, 'Payment'),
                    el('th', {}, 'Status'),
                    el('th', { className: 'number' }, 'Amount'),
                    el('th', { className: 'number' }, 'Refunded'),
                    el('th', {}, 'Last update'))),
                rows)),
            message,
            retry,
            footer));

    loadPage(1);
}

function paymentRow(payment) {
    return el('tr', {},
        el('td', {}, el('a', { href: `#/payments/${encodeURIComponent(payment.paymentIntentId)}` }, payment.paymentIntentId)),
        el('td', {}, statusBadge(payment.status)),
        el('td', { className: 'number' }, money(payment.amount, payment.currency)),
        el('td', { className: 'number' }, money(payment.amountRefunded, payment.currency)),
        el('td', { title: payment.updatedAt }, dateTime(payment.updatedAt)));
}
