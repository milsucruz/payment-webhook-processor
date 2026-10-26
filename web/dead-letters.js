import { listDeadLetters, reprocessDeadLetter } from './api.js';
import { dateTime } from './format.js';
import { onHub, onReconnected } from './live.js';
import { PageSize, pager } from './pager.js';
import { el, pageHeader } from './dom.js';

let adminKey = '';

export function renderDeadLetters(view, signal) {
    const keyInput = el('input', { type: 'password', value: adminKey, autocomplete: 'off', placeholder: 'Needed to reprocess' });
    const message = el('p', { hidden: true });
    const rows = el('tbody', {}, emptyRow('Loading…'));
    const footer = el('div', { className: 'card-footer' });
    const refresh = el('button', { type: 'button' }, 'Refresh');
    let deadLetters = [];
    let current = 1;
    let generation = 0;
    let lastDeadLettered = null;
    let loadFailed = false;

    async function load() {
        const loading = ++generation;
        refresh.disabled = true;
        try {
            const result = await listDeadLetters();
            if (loading !== generation) {
                return;
            }
            if (loadFailed) {
                showMessage('', '');
                loadFailed = false;
            }
            deadLetters = result;
            showPage(current);
        } catch (error) {
            if (loading === generation) {
                if (deadLetters.length === 0) {
                    rows.replaceChildren();
                }
                showMessage('error', `Could not load the dead-letter: ${error.message}`);
                loadFailed = true;
            }
        } finally {
            if (loading === generation) {
                refresh.disabled = false;
            }
        }
    }

    function showPage(page) {
        const pageCount = Math.max(1, Math.ceil(deadLetters.length / PageSize));
        current = Math.min(page, pageCount);
        const start = (current - 1) * PageSize;
        rows.replaceChildren(...deadLetters.slice(start, start + PageSize).map(deadLetterRow));
        if (deadLetters.length === 0) {
            rows.append(emptyRow('The dead-letter is empty.'));
        }
        footer.replaceChildren(pager(current, pageCount, showPage));
    }

    async function reprocess(deadLetter, button) {
        if (adminKey === '') {
            showMessage('error', 'Enter the admin key to reprocess.');
            return;
        }
        button.disabled = true;
        try {
            const status = await reprocessDeadLetter(deadLetter.sequenceNumber, adminKey);
            if (status === 202) {
                showMessage('success', `Event ${deadLetter.eventId} is back in the queue.`);
                load();
            } else if (status === 401) {
                showMessage('error', 'Wrong admin key.');
                button.disabled = false;
            } else if (status === 404) {
                showMessage('error', `Event ${deadLetter.eventId} is no longer in the dead-letter.`);
                load();
            } else {
                showMessage('error', `Reprocess answered ${status}.`);
                button.disabled = false;
            }
        } catch (error) {
            showMessage('error', `Could not reprocess: ${error.message}`);
            button.disabled = false;
        }
    }

    function deadLetterRow(deadLetter) {
        const button = el('button', { type: 'button' }, 'Reprocess');
        button.addEventListener('click', () => reprocess(deadLetter, button));
        return el('tr', {},
            el('td', { title: deadLetter.enqueuedAt }, dateTime(deadLetter.enqueuedAt)),
            el('td', {}, el('code', {}, deadLetter.eventId)),
            el('td', {}, el('span', { className: 'status danger' }, deadLetter.reason ?? '')),
            el('td', { className: 'long-text' }, deadLetter.description ?? ''),
            el('td', { className: 'long-text' }, deadLetter.lastFailureReason ?? ''),
            el('td', { className: 'number' }, String(deadLetter.deliveryCount)),
            el('td', {}, button));
    }

    function showMessage(tone, text) {
        message.className = `banner ${tone}`;
        message.textContent = text;
        message.hidden = text === '';
    }

    keyInput.addEventListener('input', () => {
        adminKey = keyInput.value;
    });
    refresh.addEventListener('click', load);
    onHub('QueueStats', stats => {
        if (stats.deadLetteredMessages !== lastDeadLettered) {
            load();
        }
        lastDeadLettered = stats.deadLetteredMessages;
    }, signal);
    onReconnected(load, signal);

    view.append(
        pageHeader('Dead-letter', 'Messages the worker gave up on, oldest first. Reprocess sends one back to the queue.',
            el('label', { className: 'admin-key' }, 'Admin key', keyInput),
            refresh),
        message,
        el('section', { className: 'card' },
            el('div', { className: 'table-wrap' }, el('table', {},
                el('thead', {}, el('tr', {},
                    el('th', {}, 'Enqueued'),
                    el('th', {}, 'Event'),
                    el('th', {}, 'Reason'),
                    el('th', {}, 'Description'),
                    el('th', {}, 'Last failure'),
                    el('th', { className: 'number' }, 'Deliveries'),
                    el('th', {}))),
                rows)),
            footer));

    load();
}

function emptyRow(text) {
    return el('tr', {}, el('td', { colSpan: 7, className: 'muted' }, text));
}
