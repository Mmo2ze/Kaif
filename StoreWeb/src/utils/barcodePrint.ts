import { isMobileLayout } from './mobile';

export function barcodePrintFailureMessage(): string {
  if (typeof window.storePrintBarcodeBegin !== 'function') {
    return 'Print script did not load. Hard-refresh the page (Cmd+Shift+R).';
  }
  if (isMobileLayout()) {
    return 'Label printing needs a wider window. Use desktop layout or rotate your device.';
  }
  return 'Could not open the print window. Allow pop-ups for this site, then try again.';
}

/** Call synchronously from a click handler, before any await. */
export function beginBarcodePrint(): Window | null {
  return window.storePrintBarcodeBegin?.() ?? null;
}

/** Call after loading the label image (async is OK here). */
export function finishBarcodePrint(
  printWin: Window,
  dataUrl: string,
  count: number,
  options?: { widthMm?: number; heightMm?: number }
): boolean {
  return window.storePrintBarcodeFinish?.(printWin, dataUrl, count, options) ?? false;
}

/** When the image is already loaded (no await before print). */
export function printBarcodeNow(
  dataUrl: string,
  count: number,
  options?: { widthMm?: number; heightMm?: number }
): boolean {
  return window.storePrintBarcode?.(dataUrl, count, options) ?? false;
}
