/// <reference types="vite/client" />

interface Window {
  storePrintBarcode?: (dataUrl: string, count: number, options?: { widthMm?: number; heightMm?: number }) => boolean;
  storePrintBarcodeBegin?: () => Window | null;
  storePrintBarcodeFinish?: (
    w: Window,
    dataUrl: string,
    count: number,
    options?: { widthMm?: number; heightMm?: number }
  ) => boolean;
  storePrintBarcodeLabelSize?: (widthMm: number, heightMm: number) => void;
}
