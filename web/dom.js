export function el(tag, properties = {}, ...children) {
    const element = document.createElement(tag);
    Object.assign(element, properties);
    element.append(...children);
    return element;
}

export function pageHeader(title, subtitle, ...actions) {
    return el('header', { className: 'page-header' },
        el('div', {}, el('h1', {}, title), el('p', { className: 'muted' }, subtitle)),
        el('div', { className: 'page-actions' }, ...actions));
}
