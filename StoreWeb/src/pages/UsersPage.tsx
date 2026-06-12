import { useEffect, useState } from 'react';
import * as api from '../api';
import { useAuth } from '../auth/AuthContext';
import type { UserAdminRowDto, UserRole } from '../types';

export function UsersPage() {
  const { user } = useAuth();
  const [rows, setRows] = useState<UserAdminRowDto[] | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [statusError, setStatusError] = useState(false);
  const [showAdd, setShowAdd] = useState(false);
  const [addUser, setAddUser] = useState('');
  const [addPass, setAddPass] = useState('');
  const [addRole, setAddRole] = useState<UserRole>('seller');
  const [resetUser, setResetUser] = useState<UserAdminRowDto | null>(null);
  const [resetPass, setResetPass] = useState('');

  const reload = async () => {
    setRows(await api.getUsers());
  };

  useEffect(() => {
    void reload();
  }, []);

  const create = async () => {
    try {
      await api.createUser({ username: addUser.trim(), password: addPass, role: addRole });
      setShowAdd(false);
      setAddUser('');
      setAddPass('');
      setStatus('User created');
      setStatusError(false);
      await reload();
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Create failed');
      setStatusError(true);
    }
  };

  const reset = async () => {
    if (!resetUser) return;
    try {
      await api.resetPassword(resetUser.id, { newPassword: resetPass });
      setResetUser(null);
      setResetPass('');
      setStatus('Password updated');
      setStatusError(false);
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Reset failed');
      setStatusError(true);
    }
  };

  const deactivate = async (u: UserAdminRowDto) => {
    if (!window.confirm(`Deactivate ${u.username}?`)) return;
    try {
      await api.deactivateUser(u.id);
      setStatus(`${u.username} deactivated`);
      setStatusError(false);
      await reload();
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Deactivate failed');
      setStatusError(true);
    }
  };

  return (
    <div className="page-pad">
      <h1 className="page-title">Users</h1>
      <div className="btn-row">
        <button type="button" className="btn primary touch-target" onClick={() => setShowAdd(true)}>
          Add user
        </button>
        <button type="button" className="btn secondary touch-target" onClick={() => void reload()}>
          Refresh
        </button>
      </div>
      {status && <div className={`inline-banner ${statusError ? 'inline-banner-err' : 'inline-banner-ok'}`}>{status}</div>}
      {!rows ? (
        <p className="muted">Loading…</p>
      ) : rows.length === 0 ? (
        <div className="empty-state">
          <p className="empty-state-title">No staff accounts yet</p>
        </div>
      ) : (
        rows.map((u) => (
          <div key={u.id} className="card card-row">
            <div>
              <strong>{u.username}</strong>
              <span className="muted">
                {' '}
                · {u.role} · {u.isActive ? 'Active' : 'Inactive'}
              </span>
            </div>
            <div className="user-card-actions">
              <button type="button" className="btn secondary touch-target" disabled={!u.isActive} onClick={() => setResetUser(u)}>
                Reset password
              </button>
              <button type="button" className="btn secondary touch-target" disabled={!u.isActive || u.id === user?.id} onClick={() => void deactivate(u)}>
                Deactivate
              </button>
            </div>
          </div>
        ))
      )}
      {showAdd && (
        <>
          <div className="modal-backdrop" onClick={() => setShowAdd(false)} />
          <div className="modal-panel">
            <h2 className="modal-title">Add user</h2>
            <div className="field">
              <label>Username</label>
              <input className="input touch-input" value={addUser} onChange={(e) => setAddUser(e.target.value)} />
            </div>
            <div className="field">
              <label>Password</label>
              <input className="input touch-input" value={addPass} onChange={(e) => setAddPass(e.target.value)} />
            </div>
            <div className="field">
              <label>Role</label>
              <select className="input touch-input" value={addRole} onChange={(e) => setAddRole(e.target.value as UserRole)}>
                <option value="admin">Admin</option>
                <option value="seller">Seller</option>
              </select>
            </div>
            <div className="btn-row">
              <button type="button" className="btn primary touch-target" onClick={() => void create()}>
                Create
              </button>
              <button type="button" className="btn secondary touch-target" onClick={() => setShowAdd(false)}>
                Cancel
              </button>
            </div>
          </div>
        </>
      )}
      {resetUser && (
        <>
          <div className="modal-backdrop" onClick={() => setResetUser(null)} />
          <div className="modal-panel">
            <h2 className="modal-title">Reset password — {resetUser.username}</h2>
            <div className="field">
              <label>New password</label>
              <input className="input touch-input" value={resetPass} onChange={(e) => setResetPass(e.target.value)} />
            </div>
            <div className="btn-row">
              <button type="button" className="btn primary touch-target" onClick={() => void reset()}>
                Save
              </button>
              <button type="button" className="btn secondary touch-target" onClick={() => setResetUser(null)}>
                Cancel
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
