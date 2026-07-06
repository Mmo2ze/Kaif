import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export function LoginPage() {
  const { isAuthenticated, isAdmin, login } = useAuth();
  const navigate = useNavigate();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [offline, setOffline] = useState(false);

  useEffect(() => {
    if (isAuthenticated) navigate(isAdmin ? '/home' : '/products', { replace: true });
  }, [isAuthenticated, isAdmin, navigate]);

  const submit = async () => {
    setError(null);
    setOffline(false);
    setBusy(true);
    try {
      const me = await login({ username: username.trim(), password });
      navigate(me.role === 'admin' ? '/home' : '/products', { replace: true });
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Could not sign in.';
      if (msg.includes('fetch') || msg.includes('Failed to fetch')) {
        setOffline(true);
        setError('Cannot reach the server. Check that StoreAPI is running.');
      } else if (msg.includes('401')) {
        setError('Invalid username or password.');
      } else {
        setError('Could not sign in.');
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="login-screen">
      <div className="login-brand">
        <div className="login-logo" aria-hidden="true">
          K
        </div>
        <h1>Kaif Store</h1>
        <p className="muted">Sign in to manage stock and products</p>
      </div>
      <div className="card login-card">
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void submit();
          }}
        >
          <div className="field">
            <label htmlFor="user">Username</label>
            <input
              id="user"
              className="input touch-input"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              autoComplete="username"
            />
          </div>
          <div className="field">
            <label htmlFor="pass">Password</label>
            <input
              id="pass"
              type="password"
              className="input touch-input"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
            />
          </div>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <button type="submit" className="btn primary full-width touch-target" disabled={busy}>
            {busy && <span className="spinner spinner-sm" aria-hidden="true" />}
            {busy ? 'Signing in…' : 'Sign in'}
          </button>
          {offline && (
            <button type="button" className="btn secondary full-width touch-target" onClick={() => void submit()} disabled={busy}>
              Retry
            </button>
          )}
        </form>
      </div>
    </div>
  );
}
