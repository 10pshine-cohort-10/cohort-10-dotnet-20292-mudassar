import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "./AuthContext";

export function SessionRecoveryView({ message, onRetry, onSignInAgain }) {
  return (
    <div className="app">
      <div className="page">
        <div className="alert error" role="alert">
          <p>{message}</p>
          <div className="row-actions">
            <button type="button" className="retry" onClick={() => void onRetry()}>
              Retry
            </button>
            <button type="button" className="retry" onClick={() => void onSignInAgain().catch(() => {})}>
              Sign in again
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

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
      <SessionRecoveryView
        message={restoreError}
        onRetry={retryRestore}
        onSignInAgain={logout}
      />
    );
  }
  if (!user) return <Navigate to="/login" replace />;
  if (adminOnly && !isAdmin) return <Navigate to="/dashboard" replace />;
  return <Outlet />;
}
