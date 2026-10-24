import { getPayment } from './api.js';
import { dateTime, money, statusBadge, statusLabel } from './format.js';
import { el } from './dom.js';

export async function renderPaymentDetail(view, paymentIntentId) {
    view.append(el('p', {}, el('a', { href: '#/payments' }, '← Payments')), el('h1', {}, paymentIntentId));

    let payment;
    try {
        payment = await getPayment(paymentIntentId);
    } catch (error) {
        view.append(el('p', { className: 'error' }, `Could not load the payment: ${error.message}`));
        return;
    }
    if (payment === null) {
        view.append(el('p', { className: 'error' }, 'Payment not found.'));
        return;
    }

    view.append(
        el('dl', { className: 'summary' },
            el('dt', {}, 'Status'), el('dd', {}, statusBadge(payment.status)),
            el('dt', {}, 'Amount'), el('dd', {}, money(payment.amount, payment.currency)),
            el('dt', {}, 'Refunded'), el('dd', {}, money(payment.amountRefunded, payment.currency)),
            el('dt', {}, 'Created'), el('dd', { title: payment.createdAt }, dateTime(payment.createdAt)),
            el('dt', {}, 'Last update'), el('dd', { title: payment.updatedAt }, dateTime(payment.updatedAt))),
        el('h2', {}, 'Timeline'),
        el('ol', { className: 'timeline' }, ...payment.history.map(timelineItem)));
}

function timelineItem(entry) {
    const transition = entry.fromStatus === null
        ? `New payment → ${statusLabel(entry.toStatus)}`
        : `${statusLabel(entry.fromStatus)} → ${statusLabel(entry.toStatus)}`;
    const badge = entry.applied
        ? el('span', { className: 'badge applied' }, 'Applied')
        : el('span', { className: 'badge ignored' }, entry.reason ? `Ignored: ${entry.reason}` : 'Ignored');

    return el('li', { className: entry.applied ? 'applied' : 'ignored' },
        el('div', { className: 'timeline-head' }, badge, el('strong', {}, entry.eventType)),
        el('div', {}, transition),
        el('div', { className: 'muted' },
            el('span', { title: entry.occurredAt }, dateTime(entry.occurredAt)),
            ' · ',
            el('code', {}, entry.eventId)));
}
