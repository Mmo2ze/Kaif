import { useEffect, useState } from 'react';
import { NavLink } from 'react-router-dom';
import * as api from '../api';
import { useAuth } from '../auth/AuthContext';

export function HomePage() {
  const { user } = useAuth();
  const [healthOk, setHealthOk] = useState<boolean | null>(null);

  useEffect(() => {
    void (async () => {
      try {
        await api.getHealth();
        setHealthOk(true);
      } catch {
        setHealthOk(false);
      }
    })();
  }, []);

  return (
    <div className="page-pad">
      <h1 className="page-title">Dashboard</h1>
      <p className="muted page-sub">Welcome, {user?.username}</p>
      {healthOk === false && (
        <div className="banner banner-warn" role="alert">
          API not reachable. Start StoreAPI on port 5050.
        </div>
      )}
      <div className="dash-grid">
        <NavLink className="dash-card touch-target" to="/products">
          <span className="dash-card-icon">▦</span>
          <span className="dash-card-title">Products</span>
          <span className="dash-card-sub muted">Stock, prices &amp; barcodes</span>
        </NavLink>
      </div>
    </div>
  );
}
