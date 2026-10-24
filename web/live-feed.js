import { onHub, onReconnected } from './live.js';
import { el } from './dom.js';

const maxEntries = 100;
const entries = [];
const counters = { total: 0, Applied: 0, IgnoredOutOfOrder: 0, Redundant: 0, Duplicate: 0 };
const changeHandlers = new Set();
let queueStats = null;

const outcomeBadges = {
    Applied: ['applied', 'Applied'],
    IgnoredOutOfOrder: ['ignored', 'Ignored: out of order'],
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
    const counterList = el('dl', { className: 'counters' });
    const queueLine = el('p', { className: 'muted' });
    const feed = el('ol', { className: 'feed' });

    function render() {
        counterList.replaceChildren(
            ...counter('Events', counters.total),
            ...counter('Applied', counters.Applied),
            ...counter('Out of order', counters.IgnoredOutOfOrder),
            ...counter('Redundant', counters.Redundant),
            ...counter('Duplicates', counters.Duplicate));
        queueLine.textContent = queueStats === null
            ? 'Queue: waiting for the first count…'
            : `Queue: ${queueCount(queueStats.activeMessages)} waiting · ${queueCount(queueStats.deadLetteredMessages)} in the dead-letter`;
        feed.replaceChildren(...entries.map(feedItem));
        if (entries.length === 0) {
            feed.append(el('li', { className: 'muted' }, 'No events since this page opened. Events show up here as the worker processes them.'));
        }
    }

    changeHandlers.add(render);
    signal.addEventListener('abort', () => changeHandlers.delete(render));

    view.append(el('h1', {}, 'Live'), counterList, queueLine, feed);
    render();
}

function add(entry) {
    entries.unshift(entry);
    entries.length = Math.min(entries.length, maxEntries);
    changed();
}

function changed() {
    changeHandlers.forEach(handler => handler());
}

function counter(label, value) {
    return [el('dt', {}, label), el('dd', {}, String(value))];
}

function feedItem(entry) {
    const time = el('span', { className: 'muted', title: entry.receivedAt.toISOString() }, entry.receivedAt.toLocaleTimeString());
    if (entry.gap) {
        return el('li', { className: 'gap' }, time, ' Reconnected: events processed while disconnected are not shown here.');
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
