import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { getMe, loginRequest, logoutRequest } from "../api/authApi";
import { getErrorMessage } from "../utils/errorMessage";
import { clearPageCache } from "../utils/pageCache";
import { defaultSessionStorage, setActiveSessionStorage } from "./tokenStorage";

const AuthContext = createContext(null);
export const useAuth = () => useContext(AuthContext);

export function AuthProvider({ children, storage = defaultSessionStorage }) {
  const [user, setUserState] = useState(null);
  const [loading, setLoading] = useState(true);
  const [restoreError, setRestoreError] = useState("");

  const sessionVersionRef = useRef(0);
  const logoutInFlightRef = useRef(false);

  const getSessionVersion = useCallback(() => sessionVersionRef.current, []);

  // Session-scoped profile updater: never restores a user after logout or across a different session.
  const setUser = useCallback((nextUser, expectedSessionVersion) => {
    if (expectedSessionVersion !== undefined && sessionVersionRef.current !== expectedSessionVersion) {
      return;
    }
    setUserState((prev) => {
      if (!prev) return prev;
      return typeof nextUser === "function" ? nextUser(prev) : nextUser;
    });
  }, []);

  const restoreSession = useCallback(async () => {
    setActiveSessionStorage(storage);
    const version = sessionVersionRef.current;
    const token = await storage.getToken();
    if (sessionVersionRef.current !== version) return;

    if (!token) {
      setRestoreError("");
      setLoading(false);
      return;
    }

    setLoading(true);
    setRestoreError("");

    try {
      const me = await getMe();
      if (sessionVersionRef.current !== version) return;
      setUserState(me);
    } catch (err) {
      if (sessionVersionRef.current !== version) return;
      const status = err?.response?.status;
      if (status === 401 || status === 403) {
        await storage.removeToken();
        if (sessionVersionRef.current !== version) return;
        setUserState(null);
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
  }, [storage]);

  useEffect(() => {
    setActiveSessionStorage(storage);
    void restoreSession();

    const handleStorage = (event) => {
      if (event.key !== "token") return;
      sessionVersionRef.current += 1;
      logoutInFlightRef.current = false;
      clearPageCache();
      if (!event.newValue) {
        setUserState(null);
        setRestoreError("");
        setLoading(false);
        return;
      }
      setUserState(null);
      void restoreSession();
    };

    window.addEventListener("storage", handleStorage);
    return () => window.removeEventListener("storage", handleStorage);
  }, [restoreSession, storage]);

  const login = useCallback(async (email, password) => {
    setActiveSessionStorage(storage);
    clearPageCache();
    const data = await loginRequest(email, password);
    sessionVersionRef.current += 1;
    logoutInFlightRef.current = false;
    await storage.setToken(data.token);
    setRestoreError("");
    setUserState(data.user);
  }, [storage]);

  const logout = useCallback(async () => {
    if (logoutInFlightRef.current) return;
    setActiveSessionStorage(storage);
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
        await storage.removeToken();
        setRestoreError("");
        setUserState(null);
      }
    }
  }, [storage]);

  const value = useMemo(
    () => ({
      user,
      setUser,
      getSessionVersion,
      loading,
      restoreError,
      retryRestore: restoreSession,
      login,
      logout,
      isAdmin: !!user?.roles?.includes("Admin"),
    }),
    [user, setUser, getSessionVersion, loading, restoreError, restoreSession, login, logout]
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
