import { getPayment } from './api.js';
import { dateTime, money, statusBadge, statusLabel } from './format.js';
import { onHub, onReconnected } from './live.js';
import { el } from './dom.js';

export function renderPaymentDetail(view, paymentIntentId, signal) {
    const content = el('div');
    let generation = 0;

    async function load() {
        const current = ++generation;
        let payment;
        try {
            payment = await getPayment(paymentIntentId);
        } catch (error) {
            if (current === generation) {
                content.replaceChildren(el('p', { className: 'error' }, `Could not load the payment: ${error.message}`));
            }
            return;
        }
        if (current !== generation) {
            return;
        }
        if (payment === null) {
            content.replaceChildren(el('p', { className: 'error' }, 'Payment not found.'));
            return;
        }

        content.replaceChildren(
            el('dl', { className: 'summary' },
                el('dt', {}, 'Status'), el('dd', {}, statusBadge(payment.status)),
                el('dt', {}, 'Amount'), el('dd', {}, money(payment.amount, payment.currency)),
                el('dt', {}, 'Refunded'), el('dd', {}, money(payment.amountRefunded, payment.currency)),
                el('dt', {}, 'Created'), el('dd', { title: payment.createdAt }, dateTime(payment.createdAt)),
                el('dt', {}, 'Last update'), el('dd', { title: payment.updatedAt }, dateTime(payment.updatedAt))),
            el('h2', {}, 'Timeline'),
            el('ol', { className: 'timeline' }, ...payment.history.map(timelineItem)));
    }

    onHub('EventProcessed', event => {
        if (event.paymentIntentId === paymentIntentId && event.outcome !== 'Duplicate') {
            load();
        }
    }, signal);
    onReconnected(load, signal);

    view.append(el('p', {}, el('a', { href: '#/payments' }, '← Payments')), el('h1', {}, paymentIntentId), content);
    load();
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
