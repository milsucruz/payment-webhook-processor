import { el } from './dom.js';

export const PageSize = 20;

export function pager(current, pageCount, onSelect) {
    const nav = el('nav', { className: 'pager', ariaLabel: 'Pages' });
    if (pageCount <= 1) {
        nav.hidden = true;
        return nav;
    }

    nav.append(pageButton('‹ Previous', current - 1, current === 1, onSelect));
    let previous = 0;
    for (const page of visiblePages(current, pageCount)) {
        if (page > previous + 1) {
            nav.append(el('span', { className: 'pager-gap' }, '…'));
        }
        const button = pageButton(String(page), page, false, onSelect);
        if (page === current) {
            button.setAttribute('aria-current', 'page');
        }
        nav.append(button);
        previous = page;
    }
    nav.append(pageButton('Next ›', current + 1, current === pageCount, onSelect));
    return nav;
}

function pageButton(label, page, disabled, onSelect) {
    const button = el('button', { type: 'button', disabled }, label);
    button.addEventListener('click', () => onSelect(page));
    return button;
}

function visiblePages(current, pageCount) {
    const pages = new Set([1, pageCount]);
    for (let page = current - 1; page <= current + 1; page++) {
        if (page >= 1 && page <= pageCount) {
            pages.add(page);
        }
    }
    return [...pages].sort((a, b) => a - b);
}
