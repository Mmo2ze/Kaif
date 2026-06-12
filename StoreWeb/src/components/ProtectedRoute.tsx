import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export function ProtectedRoute({ adminOnly = false }: { adminOnly?: boolean }) {
  const { sessionReady, isAuthenticated, isAdmin } = useAuth();

  if (!sessionReady) {
    return (
      <div className="app-loading">
        <span className="spinner" aria-hidden="true" />
        <span>Loading…</span>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  if (adminOnly && !isAdmin) {
    return <Navigate to="/seller" replace />;
  }

  return <Outlet />;
}
