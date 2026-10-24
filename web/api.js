export async function listPayments(cursor) {
    const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : '';
    return getJson(`/api/payments${query}`);
}

export async function getPayment(paymentIntentId) {
    return getJson(`/api/payments/${encodeURIComponent(paymentIntentId)}`);
}

export async function listDeadLetters() {
    return getJson('/api/dead-letters');
}

export async function reprocessDeadLetter(sequenceNumber, adminKey) {
    const response = await fetch(`/api/dead-letters/${sequenceNumber}/reprocess`, {
        method: 'POST',
        headers: { 'X-Admin-Key': adminKey },
    });
    return response.status;
}

async function getJson(url) {
    const response = await fetch(url);
    if (response.status === 404) {
        return null;
    }
    if (!response.ok) {
        throw new Error(`${url} answered ${response.status}`);
    }
    return response.json();
}
