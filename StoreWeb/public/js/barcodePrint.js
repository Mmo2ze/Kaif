window.storePrintBarcode = (dataUrl, count) => {
    if (window.matchMedia("(max-width: 767px)").matches) {
        return false;
    }
    const copies = Math.max(1, Math.min(500, parseInt(count, 10) || 1));
    const w = window.open("", "_blank");
    if (!w) {
        return false;
    }
    let labels = "";
    for (let i = 0; i < copies; i++) {
        labels += '<div class="label"><img src="' + dataUrl + '" alt="Barcode" /></div>';
    }
    w.document.write(
        '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Barcode</title>' +
        '<style>' +
        'body{margin:0;padding:12px;font-family:system-ui,sans-serif;}' +
        '.labels{display:flex;flex-wrap:wrap;gap:8px;justify-content:flex-start;}' +
        '.label{width:2in;padding:8px;box-sizing:border-box;text-align:center;page-break-inside:avoid;}' +
        '.label img{max-width:100%;height:auto;display:block;margin:0 auto;}' +
        '@media print{.label{break-inside:avoid;}}' +
        '</style></head><body><div class="labels">' + labels + '</div></body></html>');
    w.document.close();
    const imgs = w.document.querySelectorAll("img");
    if (imgs.length === 0) {
        return false;
    }
    let loaded = 0;
    const tryPrint = () => {
        loaded++;
        if (loaded >= imgs.length) {
            w.focus();
            w.print();
        }
    };
    imgs.forEach((img) => {
        if (img.complete) {
            tryPrint();
        } else {
            img.onload = tryPrint;
            img.onerror = tryPrint;
        }
    });
    return true;
};
