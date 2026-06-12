import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './auth/AuthContext';
import { AppShell } from './components/AppShell';
import { ProtectedRoute } from './components/ProtectedRoute';
import { SettingsProvider } from './settings/SettingsContext';
import { HomePage } from './pages/HomePage';
import { LoginPage } from './pages/LoginPage';
import { MorePage } from './pages/MorePage';
import { ProductsPage } from './pages/ProductsPage';
import { SellerNoticePage } from './pages/SellerNoticePage';
import { StockPage } from './pages/StockPage';

export default function App() {
  return (
    <AuthProvider>
      <SettingsProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/" element={<Navigate to="/login" replace />} />
            <Route element={<ProtectedRoute />}>
              <Route element={<AppShell />}>
                <Route path="/seller" element={<SellerNoticePage />} />
              </Route>
            </Route>
            <Route element={<ProtectedRoute adminOnly />}>
              <Route element={<AppShell />}>
                <Route path="/home" element={<HomePage />} />
                <Route path="/stock" element={<StockPage />} />
                <Route path="/products" element={<ProductsPage />} />
                <Route path="/more" element={<MorePage />} />
                <Route path="/pos" element={<Navigate to="/home" replace />} />
                <Route path="/sales" element={<Navigate to="/home" replace />} />
                <Route path="/users" element={<Navigate to="/home" replace />} />
                <Route path="/settings" element={<Navigate to="/home" replace />} />
              </Route>
            </Route>
            <Route path="*" element={<p className="page-pad">Page not found.</p>} />
          </Routes>
        </BrowserRouter>
      </SettingsProvider>
    </AuthProvider>
  );
}
