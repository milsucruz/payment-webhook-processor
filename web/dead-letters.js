import { listDeadLetters, reprocessDeadLetter } from './api.js';
import { dateTime } from './format.js';
import { onHub, onReconnected } from './live.js';
import { el } from './dom.js';

let adminKey = '';

export function renderDeadLetters(view, signal) {
    const keyInput = el('input', { type: 'password', value: adminKey, autocomplete: 'off', placeholder: 'Needed to reprocess' });
    const message = el('p');
    const rows = el('tbody');
    const refresh = el('button', { type: 'button' }, 'Refresh');
    let generation = 0;
    let lastDeadLettered = null;

    async function load() {
        const current = ++generation;
        refresh.disabled = true;
        try {
            const deadLetters = await listDeadLetters();
            if (current !== generation) {
                return;
            }
            rows.replaceChildren(...deadLetters.map(deadLetterRow));
            if (deadLetters.length === 0) {
                rows.append(el('tr', {}, el('td', { colSpan: 7, className: 'muted' }, 'The dead-letter is empty.')));
            }
        } catch (error) {
            if (current === generation) {
                showMessage('error', `Could not load the dead-letter: ${error.message}`);
            }
        } finally {
            if (current === generation) {
                refresh.disabled = false;
            }
        }
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
                showMessage('', `Event ${deadLetter.eventId} is back in the queue.`);
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
            el('td', {}, deadLetter.reason ?? ''),
            el('td', {}, deadLetter.description ?? ''),
            el('td', {}, deadLetter.lastFailureReason ?? ''),
            el('td', { className: 'number' }, String(deadLetter.deliveryCount)),
            el('td', {}, button));
    }

    function showMessage(className, text) {
        message.className = className;
        message.textContent = text;
    }

    keyInput.addEventListener('input', () => {
        adminKey = keyInput.value;
    });
    refresh.addEventListener('click', load);
    onHub('QueueStats', stats => {
        if (lastDeadLettered !== null && stats.deadLetteredMessages !== lastDeadLettered) {
            load();
        }
        lastDeadLettered = stats.deadLetteredMessages;
    }, signal);
    onReconnected(load, signal);

    view.append(
        el('h1', {}, 'Dead-letter'),
        el('label', { className: 'admin-key' }, 'Admin key ', keyInput),
        message,
        el('table', {},
            el('thead', {}, el('tr', {},
                el('th', {}, 'Enqueued'),
                el('th', {}, 'Event'),
                el('th', {}, 'Reason'),
                el('th', {}, 'Description'),
                el('th', {}, 'Last failure'),
                el('th', { className: 'number' }, 'Deliveries'),
                el('th', {}))),
            rows),
        refresh);

    load();
}
