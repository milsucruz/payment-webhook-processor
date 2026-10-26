import { getPayment } from './api.js';
import { dateTime, money, statusBadge, statusLabel } from './format.js';
import { onHub, onReconnected } from './live.js';
import { el } from './dom.js';

export function renderPaymentDetail(view, paymentIntentId, signal) {
    const badge = el('span');
    const content = el('div', {}, el('p', { className: 'muted' }, 'Loading…'));
    let generation = 0;

    async function load() {
        const current = ++generation;
        let payment;
        try {
            payment = await getPayment(paymentIntentId);
        } catch (error) {
            if (current === generation) {
                content.replaceChildren(el('p', { className: 'banner error' }, `Could not load the payment: ${error.message}`));
            }
            return;
        }
        if (current !== generation) {
            return;
        }
        if (payment === null) {
            content.replaceChildren(el('p', { className: 'banner error' }, 'Payment not found.'));
            return;
        }

        badge.replaceChildren(statusBadge(payment.status));
        content.replaceChildren(
            el('dl', { className: 'metrics' },
                fact('Amount', money(payment.amount, payment.currency)),
                fact('Refunded', money(payment.amountRefunded, payment.currency)),
                fact('Created', dateTime(payment.createdAt), payment.createdAt),
                fact('Last update', dateTime(payment.updatedAt), payment.updatedAt)),
            el('section', { className: 'card' },
                el('h2', { className: 'card-title' }, `Timeline (${payment.history.length} ${payment.history.length === 1 ? 'event' : 'events'})`),
                el('ol', { className: 'timeline' }, ...payment.history.map(timelineItem))));
    }

    onHub('EventProcessed', event => {
        if (event.paymentIntentId === paymentIntentId && event.outcome !== 'Duplicate') {
            load();
        }
    }, signal);
    onReconnected(load, signal);

    view.append(
        el('p', { className: 'back' }, el('a', { href: '#/payments' }, '← Payments')),
        el('header', { className: 'page-header' },
            el('div', {},
                el('div', { className: 'title-row' }, el('h1', { className: 'payment-id' }, paymentIntentId), badge),
                el('p', { className: 'muted' }, 'Every event received for this payment, in processing order'))),
        content);
    load();
}

function fact(label, value, title) {
    return el('div', { className: 'metric' },
        el('dt', { className: 'metric-label' }, label),
        el('dd', { className: 'metric-value small', title: title ?? '' }, value));
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
            el('code', {}, entry.eventId),
            ...(entry.traceId ? [' · trace ', el('code', { title: 'Search this operation ID in Application Insights' }, entry.traceId)] : [])));
}
