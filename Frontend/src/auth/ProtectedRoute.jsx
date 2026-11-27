import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "./AuthContext";

export default function ProtectedRoute({ adminOnly = false }) {
  const { user, loading, restoreError, retryRestore, logout, isAdmin } = useAuth();
  if (loading) {
    return (
      <p role="status" aria-live="polite" style={{ padding: 24 }}>
        Loading...
      </p>
    );
  }
  if (!user && restoreError) {
    return (
      <div className="app">
        <div className="page">
          <div className="alert error" role="alert">
            <p>{restoreError}</p>
            <div className="row-actions">
              <button type="button" className="retry" onClick={() => void retryRestore()}>
                Retry
              </button>
              <button type="button" className="retry" onClick={() => void logout().catch(() => {})}>
                Sign in again
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }
  if (!user) return <Navigate to="/login" replace />;
  if (adminOnly && !isAdmin) return <Navigate to="/dashboard" replace />;
  return <Outlet />;
}
