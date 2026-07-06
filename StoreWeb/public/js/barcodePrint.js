(function () {
    const DEFAULT_WIDTH_MM = 35;
    const DEFAULT_HEIGHT_MM = 25;

    function isMobileLayout() {
        return window.matchMedia("(max-width: 767px)").matches;
    }

    function readLabelSize(options) {
        const widthMm = Math.max(
            20,
            Math.min(
                108,
                options?.widthMm ??
                    parseInt(localStorage.getItem("kaif_label_width_mm") || "", 10) ||
                    DEFAULT_WIDTH_MM
            )
        );
        const heightMm = Math.max(
            15,
            Math.min(
                150,
                options?.heightMm ??
                    parseInt(localStorage.getItem("kaif_label_height_mm") || "", 10) ||
                    DEFAULT_HEIGHT_MM
            )
        );
        return { widthMm, heightMm };
    }

    function renderPrintWindow(w, dataUrl, count, options) {
        const copies = Math.max(1, Math.min(500, parseInt(count, 10) || 1));
        const { widthMm, heightMm } = readLabelSize(options);

        let labels = "";
        for (let i = 0; i < copies; i++) {
            labels += '<div class="label-page"><img src="' + dataUrl + '" alt="Barcode label" /></div>';
        }

        const css =
            "@page{size:" +
            widthMm +
            "mm " +
            heightMm +
            "mm;margin:0;}" +
            "html,body{margin:0;padding:0;}" +
            ".hint{padding:12px 14px;font:13px/1.45 system-ui,sans-serif;color:#444;max-width:28rem;}" +
            ".hint strong{display:block;margin-bottom:6px;color:#111;}" +
            ".label-page{box-sizing:border-box;overflow:hidden;}" +
            ".label-page img{display:block;width:" +
            widthMm +
            "mm;height:" +
            heightMm +
            "mm;margin:0;border:0;object-fit:fill;}" +
            "@media screen{" +
            "body{padding:12px;}" +
            ".label-page{width:" +
            widthMm +
            "mm;height:" +
            heightMm +
            "mm;margin:0 0 10px;border:1px dashed #bbb;}" +
            "}" +
            "@media print{" +
            ".hint{display:none!important;}" +
            ".label-page{width:" +
            widthMm +
            "mm;height:" +
            heightMm +
            "mm;page-break-after:always;break-after:page;}" +
            ".label-page:last-child{page-break-after:auto;break-after:auto;}" +
            "}";

        const hint =
            '<div class="hint"><strong>Label printer tips</strong>' +
            "Each label is one page (" +
            widthMm +
            "×" +
            heightMm +
            " mm). In the print dialog: margins <strong>None</strong>, scale <strong>100%</strong>, headers/footers <strong>off</strong>. " +
            "In the printer driver (Printing preferences), set paper/label size to <strong>" +
            widthMm +
            "×" +
            heightMm +
            " mm</strong> — a mismatch feeds blank labels or skips stickers.</div>";

        w.document.open();
        w.document.write(
            '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Barcode labels</title>' +
            "<style>" +
            css +
            "</style></head><body>" +
            hint +
            labels +
            "</body></html>"
        );
        w.document.close();

        const imgs = w.document.querySelectorAll("img");
        if (imgs.length === 0) {
            return false;
        }

        let loaded = 0;
        const tryPrint = function () {
            loaded++;
            if (loaded >= imgs.length) {
                w.focus();
                w.print();
            }
        };

        imgs.forEach(function (img) {
            if (img.complete) {
                tryPrint();
            } else {
                img.onload = tryPrint;
                img.onerror = tryPrint;
            }
        });
        return true;
    }

    /** Optional: storePrintBarcodeLabelSize(35, 25) from browser console or settings UI. */
    window.storePrintBarcodeLabelSize = function (widthMm, heightMm) {
        localStorage.setItem("kaif_label_width_mm", String(Math.max(20, Math.min(108, widthMm))));
        localStorage.setItem("kaif_label_height_mm", String(Math.max(15, Math.min(150, heightMm))));
    };

    /** Open the print window immediately from a click handler (before any await). */
    window.storePrintBarcodeBegin = function () {
        if (isMobileLayout()) {
            return null;
        }
        const w = window.open("", "_blank");
        if (!w) {
            return null;
        }
        w.document.write(
            '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Barcode labels</title>' +
            '<style>body{margin:0;font:14px/1.5 system-ui,sans-serif;padding:24px;color:#444;}</style></head>' +
            "<body><p>Loading label…</p></body></html>"
        );
        w.document.close();
        return w;
    };

    /** Fill an already-open print window (safe after async fetch). */
    window.storePrintBarcodeFinish = function (w, dataUrl, count, options) {
        if (!w || w.closed) {
            return false;
        }
        try {
            return renderPrintWindow(w, dataUrl, count, options);
        } catch (err) {
            try {
                w.close();
            } catch (ignore) {}
            return false;
        }
    };

    /** Sync print when the image is already available. */
    window.storePrintBarcode = function (dataUrl, count, options) {
        const w = window.storePrintBarcodeBegin();
        if (!w) {
            return false;
        }
        return window.storePrintBarcodeFinish(w, dataUrl, count, options);
    };
})();
