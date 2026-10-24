import { listPayments } from './api.js';
import { dateTime, money, statusLabel } from './format.js';
import { el } from './dom.js';

export function renderPayments(view) {
    const rows = el('tbody');
    const loadMore = el('button', { type: 'button', hidden: true }, 'Load more');
    const message = el('p');
    let nextCursor = null;

    async function loadPage() {
        loadMore.disabled = true;
        message.textContent = '';
        try {
            const page = await listPayments(nextCursor);
            rows.append(...page.items.map(paymentRow));
            nextCursor = page.nextCursor;
            loadMore.hidden = nextCursor === null;
            if (rows.childElementCount === 0) {
                message.textContent = 'No payments yet.';
            }
        } catch (error) {
            message.className = 'error';
            message.textContent = `Could not load the payments: ${error.message}`;
        } finally {
            loadMore.disabled = false;
        }
    }

    loadMore.addEventListener('click', loadPage);

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
        el('td', {}, el('span', { className: `status status-${payment.status}` }, statusLabel(payment.status))),
        el('td', { className: 'number' }, money(payment.amount, payment.currency)),
        el('td', { className: 'number' }, money(payment.amountRefunded, payment.currency)),
        el('td', { title: payment.updatedAt }, dateTime(payment.updatedAt)));
}
