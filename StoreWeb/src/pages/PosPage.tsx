import { useEffect, useState } from 'react';
import * as api from '../api';
import { useAuth } from '../auth/AuthContext';
import { useSettings } from '../settings/SettingsContext';
import { formatMoney } from '../utils/money';

interface CartLine {
  skuId: number;
  productName: string;
  qty: number;
  unitPrice: number;
  maxStock: number;
}

export function PosPage() {
  const { isAdmin } = useAuth();
  const settings = useSettings();
  const [cart, setCart] = useState<CartLine[]>([]);
  const [barcode, setBarcode] = useState('');
  const [flash, setFlash] = useState<string | null>(null);
  const [discountRaw, setDiscountRaw] = useState('');
  const [discToken, setDiscToken] = useState<string | null>(null);
  const [discUnlocked, setDiscUnlocked] = useState(false);
  const [showPin, setShowPin] = useState(false);
  const [pin, setPin] = useState('');
  const [busy, setBusy] = useState(false);
  const [receiptMsg, setReceiptMsg] = useState<string | null>(null);

  const { load: loadSettings } = settings;

  useEffect(() => {
    void loadSettings();
  }, [loadSettings]);

  const subtotal = cart.reduce((s, l) => s + l.unitPrice * l.qty, 0);
  const discount = Math.max(0, parseFloat(discountRaw) || 0);
  const total = Math.max(0, subtotal - discount);
  const isSeller = !isAdmin;
  const canDiscount = discUnlocked || isAdmin || settings.allowSellerDiscount;

  const addBarcode = async () => {
    setFlash(null);
    if (!barcode.trim()) return;
    try {
      const sku = await api.getSkuByBarcode(barcode.trim());
      if (sku.stock <= 0) {
        setFlash('Out of stock');
        setBarcode('');
        return;
      }
      setCart((prev) => {
        const existing = prev.find((c) => c.skuId === sku.id);
        if (existing) {
          if (existing.qty >= sku.stock) {
            setFlash('Max stock reached');
            return prev;
          }
          return prev.map((c) => (c.skuId === sku.id ? { ...c, qty: c.qty + 1 } : c));
        }
        return [
          ...prev,
          {
            skuId: sku.id,
            productName: sku.productName,
            unitPrice: sku.unitPrice,
            qty: 1,
            maxStock: sku.stock,
          },
        ];
      });
      setBarcode('');
    } catch {
      setFlash('Not found');
      setBarcode('');
    }
  };

  const authorizePin = async () => {
    try {
      const res = await api.authorizeDiscount(pin);
      setDiscToken(res.discountAuthorizationToken);
      setDiscUnlocked(true);
      setShowPin(false);
      setPin('');
    } catch {
      setFlash('Invalid PIN');
    }
  };

  const checkout = async () => {
    setBusy(true);
    setFlash(null);
    try {
      const result = await api.createSale({
        items: cart.map((l) => ({ skuId: l.skuId, quantity: l.qty, unitPrice: l.unitPrice })),
        discountAmount: discount,
        discountAuthorizationToken: discToken,
      });
      setReceiptMsg(`Sale #${result.saleId} · ${formatMoney(result.totalAmount, settings.currencyLabel)} · ${result.receiptNumber}`);
      setCart([]);
      setDiscountRaw('');
      setDiscToken(null);
    } catch (e) {
      setFlash(e instanceof Error ? e.message : 'Checkout failed');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="pos-mobile">
      {flash && (
        <div className="banner banner-warn" role="alert">
          {flash}
        </div>
      )}
      <div className="field">
        <label htmlFor="pos-bc">Barcode</label>
        <input
          id="pos-bc"
          className="input touch-input pos-barcode"
          value={barcode}
          onChange={(e) => setBarcode(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && void addBarcode()}
          autoComplete="off"
          placeholder="Scan or type, Enter to add"
        />
      </div>
      {cart.length === 0 ? (
        <div className="pos-empty-state">
          <span className="pos-empty-icon" aria-hidden="true">
            ▣
          </span>
          <p>
            <strong>Ready to sell</strong>
          </p>
          <p className="small">Scan a barcode or type it above, then press Enter</p>
        </div>
      ) : (
        cart.map((line) => (
          <div key={line.skuId} className="card cart-line-card">
            <div className="cart-line-head">
              <strong>{line.productName}</strong>
              <button type="button" className="btn-link touch-target" onClick={() => setCart((c) => c.filter((x) => x.skuId !== line.skuId))}>
                ✕
              </button>
            </div>
            <div className="muted small">
              {formatMoney(line.unitPrice, settings.currencyLabel)}
            </div>
            <div className="cart-line-foot">
              <div className="qty-stepper">
                <button type="button" className="qty-stepper-btn" disabled={line.qty <= 1} onClick={() => setCart((c) => c.map((x) => (x.skuId === line.skuId ? { ...x, qty: x.qty - 1 } : x)))}>
                  −
                </button>
                <span className="qty-stepper-value">{line.qty}</span>
                <button type="button" className="qty-stepper-btn" disabled={line.qty >= line.maxStock} onClick={() => setCart((c) => c.map((x) => (x.skuId === line.skuId ? { ...x, qty: x.qty + 1 } : x)))}>
                  +
                </button>
              </div>
              <strong>{formatMoney(line.unitPrice * line.qty, settings.currencyLabel)}</strong>
            </div>
          </div>
        ))
      )}
      <div className="pos-sticky-footer">
        <div className="pos-totals">
          <div>
            <span>Subtotal</span>
            <span>{formatMoney(subtotal, settings.currencyLabel)}</span>
          </div>
          {discount > 0 && (
            <div>
              <span>Discount</span>
              <span>- {formatMoney(discount, settings.currencyLabel)}</span>
            </div>
          )}
          <div className="pos-total-row">
            <span>TOTAL</span>
            <strong>{formatMoney(total, settings.currencyLabel)}</strong>
          </div>
        </div>
        {isSeller && !settings.allowSellerDiscount && !discToken && (
          <button type="button" className="btn secondary full-width touch-target" onClick={() => setShowPin(true)}>
            Manager PIN for discount
          </button>
        )}
        {canDiscount && (
          <div className="field">
            <label>Discount ({settings.currencyLabel})</label>
            <input type="number" className="input touch-input" min={0} step={0.01} value={discountRaw} onChange={(e) => setDiscountRaw(e.target.value)} />
          </div>
        )}
        <button type="button" className="btn primary full-width touch-target checkout-btn" disabled={cart.length === 0 || busy} onClick={() => void checkout()}>
          {busy ? 'Processing…' : 'Checkout'}
        </button>
        <button type="button" className="btn secondary full-width touch-target" disabled={cart.length === 0} onClick={() => { setCart([]); setDiscountRaw(''); setDiscToken(null); }}>
          Clear cart
        </button>
      </div>
      {showPin && (
        <>
          <div className="modal-backdrop" onClick={() => setShowPin(false)} />
          <div className="modal-panel">
            <h2 className="modal-title">Manager PIN</h2>
            <input type="password" className="input touch-input" value={pin} onChange={(e) => setPin(e.target.value)} />
            <div className="btn-row">
              <button type="button" className="btn primary touch-target" onClick={() => void authorizePin()}>
                OK
              </button>
              <button type="button" className="btn secondary touch-target" onClick={() => setShowPin(false)}>
                Cancel
              </button>
            </div>
          </div>
        </>
      )}
      {receiptMsg && (
        <>
          <div className="modal-backdrop" onClick={() => setReceiptMsg(null)} />
          <div className="modal-panel">
            <h2 className="modal-title">Sale complete</h2>
            <p>{receiptMsg}</p>
            <button type="button" className="btn primary touch-target full-width" onClick={() => setReceiptMsg(null)}>
              New sale
            </button>
          </div>
        </>
      )}
    </div>
  );
}
