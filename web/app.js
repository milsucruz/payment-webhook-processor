import { renderPayments } from './payments.js';
import { renderPaymentDetail } from './payment-detail.js';
import { el } from './dom.js';

const view = document.getElementById('view');

function route() {
    const path = location.hash.replace(/^#/, '') || '/payments';
    const detail = path.match(/^\/payments\/([^/]+)$/);

    const page = el('div');
    view.replaceChildren(page);
    if (path === '/payments') {
        renderPayments(page);
    } else if (detail) {
        renderPaymentDetail(page, decodeURIComponent(detail[1]));
    } else {
        page.append(el('p', { className: 'error' }, 'Page not found.'));
    }
}

window.addEventListener('hashchange', route);
route();
