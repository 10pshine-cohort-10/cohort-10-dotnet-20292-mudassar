import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { getMe, loginRequest, logoutRequest } from "../api/authApi";
import { getErrorMessage } from "../utils/errorMessage";
import { clearPageCache } from "../utils/pageCache";
import {
  defaultSessionStorage,
  getTokenAccountIdentity,
  setActiveAccountIdentity,
  setActiveSessionStorage,
  subscribeStorageRecovery,
  subscribeTokenChanges,
} from "./tokenStorage";

const AuthContext = createContext(null);
export const useAuth = () => useContext(AuthContext);

const INCOMPLETE_LOGOUT_ERROR = "Could not complete sign-out on this device. Please retry sign-out.";
const INTERRUPTED_LOGIN_ERROR = "Sign-in was interrupted by sign-out. Please sign in again.";

const isAuthFailure = (err) => {
  const status = err?.response?.status;
  return status === 401 || status === 403;
};

const resolveAccountIdentity = (userObj, token) =>
  userObj?.id ?? getTokenAccountIdentity(token);

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
    const token = await storage.getToken();
    return { ok: true, token: token ?? null };
  } catch (error) {
    return { ok: false, token: null, error };
  }
}

async function removeSessionToken(storage, captured) {
  if (!captured?.ok) {
    throw captured?.error ?? new Error(INCOMPLETE_LOGOUT_ERROR);
  }
  if (!captured.token) {
    return;
  }
  if (typeof storage.removeTokenIfMatch === "function") {
    await storage.removeTokenIfMatch(captured.token);
    return;
  }
  await storage.removeToken();
}

async function resolveSupersededLoginOutcome(storage, isSuppressed) {
  const captured = await readCapturedToken(storage);
  if (isSuppressed || !captured.ok || !captured.token) {
    return {
      ok: false,
      interrupted: true,
      status: "interrupted",
      message: INTERRUPTED_LOGIN_ERROR,
    };
  }
  return {
    ok: true,
    interrupted: false,
    status: "superseded",
  };
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
  const [userState, setUserState] = useState(null);
  const [loading, setLoading] = useState(true);
  const [restoreError, setRestoreError] = useState("");

  const sessionVersionRef = useRef(0);
  const restoreRequestIdRef = useRef(0);
  const blockingRestorePendingRef = useRef(false);
  const logoutPromiseRef = useRef(null);
  const suppressStoredTokenRef = useRef(false);
  const userRef = useRef(userState);
  userRef.current = userState;

  const getSessionVersion = useCallback(() => sessionVersionRef.current, []);

  const isCurrentRestore = useCallback(
    (version, requestId) =>
      sessionVersionRef.current === version && restoreRequestIdRef.current === requestId,
    []
  );

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

  const clearSuppressedSession = useCallback(async (version, requestId) => {
    const cleared = await tryClearSuppressedToken(storage);
    if (cleared) suppressStoredTokenRef.current = false;
    if (!isCurrentRestore(version, requestId)) return;
    setActiveAccountIdentity(null);
    setUserState(null);
    setRestoreError(cleared ? "" : INCOMPLETE_LOGOUT_ERROR);
  }, [isCurrentRestore, storage]);

  const handleRestoreFailure = useCallback(
    async ({ err, version, requestId, attemptedToken, retryRestoreFn }) => {
      if (!isCurrentRestore(version, requestId)) return;
      if (!isAuthFailure(err)) {
        // Keep the token on storage/network/5xx errors so the user can retry.
        setRestoreError(getErrorMessage(err));
        return;
      }
      const failedToken = err?.config?._requestToken ?? attemptedToken;
      const hasNewerToken = await reconcileFailedAuthToken(storage, failedToken);
      if (!isCurrentRestore(version, requestId)) return;
      if (hasNewerToken) {
        sessionVersionRef.current += 1;
        setUserState(null);
        await retryRestoreFn();
        return;
      }
      setActiveAccountIdentity(null);
      setUserState(null);
      setRestoreError("");
    },
    [isCurrentRestore, storage]
  );

  const restoreSession = useCallback(async (options) => {
    const silent = Boolean(options?.silent);
    setActiveSessionStorage(storage);
    const version = sessionVersionRef.current;
    const requestId = ++restoreRequestIdRef.current;
    if (!silent) {
      blockingRestorePendingRef.current = true;
      setLoading(true);
    }
    setRestoreError("");

    let attemptedToken = null;
    try {
      if (suppressStoredTokenRef.current) {
        await clearSuppressedSession(version, requestId);
        return;
      }

      const token = await storage.getToken();
      attemptedToken = token;
      if (!isCurrentRestore(version, requestId)) return;

      if (!token) {
        setActiveAccountIdentity(null);
        setUserState(null);
        setRestoreError("");
        return;
      }

      setActiveAccountIdentity(getTokenAccountIdentity(token));
      const me = await getMe();
      if (!isCurrentRestore(version, requestId)) return;
      setActiveAccountIdentity(resolveAccountIdentity(me, token));
      setUserState(me);
    } catch (err) {
      if (!isCurrentRestore(version, requestId)) return;
      await handleRestoreFailure({
        err,
        version,
        requestId,
        attemptedToken,
        retryRestoreFn: restoreSession,
      });
    } finally {
      if (isCurrentRestore(version, requestId) && (!silent || blockingRestorePendingRef.current)) {
        blockingRestorePendingRef.current = false;
        setLoading(false);
      }
    }
  }, [clearSuppressedSession, handleRestoreFailure, isCurrentRestore, storage]);

  useEffect(() => {
    setActiveSessionStorage(storage);
    void restoreSession();

    const applyCrossTabTokenChange = (nextValue) => {
      sessionVersionRef.current += 1;
      logoutPromiseRef.current = null;
      if (nextValue) {
        suppressStoredTokenRef.current = false;
        setActiveAccountIdentity(getTokenAccountIdentity(nextValue));
      } else {
        setActiveAccountIdentity(null);
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

    const unsubscribeRecovery = subscribeStorageRecovery(() => {
      if (!suppressStoredTokenRef.current) {
        void restoreSession({ silent: Boolean(userRef.current) });
      }
    });

    window.addEventListener("storage", handleStorage);
    return () => {
      sessionVersionRef.current += 1;
      unsubscribeChannel();
      unsubscribeRecovery();
      window.removeEventListener("storage", handleStorage);
    };
  }, [restoreSession, storage]);

  const login = useCallback(async (email, password) => {
    setActiveSessionStorage(storage);
    const startingVersion = sessionVersionRef.current;
    clearPageCache();
    const data = await loginRequest(email, password);
    if (sessionVersionRef.current !== startingVersion) {
      return resolveSupersededLoginOutcome(storage, suppressStoredTokenRef.current);
    }
    sessionVersionRef.current += 1;
    const loginVersion = sessionVersionRef.current;
    logoutPromiseRef.current = null;
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
        return resolveSupersededLoginOutcome(storage, suppressStoredTokenRef.current);
      }
      if (sessionVersionRef.current !== loginVersion) {
        superseded = true;
        return resolveSupersededLoginOutcome(storage, suppressStoredTokenRef.current);
      }
      suppressStoredTokenRef.current = false;
      setActiveAccountIdentity(resolveAccountIdentity(data.user, data.token));
      setRestoreError("");
      setUserState(data.user);
      return {
        ok: true,
        interrupted: false,
        status: "authenticated",
        user: data.user,
      };
    } finally {
      if (!superseded && sessionVersionRef.current === loginVersion) {
        blockingRestorePendingRef.current = false;
        setLoading(false);
      }
    }
  }, [restoreSession, storage]);

  const clearLocalSessionOnLogout = useCallback(async (capturedToken) => {
    const logoutVersion = ++sessionVersionRef.current;
    blockingRestorePendingRef.current = false;
    suppressStoredTokenRef.current = true;
    setActiveAccountIdentity(null);
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
      throw err;
    }
  }, [storage]);

  const logout = useCallback(() => {
    if (logoutPromiseRef.current) {
      return logoutPromiseRef.current;
    }

    const pendingLogout = (async () => {
      setActiveSessionStorage(storage);
      const startingVersion = sessionVersionRef.current;
      const capturedToken = await readCapturedToken(storage);

      try {
        await logoutRequest({ timeout: 3000 });
      } catch {
        /* discard local session even if the server times out or is unreachable */
      }

      // Only clear session state if no newer login occurred while logout was in flight.
      if (sessionVersionRef.current === startingVersion) {
        await clearLocalSessionOnLogout(capturedToken);
      }
    })();

    logoutPromiseRef.current = pendingLogout;
    void pendingLogout
      .finally(() => {
        if (logoutPromiseRef.current === pendingLogout) {
          logoutPromiseRef.current = null;
        }
      })
      .catch(() => { });

    return pendingLogout;
  }, [clearLocalSessionOnLogout, storage]);

  const value = useMemo(
    () => ({
      user: userState,
      setUser,
      getSessionVersion,
      loading,
      restoreError,
      retryRestore: restoreSession,
      login,
      logout,
      isAdmin: !!userState?.roles?.includes("Admin"),
    }),
    [userState, setUser, getSessionVersion, loading, restoreError, restoreSession, login, logout]
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
