import { renderLive, startLiveFeed } from './live-feed.js';
import { renderPayments } from './payments.js';
import { renderPaymentDetail } from './payment-detail.js';
import { renderDeadLetters } from './dead-letters.js';
import { startConnection } from './live.js';
import { el } from './dom.js';

const view = document.getElementById('view');
let pageController = null;

function route() {
    const path = location.hash.replace(/^#/, '') || '/live';
    const detail = path.match(/^\/payments\/([^/]+)$/);
    const paymentIntentId = detail ? decodeSegment(detail[1]) : null;

    markCurrentLink(path);
    pageController?.abort();
    pageController = new AbortController();
    const page = el('div');
    view.replaceChildren(page);
    if (path === '/live') {
        renderLive(page, pageController.signal);
    } else if (path === '/payments') {
        renderPayments(page, pageController.signal);
    } else if (paymentIntentId !== null) {
        renderPaymentDetail(page, paymentIntentId, pageController.signal);
    } else if (path === '/dead-letters') {
        renderDeadLetters(page, pageController.signal);
    } else {
        page.append(el('p', { className: 'error' }, 'Page not found.'));
    }
}

function markCurrentLink(path) {
    document.querySelectorAll('nav a').forEach(link => {
        const section = link.getAttribute('href').replace(/^#/, '');
        if (path === section || path.startsWith(`${section}/`)) {
            link.setAttribute('aria-current', 'page');
        } else {
            link.removeAttribute('aria-current');
        }
    });
}

function decodeSegment(segment) {
    try {
        return decodeURIComponent(segment);
    } catch {
        return null;
    }
}

startLiveFeed();
window.addEventListener('hashchange', route);
route();
startConnection();
