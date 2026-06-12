/// <reference types="vite/client" />

interface Window {
  storePrintBarcode?: (dataUrl: string, count: number) => boolean;
}
