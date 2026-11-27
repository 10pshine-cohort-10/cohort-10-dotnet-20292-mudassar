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

const INCOMPLETE_LOGOUT_ERROR = "Could not complete sign-out on this device. Please retry sign-out.";

const isAuthFailure = (err) => {
  const status = err?.response?.status;
  return status === 401 || status === 403;
};

async function tryClearSuppressedToken(storage) {
  try {
    await storage.removeToken();
    return true;
  } catch {
    return false;
  }
}

async function readCapturedToken(storage) {
  try {
    return await storage.getToken();
  } catch {
    return null;
  }
}

async function removeSessionToken(storage, capturedToken) {
  if (capturedToken && typeof storage.removeTokenIfMatch === "function") {
    const removed = await storage.removeTokenIfMatch(capturedToken);
    if (removed) return;

    const currentToken = await storage.getToken();
    if (!currentToken || currentToken !== capturedToken) {
      return;
    }

    await storage.removeToken();
    const remainingToken = await storage.getToken();
    if (remainingToken === capturedToken) {
      throw new Error("Failed to clear active session token during logout.");
    }
    return;
  }
  await storage.removeToken();
}

async function reconcileFailedAuthToken(storage, failedToken) {
  let removed = false;
  if (failedToken && typeof storage.removeTokenIfMatch === "function") {
    try {
      removed = await storage.removeTokenIfMatch(failedToken);
    } catch {
      /* ignore remove failure on invalid auth */
    }
  } else if (!failedToken) {
    try {
      await storage.removeToken();
      removed = true;
    } catch {
      /* ignore remove failure on invalid auth */
    }
  }

  if (!failedToken || removed) {
    return false;
  }

  const currentToken = await storage.getToken().catch(() => null);
  return Boolean(currentToken && currentToken !== failedToken);
}

export function AuthProvider({ children, storage: storageProp }) {
  const storage = storageProp ?? defaultSessionStorage;
  const [user, setUserState] = useState(null);
  const [loading, setLoading] = useState(true);
  const [restoreError, setRestoreError] = useState("");

  const sessionVersionRef = useRef(0);
  const logoutInFlightRef = useRef(false);
  const suppressStoredTokenRef = useRef(false);

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

    let attemptedToken = null;
    try {
      if (suppressStoredTokenRef.current) {
        const cleared = await tryClearSuppressedToken(storage);
        if (cleared) suppressStoredTokenRef.current = false;
        if (sessionVersionRef.current !== version) return;
        setUserState(null);
        setRestoreError(cleared ? "" : INCOMPLETE_LOGOUT_ERROR);
        return;
      }

      const token = await storage.getToken();
      attemptedToken = token;
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
      if (isAuthFailure(err)) {
        const failedToken = err?.config?._requestToken ?? attemptedToken;
        const hasNewerToken = await reconcileFailedAuthToken(storage, failedToken);
        if (sessionVersionRef.current !== version) return;
        if (hasNewerToken) {
          sessionVersionRef.current += 1;
          setUserState(null);
          await restoreSession();
          return;
        }
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

    const applyCrossTabTokenChange = (nextValue) => {
      sessionVersionRef.current += 1;
      logoutInFlightRef.current = false;
      if (nextValue) {
        suppressStoredTokenRef.current = false;
      }
      clearPageCache();
      setUserState(null);
      void restoreSession();
    };

    const handleStorage = (event) => {
      if (event.storageArea && event.storageArea !== window.localStorage) return;
      if (event.key !== null && event.key !== "token") return;
      applyCrossTabTokenChange(event.newValue ?? null);
    };

    const unsubscribeChannel = subscribeTokenChanges((evt) => {
      applyCrossTabTokenChange(evt?.newValue ?? null);
    });

    window.addEventListener("storage", handleStorage);
    return () => {
      sessionVersionRef.current += 1;
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
    let superseded = false;
    try {
      const applied = await storage.setToken(data.token);
      if (applied === false) {
        superseded = true;
        if (sessionVersionRef.current === loginVersion) {
          suppressStoredTokenRef.current = false;
          setUserState(null);
          await restoreSession();
        }
        return;
      }
      if (sessionVersionRef.current === loginVersion) {
        suppressStoredTokenRef.current = false;
        setRestoreError("");
        setUserState(data.user);
      }
    } finally {
      if (!superseded && sessionVersionRef.current === loginVersion) {
        setLoading(false);
      }
    }
  }, [restoreSession, storage]);

  const logout = useCallback(async () => {
    if (logoutInFlightRef.current) return;
    setActiveSessionStorage(storage);
    logoutInFlightRef.current = true;
    const startingVersion = sessionVersionRef.current;
    const capturedToken = await readCapturedToken(storage);
    let incompleteLogoutError = null;

    try {
      await logoutRequest({ timeout: 3000 });
    } catch {
      /* discard local session even if the server times out or is unreachable */
    } finally {
      logoutInFlightRef.current = false;
      // Only clear session state if no newer login occurred while logout was in flight.
      if (sessionVersionRef.current === startingVersion) {
        const logoutVersion = ++sessionVersionRef.current;
        suppressStoredTokenRef.current = true;
        clearPageCache();
        setRestoreError("");
        setLoading(true);
        try {
          await removeSessionToken(storage, capturedToken);
          if (sessionVersionRef.current === logoutVersion) {
            suppressStoredTokenRef.current = false;
            setRestoreError("");
            setUserState(null);
            setLoading(false);
          }
        } catch (err) {
          // Keep suppressStoredTokenRef true so restoreSession cannot resurrect the stale token,
          // and surface the incomplete sign-out because server logout is stateless.
          console.error("Failed to remove token from session storage during logout:", err);
          if (sessionVersionRef.current === logoutVersion) {
            setUserState(null);
            setRestoreError(INCOMPLETE_LOGOUT_ERROR);
            setLoading(false);
          }
          incompleteLogoutError = err;
        }
      }
    }

    if (incompleteLogoutError) {
      throw incompleteLogoutError;
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
