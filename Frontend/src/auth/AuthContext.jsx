import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { getMe, loginRequest, logoutRequest } from "../api/authApi";
import { getErrorMessage } from "../utils/errorMessage";
import { clearPageCache } from "../utils/pageCache";
import {
  defaultSessionStorage,
  setActiveSessionStorage,
  subscribeTokenChanges,
} from "./tokenStorage";

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
    setLoading(true);
    setRestoreError("");

    try {
      const token = await storage.getToken();
      if (sessionVersionRef.current !== version) return;

      if (!token) {
        setUserState(null);
        setRestoreError("");
        return;
      }

      const me = await getMe();
      if (sessionVersionRef.current !== version) return;
      setUserState(me);
    } catch (err) {
      if (sessionVersionRef.current !== version) return;
      const status = err?.response?.status;
      if (status === 401 || status === 403) {
        try {
          await storage.removeToken();
        } catch {
          /* ignore remove failure on invalid auth */
        }
        if (sessionVersionRef.current !== version) return;
        setUserState(null);
        setRestoreError("");
      } else {
        // Keep the token on storage/network/5xx errors so the user can retry.
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

    const applyCrossTabTokenChange = () => {
      sessionVersionRef.current += 1;
      logoutInFlightRef.current = false;
      clearPageCache();
      void restoreSession();
    };

    const handleStorage = (event) => {
      if (event.storageArea && event.storageArea !== window.localStorage) return;
      if (event.key !== null && event.key !== "token") return;
      applyCrossTabTokenChange();
    };

    const unsubscribeChannel = subscribeTokenChanges(() => {
      applyCrossTabTokenChange();
    });

    window.addEventListener("storage", handleStorage);
    return () => {
      unsubscribeChannel();
      window.removeEventListener("storage", handleStorage);
    };
  }, [restoreSession, storage]);

  const login = useCallback(async (email, password) => {
    setActiveSessionStorage(storage);
    clearPageCache();
    const data = await loginRequest(email, password);
    sessionVersionRef.current += 1;
    const loginVersion = sessionVersionRef.current;
    logoutInFlightRef.current = false;
    try {
      await storage.setToken(data.token);
      if (sessionVersionRef.current === loginVersion) {
        setRestoreError("");
        setUserState(data.user);
      }
    } finally {
      if (sessionVersionRef.current === loginVersion) {
        setLoading(false);
      }
    }
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
        setRestoreError("");
        setUserState(null);
        try {
          await storage.removeToken();
        } catch {
          /* best-effort removal so logout always completes */
        }
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
