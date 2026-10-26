import { onHub, onReconnected } from './live.js';
import { PageSize, pager } from './pager.js';
import { el, pageHeader } from './dom.js';

const maxEntries = 100;
const entries = [];
const counters = { total: 0, Applied: 0, IgnoredOutOfOrder: 0, Redundant: 0, Duplicate: 0 };
const changeHandlers = new Set();
let queueStats = null;

const outcomeBadges = {
    Applied: ['applied', 'Applied'],
    IgnoredOutOfOrder: ['ignored', 'Out of order'],
    Redundant: ['ignored', 'Redundant'],
    Duplicate: ['duplicate', 'Duplicate'],
};

export function startLiveFeed() {
    onHub('EventProcessed', event => {
        counters.total++;
        counters[event.outcome] = (counters[event.outcome] ?? 0) + 1;
        add({ event, receivedAt: new Date() });
    });
    onHub('QueueStats', stats => {
        queueStats = stats;
        changed();
    });
    onReconnected(() => add({ gap: true, receivedAt: new Date() }));
}

export function queueCount(count) {
    return queueStats !== null && count >= queueStats.countLimit ? `${queueStats.countLimit}+` : String(count);
}

export function renderLive(view, signal) {
    const metrics = el('div', { className: 'metrics' });
    const feed = el('ol', { className: 'feed' });
    const goToFirst = el('button', { type: 'button', className: 'link-button' }, 'Back to page 1');
    const notice = el('p', { className: 'notice', hidden: true }, '', goToFirst);
    const footer = el('div', { className: 'card-footer' });
    let current = 1;
    let frozen = null;
    let missed = 0;

    function render() {
        metrics.replaceChildren(
            metric('Events', counters.total, ''),
            metric('Applied', counters.Applied, 'success'),
            metric('Out of order', counters.IgnoredOutOfOrder, 'warning'),
            metric('Redundant', counters.Redundant, 'warning'),
            metric('Duplicates', counters.Duplicate, ''),
            metric('Queue waiting', queueStats === null ? '…' : queueCount(queueStats.activeMessages), ''),
            metric('Dead-letter', queueStats === null ? '…' : queueCount(queueStats.deadLetteredMessages),
                queueStats?.deadLetteredMessages > 0 ? 'danger' : ''));

        const list = frozen ?? entries;
        const pageCount = Math.max(1, Math.ceil(list.length / PageSize));
        const start = (current - 1) * PageSize;
        feed.replaceChildren(...list.slice(start, start + PageSize).map(feedItem));
        if (list.length === 0) {
            feed.append(el('li', { className: 'empty' }, 'No events since this page opened. Events show up here as the worker processes them.'));
        }
        notice.hidden = missed === 0;
        notice.firstChild.textContent = `${missed} new ${missed === 1 ? 'event' : 'events'} since you left page 1. `;
        footer.replaceChildren(pager(current, pageCount, showPage));
    }

    function showPage(page) {
        if (page === 1) {
            frozen = null;
            missed = 0;
        } else if (frozen === null) {
            frozen = entries.slice();
        }
        current = page;
        render();
    }

    function onChange(entry) {
        if (entry && frozen !== null) {
            missed++;
        }
        render();
    }

    goToFirst.addEventListener('click', () => showPage(1));
    changeHandlers.add(onChange);
    signal.addEventListener('abort', () => changeHandlers.delete(onChange));

    view.append(
        pageHeader('Live', 'Events as the worker processes them, counted since this page opened'),
        metrics,
        el('section', { className: 'card' },
            el('h2', { className: 'card-title' }, `Latest events (up to ${maxEntries})`),
            notice,
            feed,
            footer));
    render();
}

function add(entry) {
    entries.unshift(entry);
    entries.length = Math.min(entries.length, maxEntries);
    changed(entry);
}

function changed(entry) {
    changeHandlers.forEach(handler => handler(entry));
}

function metric(label, value, tone) {
    return el('div', { className: `metric ${tone}` }, el('span', { className: 'metric-label' }, label), el('span', { className: 'metric-value' }, String(value)));
}

function feedItem(entry) {
    const time = el('time', { className: 'muted', title: entry.receivedAt.toISOString() }, entry.receivedAt.toLocaleTimeString());
    if (entry.gap) {
        return el('li', { className: 'gap' }, time, el('span', {}, 'Reconnected: events processed while disconnected are not shown here.'));
    }

    const { event } = entry;
    const [className, label] = outcomeBadges[event.outcome] ?? ['ignored', event.outcome];
    return el('li', { className },
        time,
        el('span', { className: `badge ${className}` }, label),
        el('strong', {}, event.eventType),
        el('a', { href: `#/payments/${encodeURIComponent(event.paymentIntentId)}` }, event.paymentIntentId),
        el('code', { className: 'muted' }, event.eventId));
}
