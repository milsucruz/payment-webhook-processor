const { HubConnectionBuilder, LogLevel } = window.signalR;

const firstRetryDelays = [0, 2000, 10000];
const steadyRetryDelay = 30000;

const connection = new HubConnectionBuilder()
    .withUrl('/hubs/payments')
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: context => retryDelay(context.previousRetryCount) })
    .configureLogging(LogLevel.Warning)
    .build();

const reconnectedHandlers = new Set();
const statusElement = document.getElementById('connection-status');

export function onHub(method, handler, signal) {
    connection.on(method, handler);
    signal?.addEventListener('abort', () => connection.off(method, handler));
}

export function onReconnected(handler, signal) {
    reconnectedHandlers.add(handler);
    signal?.addEventListener('abort', () => reconnectedHandlers.delete(handler));
}

export function startConnection() {
    connection.onreconnecting(() => setStatus('reconnecting', 'Reconnecting…'));
    connection.onreconnected(() => connected(true));
    connection.onclose(() => start(true));
    start(false);
}

async function start(afterGap) {
    for (let attempt = 0; ; attempt++) {
        setStatus('connecting', 'Connecting…');
        try {
            await connection.start();
            connected(afterGap || attempt > 0);
            return;
        } catch {
            await new Promise(resolve => setTimeout(resolve, retryDelay(attempt)));
        }
    }
}

function connected(afterGap) {
    setStatus('live', 'Live');
    if (afterGap) {
        reconnectedHandlers.forEach(handler => handler());
    }
}

function retryDelay(previousRetries) {
    return firstRetryDelays[previousRetries] ?? steadyRetryDelay;
}

function setStatus(state, text) {
    statusElement.className = `connection ${state}`;
    statusElement.textContent = text;
}
