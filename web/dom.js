export function el(tag, properties = {}, ...children) {
    const element = document.createElement(tag);
    Object.assign(element, properties);
    element.append(...children);
    return element;
}
