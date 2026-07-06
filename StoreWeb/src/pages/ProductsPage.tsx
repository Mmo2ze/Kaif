import { useCallback, useEffect, useState } from 'react';
import * as api from '../api';
import { useAuth } from '../auth/AuthContext';
import { useSettings } from '../settings/SettingsContext';
import type { ProductModelSummaryDto } from '../types';
import { formatMoney } from '../utils/money';
import { intValue, parseIntInput, type IntInput } from '../utils/numberInput';

function parsePrice(raw: string): number {
  const n = parseFloat(raw);
  return Number.isFinite(n) && n >= 0 ? n : 0;
}

function isOnSale(unitPrice: number, salePrice: number | null): boolean {
  return salePrice != null && salePrice > 0 && salePrice < unitPrice;
}

interface PriceEdit {
  productId: number;
  title: string;
  buy: string;
  unit: string;
  sale: string;
}

export function ProductsPage() {
  const { isAdmin } = useAuth();
  const settings = useSettings();
  const [products, setProducts] = useState<ProductModelSummaryDto[] | null>(null);
  const [filter, setFilter] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const [showAdd, setShowAdd] = useState(false);
  const [newName, setNewName] = useState('');
  const [newBuy, setNewBuy] = useState('');
  const [newUnit, setNewUnit] = useState('');
  const [newSale, setNewSale] = useState('');
  const [newStock, setNewStock] = useState<IntInput>(0);
  const [busyAdd, setBusyAdd] = useState(false);

  const [editingId, setEditingId] = useState<number | null>(null);
  const [editQty, setEditQty] = useState<IntInput>(0);
  const [addingId, setAddingId] = useState<number | null>(null);
  const [addQtyByProduct, setAddQtyByProduct] = useState<Record<number, number>>({});
  const [printBarcode, setPrintBarcode] = useState<string | null>(null);
  const [printStatus, setPrintStatus] = useState<string | null>(null);

  const [priceEdit, setPriceEdit] = useState<PriceEdit | null>(null);
  const [renameId, setRenameId] = useState<number | null>(null);
  const [renameName, setRenameName] = useState('');
  const [exportBusy, setExportBusy] = useState(false);
  const [importBusy, setImportBusy] = useState(false);

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setProducts(await api.getProducts());
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Load failed');
      setProducts([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const filtered = (products ?? []).filter((p) =>
    !filter.trim() || p.name.toLowerCase().includes(filter.trim().toLowerCase()),
  );

  const rowClass = (row: ProductModelSummaryDto) => {
    if (row.stock === 0) return 'stock-zero';
    if (settings.lowStockThreshold > 0 && row.stock < settings.lowStockThreshold) return 'stock-low';
    return '';
  };

  const createProduct = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusyAdd(true);
    try {
      const sale = parsePrice(newSale);
      await api.createProduct({
        name: newName.trim(),
        buyPrice: parsePrice(newBuy),
        unitPrice: parsePrice(newUnit),
        salePrice: sale > 0 ? sale : null,
        initialStock: Math.max(0, intValue(newStock)),
      });
      setNewName('');
      setNewBuy('');
      setNewUnit('');
      setNewSale('');
      setNewStock(0);
      setShowAdd(false);
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed');
    } finally {
      setBusyAdd(false);
    }
  };

  const saveStock = async (productId: number) => {
    try {
      await api.setProductStock(productId, { quantity: Math.max(0, intValue(editQty)) });
      setEditingId(null);
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed');
    }
  };

  const addStock = async (row: ProductModelSummaryDto) => {
    const qty = addQtyByProduct[row.id] ?? 1;
    setAddingId(row.id);
    try {
      await api.addProductStock(row.id, { quantity: qty });
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Add failed');
    } finally {
      setAddingId(null);
    }
  };

  const printLabel = async (barcode: string) => {
    const raw = window.prompt('How many labels?', '1');
    if (raw === null) return;
    const count = Math.max(1, Math.min(500, parseInt(raw, 10) || 1));

    setPrintBarcode(barcode);
    setPrintStatus(null);
    setError(null);
    try {
      const result = await api.enqueueLabelPrint({ barcode, count });

      for (let i = 0; i < 15; i++) {
        await new Promise((r) => setTimeout(r, 2000));
        try {
          const stillPending = await api.isLabelPrintJobPending(result.jobId);
          if (!stillPending) {
            setPrintStatus(`Sent ${count} label${count === 1 ? '' : 's'} to the barcode printer.`);
            return;
          }
        } catch {
          /* keep waiting */
        }
      }

      setPrintStatus(
        'Print queued. Keep Store POS open, logged in, with a barcode printer saved in Settings.',
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Print failed');
    } finally {
      setPrintBarcode(null);
    }
  };

  const savePrices = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!priceEdit) return;
    const sale = parsePrice(priceEdit.sale);
    try {
      await api.updateProductPrice(priceEdit.productId, {
        buyPrice: parsePrice(priceEdit.buy),
        unitPrice: parsePrice(priceEdit.unit),
        salePrice: sale > 0 ? sale : null,
      });
      setPriceEdit(null);
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed');
    }
  };

  const saveRename = async (e: React.FormEvent) => {
    e.preventDefault();
    if (renameId == null) return;
    try {
      await api.updateProduct(renameId, { name: renameName.trim() });
      setRenameId(null);
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed');
    }
  };

  const deleteProduct = async (row: ProductModelSummaryDto) => {
    if (!window.confirm(`Delete "${row.name}"?`)) return;
    try {
      await api.deleteProduct(row.id);
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Delete failed');
    }
  };

  const exportCatalog = async () => {
    setExportBusy(true);
    try {
      const blob = await api.exportProductsCatalog();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `catalog-export-${new Date().toISOString().slice(0, 10)}.json`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Export failed');
    } finally {
      setExportBusy(false);
    }
  };

  const importCatalog = async (file: File) => {
    if (!window.confirm('Import products from this file?')) return;
    setImportBusy(true);
    try {
      await api.importProductsCatalog(file);
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Import failed');
    } finally {
      setImportBusy(false);
    }
  };

  return (
    <div className="page-pad">
      <h1>Products</h1>
      <p className="muted">
        {isAdmin
          ? 'Add products with name, prices, and starting stock. Print sends labels to the barcode printer via Store POS (must be open and logged in).'
          : 'View stock and print labels via Store POS (must be open and logged in with a barcode printer configured).'}
      </p>
      {error && <p className="error">{error}</p>}
      {printStatus && <p className="muted">{printStatus}</p>}

      {isAdmin && (
        <div className="admin-toolbar filter-bar">
          <button type="button" className="btn secondary touch-target" onClick={() => setShowAdd((v) => !v)}>
            {showAdd ? 'Hide add product' : 'Add product'}
          </button>
          <button type="button" className="btn secondary touch-target" disabled={exportBusy} onClick={() => void exportCatalog()}>
            {exportBusy ? 'Exporting…' : 'Export'}
          </button>
          <label className="btn secondary touch-target" style={{ margin: 0 }}>
            {importBusy ? 'Importing…' : 'Import'}
            <input
              type="file"
              accept=".json,application/json"
              style={{ display: 'none' }}
              disabled={importBusy}
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) void importCatalog(f);
                e.target.value = '';
              }}
            />
          </label>
        </div>
      )}

      {isAdmin && showAdd && (
        <form className="admin-card stack-form" onSubmit={(e) => void createProduct(e)}>
          <h2>New product</h2>
          <label htmlFor="new-name">Name</label>
          <input id="new-name" className="input touch-input" value={newName} onChange={(e) => setNewName(e.target.value)} required />
          <label htmlFor="new-buy">Buy price ({settings.currencyLabel})</label>
          <input id="new-buy" type="number" min={0} step="0.01" className="input touch-input" value={newBuy} onChange={(e) => setNewBuy(e.target.value)} />
          <label htmlFor="new-unit">Sell price ({settings.currencyLabel})</label>
          <input id="new-unit" type="number" min={0} step="0.01" className="input touch-input" value={newUnit} onChange={(e) => setNewUnit(e.target.value)} />
          <label htmlFor="new-sale">Sale price ({settings.currencyLabel}, optional)</label>
          <input id="new-sale" type="number" min={0} step="0.01" className="input touch-input" value={newSale} onChange={(e) => setNewSale(e.target.value)} />
          <label htmlFor="new-stock">Initial stock</label>
          <input id="new-stock" type="number" min={0} className="input touch-input" value={newStock} onChange={(e) => setNewStock(parseIntInput(e.target.value))} />
          <button type="submit" className="btn primary touch-target" disabled={busyAdd}>
            {busyAdd ? 'Saving…' : 'Save product'}
          </button>
        </form>
      )}

      <div className="stock-toolbar filter-bar">
        <input className="input touch-input" placeholder="Search name…" value={filter} onChange={(e) => setFilter(e.target.value)} />
        <button type="button" className="btn primary touch-target" disabled={loading} onClick={() => void reload()}>
          {loading ? 'Loading…' : 'Refresh'}
        </button>
      </div>

      {products === null ? (
        <p className="muted">Loading…</p>
      ) : filtered.length === 0 ? (
        <p className="muted">No products yet.</p>
      ) : (
        <div className="stock-cards">
          {filtered.map((row) => (
            <div key={row.id} className={`stock-card ${rowClass(row)}`}>
              <div className="stock-card-head">
                <strong>{row.name}</strong>
                <span className="muted">
                  Buy {formatMoney(row.buyPrice, settings.currencyLabel)} · Sell{' '}
                  {isOnSale(row.unitPrice, row.salePrice) ? (
                    <>
                      <span className="price-strike">{formatMoney(row.unitPrice, settings.currencyLabel)}</span>{' '}
                      {formatMoney(row.salePrice!, settings.currencyLabel)}
                    </>
                  ) : (
                    formatMoney(row.unitPrice, settings.currencyLabel)
                  )}
                </span>
              </div>
              {row.barcodePngBase64 && (
                <div className="stock-card-barcode">
                  <img src={`data:image/png;base64,${row.barcodePngBase64}`} className="stock-barcode-thumb" alt="" />
                  <span className="mono muted">{row.barcode}</span>
                  <button type="button" className="btn-link" disabled={printBarcode === row.barcode} onClick={() => void printLabel(row.barcode)}>
                    {printBarcode === row.barcode ? 'Printing…' : 'Print'}
                  </button>
                </div>
              )}
              <div className="stock-card-row">
                <span>Stock</span>
                {isAdmin && editingId === row.id ? (
                  <input
                    type="number"
                    min={0}
                    className="input inline-num touch-input"
                    value={editQty}
                    onChange={(e) => setEditQty(parseIntInput(e.target.value))}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') void saveStock(row.id);
                      if (e.key === 'Escape') setEditingId(null);
                    }}
                  />
                ) : isAdmin ? (
                  <button type="button" className="stock-card-qty-btn" disabled={editingId != null} onClick={() => { setEditingId(row.id); setEditQty(row.stock); }}>
                    {row.stock}
                  </button>
                ) : (
                  <strong>{row.stock}</strong>
                )}
              </div>
              {isAdmin && (
                <div className="stock-card-actions">
                  <button type="button" className="btn secondary btn-sm touch-target" disabled={addingId === row.id} onClick={() => void addStock(row)}>
                    {addingId === row.id ? '…' : '+ Add'}
                  </button>
                  <input
                    type="number"
                    min={1}
                    className="input inline-num touch-input"
                    value={addQtyByProduct[row.id] ?? 1}
                    onChange={(e) => setAddQtyByProduct((m) => ({ ...m, [row.id]: Math.max(1, parseInt(e.target.value, 10) || 1) }))}
                  />
                  <button type="button" className="btn-link touch-target" onClick={() => setPriceEdit({ productId: row.id, title: row.name, buy: String(row.buyPrice), unit: String(row.unitPrice), sale: row.salePrice != null ? String(row.salePrice) : '' })}>
                    Prices
                  </button>
                  <button type="button" className="btn-link touch-target" onClick={() => { setRenameId(row.id); setRenameName(row.name); }}>
                    Rename
                  </button>
                  <button type="button" className="btn-link danger-link touch-target" onClick={() => void deleteProduct(row)}>
                    Delete
                  </button>
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {priceEdit && (
        <div className="modal-backdrop" onClick={() => setPriceEdit(null)}>
          <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
            <h2>Edit prices — {priceEdit.title}</h2>
            <form className="stack-form" onSubmit={(e) => void savePrices(e)}>
              <label>Buy price</label>
              <input type="number" min={0} step="0.01" className="input touch-input" value={priceEdit.buy} onChange={(e) => setPriceEdit({ ...priceEdit, buy: e.target.value })} />
              <label>Sell price</label>
              <input type="number" min={0} step="0.01" className="input touch-input" value={priceEdit.unit} onChange={(e) => setPriceEdit({ ...priceEdit, unit: e.target.value })} />
              <label>Sale price (optional)</label>
              <input type="number" min={0} step="0.01" className="input touch-input" value={priceEdit.sale} onChange={(e) => setPriceEdit({ ...priceEdit, sale: e.target.value })} />
              <button type="submit" className="btn primary touch-target">Save</button>
            </form>
          </div>
        </div>
      )}

      {renameId != null && (
        <div className="modal-backdrop" onClick={() => setRenameId(null)}>
          <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
            <h2>Rename product</h2>
            <form className="stack-form" onSubmit={(e) => void saveRename(e)}>
              <input className="input touch-input" value={renameName} onChange={(e) => setRenameName(e.target.value)} required />
              <button type="submit" className="btn primary touch-target">Save</button>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
