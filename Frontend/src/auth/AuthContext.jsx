import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { getMe, loginRequest, logoutRequest } from "../api/authApi";
import { getErrorMessage } from "../utils/errorMessage";
import { clearPageCache } from "../utils/pageCache";

const AuthContext = createContext(null);
export const useAuth = () => useContext(AuthContext);

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);
  const [restoreError, setRestoreError] = useState("");

  const sessionVersionRef = useRef(0);
  const logoutInFlightRef = useRef(false);

  const restoreSession = useCallback(async () => {
    if (!localStorage.getItem("token")) {
      setRestoreError("");
      setLoading(false);
      return;
    }

    const version = sessionVersionRef.current;
    setLoading(true);
    setRestoreError("");

    try {
      const me = await getMe();
      if (sessionVersionRef.current !== version) return;
      setUser(me);
    } catch (err) {
      if (sessionVersionRef.current !== version) return;
      const status = err?.response?.status;
      if (status === 401 || status === 403) {
        localStorage.removeItem("token");
        setUser(null);
        setRestoreError("");
      } else {
        // Keep the token on transient network/5xx errors so the user can retry.
        setRestoreError(getErrorMessage(err));
      }
    } finally {
      if (sessionVersionRef.current === version) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    void restoreSession();

    const handleStorage = (event) => {
      if (event.key !== "token") return;
      sessionVersionRef.current += 1;
      logoutInFlightRef.current = false;
      clearPageCache();
      if (!event.newValue) {
        setUser(null);
        setRestoreError("");
        setLoading(false);
        return;
      }
      setUser(null);
      void restoreSession();
    };

    window.addEventListener("storage", handleStorage);
    return () => window.removeEventListener("storage", handleStorage);
  }, [restoreSession]);

  const login = useCallback(async (email, password) => {
    clearPageCache();
    const data = await loginRequest(email, password);
    sessionVersionRef.current += 1;
    logoutInFlightRef.current = false;
    localStorage.setItem("token", data.token);
    setRestoreError("");
    setUser(data.user);
  }, []);

  const logout = useCallback(async () => {
    if (logoutInFlightRef.current) return;
    logoutInFlightRef.current = true;
    const startingVersion = sessionVersionRef.current;

    try {
      await logoutRequest({ timeout: 3000 });
    } catch {
      /* discard local session even if the server times out or is unreachable */
    } finally {
      logoutInFlightRef.current = false;
      // Only clear session state if no newer login occurred while logout was in flight.
      if (sessionVersionRef.current === startingVersion) {
        sessionVersionRef.current += 1;
        clearPageCache();
        localStorage.removeItem("token");
        setRestoreError("");
        setUser(null);
      }
    }
  }, []);

  const value = useMemo(
    () => ({
      user,
      setUser,
      loading,
      restoreError,
      retryRestore: restoreSession,
      login,
      logout,
      isAdmin: !!user?.roles?.includes("Admin"),
    }),
    [user, loading, restoreError, restoreSession, login, logout]
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
