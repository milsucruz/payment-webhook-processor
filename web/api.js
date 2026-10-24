export async function listPayments(cursor) {
    const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : '';
    return getJson(`/api/payments${query}`);
}

export async function getPayment(paymentIntentId) {
    return getJson(`/api/payments/${encodeURIComponent(paymentIntentId)}`);
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
