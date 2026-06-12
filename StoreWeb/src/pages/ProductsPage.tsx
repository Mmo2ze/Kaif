import { useEffect, useState } from 'react';
import * as api from '../api';
import { useMobileLayout } from '../hooks/useMobileLayout';
import { useSettings } from '../settings/SettingsContext';
import type { ClothingSize, ProductModelSummaryDto, ProductSkuListRowDto, SkuDetailDto } from '../types';
import { downloadFile } from '../utils/download';
import { formatMoney } from '../utils/money';
import { intValue, parseIntInput, type IntInput } from '../utils/numberInput';

const SIZES: ClothingSize[] = ['xs', 's', 'm', 'l', 'xl', 'xxl', 'custom'];

function parsePrice(raw: string): number {
  const n = parseFloat(raw);
  return Number.isFinite(n) && n > 0 ? n : 0;
}

function isOnSale(unitPrice: number, salePrice: number | null): boolean {
  return salePrice != null && salePrice > 0 && salePrice < unitPrice;
}

interface PriceEdit {
  modelId: number;
  buy: string;
  unit: string;
  sale: string;
}

interface ModelEdit {
  id: number;
  name: string;
  description: string;
}

export function ProductsPage() {
  const isMobile = useMobileLayout();
  const settings = useSettings();
  const [models, setModels] = useState<ProductModelSummaryDto[] | null>(null);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());
  const [skusByModel, setSkusByModel] = useState<Record<number, ProductSkuListRowDto[]>>({});
  const [skuErrors, setSkuErrors] = useState<Record<number, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [newName, setNewName] = useState('');
  const [newDesc, setNewDesc] = useState('');
  const [newBuyPrice, setNewBuyPrice] = useState('');
  const [newUnitPrice, setNewUnitPrice] = useState('');
  const [newSalePrice, setNewSalePrice] = useState('');
  const [busyModel, setBusyModel] = useState(false);
  const [modelId, setModelId] = useState(0);
  const [size, setSize] = useState<ClothingSize>('m');
  const [stock, setStock] = useState<IntInput>(0);
  const [busySku, setBusySku] = useState(false);
  const [lastSku, setLastSku] = useState<SkuDetailDto | null>(null);
  const [printBarcode, setPrintBarcode] = useState<string | null>(null);
  const [priceEdit, setPriceEdit] = useState<PriceEdit | null>(null);
  const [savingPrice, setSavingPrice] = useState(false);
  const [modelEdit, setModelEdit] = useState<ModelEdit | null>(null);
  const [savingModel, setSavingModel] = useState(false);
  const [deletingModelId, setDeletingModelId] = useState<number | null>(null);
  const [deletingSkuId, setDeletingSkuId] = useState<number | null>(null);
  const [exportBusy, setExportBusy] = useState(false);
  const [importBusy, setImportBusy] = useState(false);

  const reloadModels = async () => {
    setError(null);
    try {
      const list = await api.getProducts();
      setModels(list);
      if (list.length > 0 && modelId === 0) setModelId(list[0].id);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Load failed');
      setModels([]);
    }
  };

  useEffect(() => {
    void reloadModels();
  }, []);

  const loadSkus = async (id: number) => {
    try {
      const rows = await api.getProductSkus(id);
      setSkusByModel((p) => ({ ...p, [id]: rows }));
      setSkuErrors((p) => {
        const n = { ...p };
        delete n[id];
        return n;
      });
    } catch (e) {
      setSkuErrors((p) => ({ ...p, [id]: e instanceof Error ? e.message : 'Load failed' }));
    }
  };

  const toggle = async (id: number) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
    if (!skusByModel[id]) await loadSkus(id);
  };

  const createModel = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusyModel(true);
    try {
      const sale = parsePrice(newSalePrice);
      await api.createProduct({
        name: newName.trim(),
        description: newDesc.trim() || null,
        buyPrice: parsePrice(newBuyPrice),
        unitPrice: parsePrice(newUnitPrice),
        salePrice: sale > 0 ? sale : null,
      });
      setNewName('');
      setNewDesc('');
      setNewBuyPrice('');
      setNewUnitPrice('');
      setNewSalePrice('');
      setSkusByModel({});
      setExpanded(new Set());
      await reloadModels();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed');
    } finally {
      setBusyModel(false);
    }
  };

  const createSku = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusySku(true);
    try {
      const created = await api.createSku({
        productModelId: modelId,
        size,
        stock: Math.max(0, intValue(stock)),
      });
      setLastSku(created);
      await reloadModels();
      if (expanded.has(modelId)) await loadSkus(modelId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed');
    } finally {
      setBusySku(false);
    }
  };

  const saveModelEdit = async () => {
    if (!modelEdit || !modelEdit.name.trim()) return;
    setSavingModel(true);
    setError(null);
    try {
      await api.updateProduct(modelEdit.id, {
        name: modelEdit.name.trim(),
        description: modelEdit.description.trim() || null,
      });
      setModelEdit(null);
      await reloadModels();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed');
    } finally {
      setSavingModel(false);
    }
  };

  const deleteModel = async (m: ProductModelSummaryDto) => {
    if (!window.confirm(`Delete product "${m.name}" and all its sizes? This cannot be undone.`)) return;
    setDeletingModelId(m.id);
    setError(null);
    try {
      await api.deleteProduct(m.id);
      setExpanded((prev) => {
        const next = new Set(prev);
        next.delete(m.id);
        return next;
      });
      setSkusByModel((prev) => {
        const next = { ...prev };
        delete next[m.id];
        return next;
      });
      if (modelEdit?.id === m.id) setModelEdit(null);
      if (priceEdit?.modelId === m.id) setPriceEdit(null);
      await reloadModels();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed');
    } finally {
      setDeletingModelId(null);
    }
  };

  const deleteSku = async (modelId: number, row: ProductSkuListRowDto) => {
    if (!window.confirm(`Delete size ${row.size} (${row.barcode})?`)) return;
    setDeletingSkuId(row.id);
    setError(null);
    try {
      await api.deleteSku(row.id);
      await reloadModels();
      if (expanded.has(modelId)) await loadSkus(modelId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed');
    } finally {
      setDeletingSkuId(null);
    }
  };

  const savePriceEdit = async () => {
    if (!priceEdit) return;
    setSavingPrice(true);
    setError(null);
    try {
      const sale = parsePrice(priceEdit.sale);
      await api.updateProductPrice(priceEdit.modelId, {
        buyPrice: parsePrice(priceEdit.buy),
        unitPrice: parsePrice(priceEdit.unit),
        salePrice: sale > 0 ? sale : null,
      });
      setPriceEdit(null);
      await reloadModels();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed');
    } finally {
      setSavingPrice(false);
    }
  };

  const exportCatalog = async () => {
    setExportBusy(true);
    setError(null);
    try {
      const blob = await api.exportProductsCatalog();
      const buf = await blob.arrayBuffer();
      const b64 = btoa(String.fromCharCode(...new Uint8Array(buf)));
      const fname = `catalog-export-${new Date().toISOString().slice(0, 10).replace(/-/g, '')}.json`;
      if (!downloadFile(fname, b64, 'application/json')) setError('Download failed');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Export failed');
    } finally {
      setExportBusy(false);
    }
  };

  const importCatalog = async (file: File) => {
    if (!window.confirm('Import products from this file? New products/sizes get stock 0. Existing sizes keep their stock. Sales history is not changed.')) return;
    setImportBusy(true);
    setError(null);
    try {
      const result = await api.importProductsCatalog(file);
      setSkusByModel({});
      setExpanded(new Set());
      await reloadModels();
      setError(null);
      window.alert(result.message);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Import failed');
    } finally {
      setImportBusy(false);
    }
  };

  const printForBarcode = async (barcode: string) => {
    if (isMobile) return;
    setPrintBarcode(barcode);
    try {
      const detail = await api.getSkuByBarcode(barcode, true);
      if (!detail.barcodePngBase64) {
        setError('Could not load barcode image.');
        return;
      }
      const ok = window.storePrintBarcode?.(`data:image/png;base64,${detail.barcodePngBase64}`, 1);
      if (!ok) setError('Could not open print window (check pop-up blocker).');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Print failed');
    } finally {
      setPrintBarcode(null);
    }
  };

  return (
    <div className="page-pad">
      <h1 className="page-title">Products</h1>
      {error && <p className="error">{error}</p>}
      <div className="btn-row" style={{ marginBottom: '0.75rem', flexWrap: 'wrap' }}>
        <button type="button" className="btn secondary touch-target" disabled={exportBusy} onClick={() => void exportCatalog()}>
          {exportBusy ? 'Exporting…' : 'Export products'}
        </button>
        <label className="btn secondary touch-target" style={{ margin: 0 }}>
          {importBusy ? 'Importing…' : 'Import products'}
          <input
            type="file"
            accept=".json,application/json"
            disabled={importBusy}
            style={{ display: 'none' }}
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) void importCatalog(f);
              e.target.value = '';
            }}
          />
        </label>
      </div>
      <p className="muted small" style={{ marginBottom: '1rem' }}>
        Export product names and sizes only. Import adds missing products/sizes with stock 0; existing sizes keep their stock. Set prices after import.
      </p>
      <section className="admin-card">
        <h2>Add product model</h2>
        <form onSubmit={(e) => void createModel(e)}>
          <div className="field">
            <label htmlFor="pm-name">Name</label>
            <input id="pm-name" className="input touch-input" value={newName} onChange={(e) => setNewName(e.target.value)} />
          </div>
          <div className="field">
            <label htmlFor="pm-desc">Description (optional)</label>
            <textarea id="pm-desc" className="input touch-input" rows={3} value={newDesc} onChange={(e) => setNewDesc(e.target.value)} />
          </div>
          <div className="field">
            <label htmlFor="pm-buy">Buy price ({settings.currencyLabel})</label>
            <input id="pm-buy" type="number" className="input touch-input" min={0} step="0.01" value={newBuyPrice} onChange={(e) => setNewBuyPrice(e.target.value)} />
          </div>
          <div className="field">
            <label htmlFor="pm-price">Sell price ({settings.currencyLabel})</label>
            <input id="pm-price" type="number" className="input touch-input" min={0} step="0.01" value={newUnitPrice} onChange={(e) => setNewUnitPrice(e.target.value)} />
          </div>
          <div className="field">
            <label htmlFor="pm-sale">Sale price ({settings.currencyLabel}, optional)</label>
            <input id="pm-sale" type="number" className="input touch-input" min={0} step="0.01" value={newSalePrice} onChange={(e) => setNewSalePrice(e.target.value)} />
          </div>
          <button type="submit" className="btn primary touch-target full-width" disabled={busyModel}>
            {busyModel ? 'Saving…' : 'Save model'}
          </button>
        </form>
      </section>
      <section className="admin-card">
        <h2>Add size to model</h2>
        {!models || models.length === 0 ? (
          <p className="muted">Create a product model first.</p>
        ) : (
          <form onSubmit={(e) => void createSku(e)}>
            <div className="field">
              <label htmlFor="sku-model">Model</label>
              <select id="sku-model" className="input touch-input" value={modelId} onChange={(e) => setModelId(parseInt(e.target.value, 10))}>
                {models.map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.name} ({m.skuCount} sizes)
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label htmlFor="sku-size">Size</label>
              <select id="sku-size" className="input touch-input" value={size} onChange={(e) => setSize(e.target.value as ClothingSize)}>
                {SIZES.map((s) => (
                  <option key={s} value={s}>
                    {s.toUpperCase()}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label htmlFor="sku-stock">Initial stock</label>
              <input id="sku-stock" type="number" className="input touch-input" min={0} value={stock} onChange={(e) => setStock(parseIntInput(e.target.value))} />
            </div>
            <p className="muted field-hint">Prices are set once per product — use Edit prices in the catalog.</p>
            <button type="submit" className="btn primary touch-target full-width" disabled={busySku}>
              {busySku ? 'Saving…' : 'Save SKU'}
            </button>
          </form>
        )}
      </section>
      {lastSku && (
        <section className="admin-card highlight">
          <h2>New SKU</h2>
          <p className="mono">{lastSku.barcode}</p>
          {lastSku.barcodePngBase64 && (
            <div className="barcode-block">
              <img src={`data:image/png;base64,${lastSku.barcodePngBase64}`} className="barcode-img" alt="Barcode" />
            </div>
          )}
          {!isMobile && (
            <button type="button" className="btn secondary" onClick={() => lastSku.barcodePngBase64 && void printForBarcode(lastSku.barcode)}>
              Print barcode
            </button>
          )}
        </section>
      )}
      <section className="admin-card">
        <h2>Catalog</h2>
        {!models ? (
          <p>Loading…</p>
        ) : models.length === 0 ? (
          <p className="muted">No models yet.</p>
        ) : (
          <ul className="catalog-root">
            {models.map((m) => (
              <li key={m.id} className="catalog-model">
                <div className="catalog-model-header">
                  <button type="button" className="expand-btn catalog-expand" onClick={() => void toggle(m.id)} aria-expanded={expanded.has(m.id)}>
                    {expanded.has(m.id) ? '▼' : '▶'} {m.name}
                    <span className="muted"> — {m.skuCount} SKU(s)</span>
                  </button>
                  {modelEdit?.id === m.id ? (
                    <div className="catalog-model-edit">
                      <div className="field">
                        <label htmlFor={`model-name-${m.id}`}>Product name</label>
                        <input id={`model-name-${m.id}`} className="input touch-input" value={modelEdit.name} onChange={(e) => setModelEdit({ ...modelEdit, name: e.target.value })} />
                      </div>
                      <div className="field">
                        <label htmlFor={`model-desc-${m.id}`}>Description (optional)</label>
                        <input id={`model-desc-${m.id}`} className="input touch-input" value={modelEdit.description} onChange={(e) => setModelEdit({ ...modelEdit, description: e.target.value })} />
                      </div>
                      <div className="sku-price-edit-actions">
                        <button type="button" className="btn primary touch-target" disabled={savingModel} onClick={() => void saveModelEdit()}>
                          {savingModel ? 'Saving…' : 'Save'}
                        </button>
                        <button type="button" className="btn secondary touch-target" disabled={savingModel} onClick={() => setModelEdit(null)}>
                          Cancel
                        </button>
                      </div>
                    </div>
                  ) : priceEdit?.modelId === m.id ? (
                    <div className="catalog-model-edit">
                      <div className="field">
                        <label htmlFor={`edit-buy-${m.id}`}>Buy price ({settings.currencyLabel})</label>
                        <input id={`edit-buy-${m.id}`} type="number" className="input touch-input" min={0} step="0.01" value={priceEdit.buy} onChange={(e) => setPriceEdit({ ...priceEdit, buy: e.target.value })} />
                      </div>
                      <div className="field">
                        <label htmlFor={`edit-unit-${m.id}`}>Sell price ({settings.currencyLabel})</label>
                        <input id={`edit-unit-${m.id}`} type="number" className="input touch-input" min={0} step="0.01" value={priceEdit.unit} onChange={(e) => setPriceEdit({ ...priceEdit, unit: e.target.value })} />
                      </div>
                      <div className="field">
                        <label htmlFor={`edit-sale-${m.id}`}>Sale price ({settings.currencyLabel}, optional)</label>
                        <input id={`edit-sale-${m.id}`} type="number" className="input touch-input" min={0} step="0.01" value={priceEdit.sale} onChange={(e) => setPriceEdit({ ...priceEdit, sale: e.target.value })} />
                      </div>
                      <div className="sku-price-edit-actions">
                        <button type="button" className="btn primary touch-target" disabled={savingPrice} onClick={() => void savePriceEdit()}>
                          {savingPrice ? 'Saving…' : 'Save'}
                        </button>
                        <button type="button" className="btn secondary touch-target" disabled={savingPrice} onClick={() => setPriceEdit(null)}>
                          Cancel
                        </button>
                      </div>
                    </div>
                  ) : (
                    <div className="catalog-model-actions">
                      <button
                        type="button"
                        className="btn secondary touch-target"
                        onClick={() =>
                          setPriceEdit({
                            modelId: m.id,
                            buy: m.buyPrice > 0 ? String(m.buyPrice) : '',
                            unit: m.unitPrice > 0 ? String(m.unitPrice) : '',
                            sale: m.salePrice != null && m.salePrice > 0 ? String(m.salePrice) : '',
                          })
                        }
                      >
                        Edit prices
                      </button>
                      <button
                        type="button"
                        className="btn secondary touch-target"
                        onClick={() => setModelEdit({ id: m.id, name: m.name, description: m.description ?? '' })}
                      >
                        Rename
                      </button>
                      <button
                        type="button"
                        className="btn secondary touch-target danger-btn"
                        disabled={deletingModelId === m.id}
                        onClick={() => void deleteModel(m)}
                      >
                        {deletingModelId === m.id ? 'Deleting…' : 'Delete product'}
                      </button>
                    </div>
                  )}
                </div>
                <div className="muted small" style={{ paddingLeft: '0.5rem' }}>
                  Buy {formatMoney(m.buyPrice, settings.currencyLabel)}
                  {' · '}
                  Sell{' '}
                  {isOnSale(m.unitPrice, m.salePrice) ? (
                    <>
                      <span className="price-strike">{formatMoney(m.unitPrice, settings.currencyLabel)}</span>{' '}
                      <strong className="price-sale">{formatMoney(m.salePrice!, settings.currencyLabel)}</strong>
                    </>
                  ) : (
                    formatMoney(m.unitPrice, settings.currencyLabel)
                  )}
                </div>
                {expanded.has(m.id) && (
                  <>
                    {skuErrors[m.id] && <p className="error">{skuErrors[m.id]}</p>}
                    {!skusByModel[m.id] ? (
                      <p className="muted px-3">Loading sizes…</p>
                    ) : skusByModel[m.id].length === 0 ? (
                      <p className="muted px-3">No SKUs yet.</p>
                    ) : (
                      <div className="sku-cards">
                        {skusByModel[m.id].map((row) => (
                          <div key={row.barcode} className="sku-card">
                            <div className="sku-card-head">
                              <strong>{row.size}</strong>
                              <span className="muted">{row.stock} in stock</span>
                            </div>
                            <div className="mono small">{row.barcode}</div>
                            {!isMobile && (
                              <button type="button" className="btn secondary touch-target full-width" style={{ marginTop: '0.5rem' }} disabled={printBarcode === row.barcode} onClick={() => void printForBarcode(row.barcode)}>
                                {printBarcode === row.barcode ? 'Printing…' : 'Print barcode'}
                              </button>
                            )}
                            <button
                              type="button"
                              className="btn secondary touch-target full-width danger-btn"
                              style={{ marginTop: '0.5rem' }}
                              disabled={deletingSkuId === row.id}
                              onClick={() => void deleteSku(m.id, row)}
                            >
                              {deletingSkuId === row.id ? 'Deleting…' : 'Delete size'}
                            </button>
                          </div>
                        ))}
                      </div>
                    )}
                  </>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
