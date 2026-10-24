import { renderPayments } from './payments.js';
import { renderPaymentDetail } from './payment-detail.js';
import { el } from './dom.js';

const view = document.getElementById('view');

function route() {
    const path = location.hash.replace(/^#/, '') || '/payments';
    const detail = path.match(/^\/payments\/([^/]+)$/);
    const paymentIntentId = detail ? decodeSegment(detail[1]) : null;

    const page = el('div');
    view.replaceChildren(page);
    if (path === '/payments') {
        renderPayments(page);
    } else if (paymentIntentId !== null) {
        renderPaymentDetail(page, paymentIntentId);
    } else {
        page.append(el('p', { className: 'error' }, 'Page not found.'));
    }
}

function decodeSegment(segment) {
    try {
        return decodeURIComponent(segment);
    } catch {
        return null;
    }
}

window.addEventListener('hashchange', route);
route();
