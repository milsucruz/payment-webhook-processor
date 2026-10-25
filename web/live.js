const { HubConnectionBuilder, LogLevel } = window.signalR;

const firstRetryDelays = [0, 2000, 10000];
const steadyRetryDelay = 30000;
const hiddenGraceDelay = 60000;

const connection = new HubConnectionBuilder()
    .withUrl('/hubs/payments')
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: context => retryDelay(context.previousRetryCount) })
    .configureLogging(LogLevel.Warning)
    .build();

const reconnectedHandlers = new Set();
const statusElement = document.getElementById('connection-status');
let run = 0;
let paused = false;
let stopping = Promise.resolve();
let pauseTimer = null;

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
    connection.onclose(() => {
        if (!paused) {
            start(true);
        }
    });
    document.addEventListener('visibilitychange', visibilityChanged);
    start(false);
    visibilityChanged();
}

async function start(afterGap) {
    const current = ++run;
    for (let attempt = 0; ; attempt++) {
        setStatus('connecting', 'Connecting…');
        try {
            await connection.start();
            if (current === run) {
                connected(afterGap || attempt > 0);
            }
            return;
        } catch {
            await new Promise(resolve => setTimeout(resolve, retryDelay(attempt)));
            if (current !== run) {
                return;
            }
        }
    }
}

function visibilityChanged() {
    if (document.visibilityState === 'hidden') {
        pauseTimer ??= setTimeout(pause, hiddenGraceDelay);
        return;
    }
    clearTimeout(pauseTimer);
    pauseTimer = null;
    if (paused) {
        resume();
    }
}

function pause() {
    pauseTimer = null;
    paused = true;
    run++;
    setStatus('paused', 'Paused');
    stopping = connection.stop().catch(() => {});
}

async function resume() {
    await stopping;
    if (paused && document.visibilityState === 'visible') {
        paused = false;
        start(true);
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
