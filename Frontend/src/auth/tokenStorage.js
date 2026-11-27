const TOKEN_KEY = "token";
const DB_NAME = "taskflow_auth_db";
const STORE_NAME = "session";
const CHANNEL_NAME = "taskflow_auth_channel";

function createTabId() {
  if (typeof crypto !== "undefined") {
    if (typeof crypto.randomUUID === "function") {
      return crypto.randomUUID();
    }
    if (typeof crypto.getRandomValues === "function") {
      const bytes = new Uint8Array(16);
      crypto.getRandomValues(bytes);
      return Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
    }
  }
  return `${Date.now()}-${globalThis.performance?.now?.() ?? 0}`;
}

const TAB_ID = createTabId();

const memoryFallback = new Map();
const listeners = new Set();

// Tracks whether a logout removal has occurred in this tab so subsequent reads
// never restore a stale token even if an underlying storage delete fails.
let loggedOutOverride = false;

// Monotonic write metadata for deterministic cross-tab convergence in memoryFallback.
let latestWriteMeta = {
  timestamp: 0,
  revision: 0,
  originId: "",
  value: null,
};

let activeAccountIdentity = null;
let activeSessionRevision = 0;
let hasResolvedInitialSession = false;

function decodeBase64Url(segment) {
  const normalized = String(segment).replaceAll("-", "+").replaceAll("_", "/");
  const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, "=");
  if (typeof atob === "function") {
    return atob(padded);
  }
  return "";
}

export function getTokenAccountIdentity(token) {
  if (!token || typeof token !== "string") return null;
  const parts = token.split(".");
  if (parts.length === 3) {
    try {
      const json = decodeBase64Url(parts[1]);
      if (json) {
        const payload = JSON.parse(json);
        const claimId =
          payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"] ??
          payload.sub ??
          payload.nameid ??
          payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"] ??
          payload.email;
        if (claimId) return String(claimId);
      }
    } catch {
      // Fall back to the raw token string if it is not a standard JSON JWT payload.
    }
  }
  return token;
}

export function getActiveAccountIdentity() {
  return activeAccountIdentity;
}

export function setActiveAccountIdentity(identity) {
  const normalized = identity ? String(identity) : null;
  if (activeAccountIdentity !== normalized || !hasResolvedInitialSession) {
    activeAccountIdentity = normalized;
    activeSessionRevision += 1;
    hasResolvedInitialSession = true;
  }
}

export function getActiveSessionRevision() {
  return activeSessionRevision;
}

export function hasActiveSessionSnapshot() {
  return hasResolvedInitialSession;
}

function updateActiveAccountFromToken(token) {
  const nextIdentity = getTokenAccountIdentity(token);
  if (activeAccountIdentity !== nextIdentity || !hasResolvedInitialSession) {
    activeAccountIdentity = nextIdentity;
    activeSessionRevision += 1;
    hasResolvedInitialSession = true;
  }
}

function compareWriteMeta(incoming, current) {
  if (incoming.timestamp !== current.timestamp) {
    return incoming.timestamp > current.timestamp ? 1 : -1;
  }
  if (incoming.revision !== current.revision) {
    return incoming.revision > current.revision ? 1 : -1;
  }
  const incomingValue = String(incoming.value ?? "");
  const currentValue = String(current.value ?? "");
  if (incomingValue !== currentValue) {
    return incomingValue > currentValue ? 1 : -1;
  }
  const incomingOrigin = String(incoming.originId ?? "");
  const currentOrigin = String(current.originId ?? "");
  if (incomingOrigin !== currentOrigin) {
    return incomingOrigin > currentOrigin ? 1 : -1;
  }
  return 0;
}

function toStoredRecord(meta, { memoryOnly = false } = {}) {
  const record = {
    token: meta.value ?? null,
    value: meta.value ?? null,
    timestamp: meta.timestamp,
    revision: meta.revision,
    originId: meta.originId ?? "",
  };
  if (memoryOnly) {
    record.memoryOnly = true;
  }
  return record;
}

function extractStoredRecord(raw) {
  if (raw === null || raw === undefined) {
    return { token: null, meta: null, memoryOnly: false };
  }
  if (typeof raw === "string") {
    return {
      token: raw,
      meta: { timestamp: 0, revision: 0, originId: "", value: raw },
      memoryOnly: false,
    };
  }
  if (typeof raw === "object") {
    const token = raw.token ?? raw.value ?? null;
    const hasMeta = typeof raw.timestamp === "number" && typeof raw.revision === "number";
    return {
      token,
      meta: hasMeta
        ? {
            timestamp: raw.timestamp,
            revision: raw.revision,
            originId: raw.originId ?? "",
            value: token,
          }
        : { timestamp: 0, revision: 0, originId: "", value: token },
      memoryOnly: Boolean(raw.memoryOnly),
    };
  }
  return { token: null, meta: null, memoryOnly: false };
}

function createNextWriteMeta(newValue, baseMeta = latestWriteMeta) {
  const normalized = newValue ?? null;
  const currentPersistedMeta = extractStoredRecord(memoryFallback.get(TOKEN_KEY)).meta;
  const highestTimestamp = Math.max(
    Date.now(),
    latestWriteMeta.timestamp,
    baseMeta?.timestamp ?? 0,
    currentPersistedMeta?.timestamp ?? 0
  );
  const highestRevision = Math.max(
    latestWriteMeta.revision,
    baseMeta?.revision ?? 0,
    currentPersistedMeta?.revision ?? 0
  );
  return {
    timestamp: highestTimestamp,
    revision: highestRevision + 1,
    originId: TAB_ID,
    value: normalized,
  };
}

function getCurrentSessionMeta() {
  const currentStoredMeta = extractStoredRecord(memoryFallback.get(TOKEN_KEY)).meta;
  return currentStoredMeta && compareWriteMeta(currentStoredMeta, latestWriteMeta) > 0
    ? currentStoredMeta
    : latestWriteMeta;
}

function applyWinningMetaInMemory(winningMeta, { memoryOnly = false } = {}) {
  const referenceMeta = getCurrentSessionMeta();

  if (compareWriteMeta(winningMeta, referenceMeta) < 0) {
    return false;
  }

  latestWriteMeta = winningMeta;
  memoryFallback.set(TOKEN_KEY, toStoredRecord(winningMeta, { memoryOnly }));
  loggedOutOverride = !winningMeta.value;
  updateActiveAccountFromToken(winningMeta.value);
  return true;
}

const channel =
  typeof BroadcastChannel !== "undefined" ? new BroadcastChannel(CHANNEL_NAME) : null;

if (channel) {
  channel.onmessage = (event) => {
    const payload = event?.data;
    if (payload?.key !== TOKEN_KEY) return;

    const normalizedValue = payload.newValue ?? null;
    const hasMeta =
      typeof payload.timestamp === "number" && typeof payload.revision === "number";

    const incomingMeta = hasMeta
      ? {
          timestamp: payload.timestamp,
          revision: payload.revision,
          originId: payload.originId ?? "",
          value: normalizedValue,
        }
      : createNextWriteMeta(normalizedValue);

    // Deterministic winner rule: compare incoming metadata against the persisted in-memory
    // winner and update the token + metadata together only when incoming is not stale.
    if (!applyWinningMetaInMemory(incomingMeta)) {
      return;
    }

    for (const fn of listeners) {
      fn({ key: TOKEN_KEY, newValue: normalizedValue });
    }
  };
}

function notifyChange(meta) {
  if (channel) {
    channel.postMessage({
      key: TOKEN_KEY,
      newValue: meta.value,
      timestamp: meta.timestamp,
      revision: meta.revision,
      originId: meta.originId,
    });
  }
}

const recoveryListeners = new Set();

export function subscribeTokenChanges(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function subscribeStorageRecovery(listener) {
  recoveryListeners.add(listener);
  return () => recoveryListeners.delete(listener);
}

function notifyStorageRecovery() {
  for (const fn of recoveryListeners) {
    fn();
  }
}

function createStorageTimeoutError() {
  const err = new Error("Timed out opening session storage. Please retry.");
  err.code = "STORAGE_OPEN_TIMEOUT";
  err.retryable = true;
  return err;
}

const IDB_OPEN_TIMEOUT_MS = 3000;
let indexedDbUnavailable = false;
let everOpenedIndexedDb = false;
let transientOpenTimeout = false;

function handleLateOpenSuccess(db) {
  const recoveredFromTimeout = transientOpenTimeout;
  everOpenedIndexedDb = true;
  indexedDbUnavailable = false;
  transientOpenTimeout = false;
  db?.close();
  if (recoveredFromTimeout) {
    notifyStorageRecovery();
  }
}

function handleOpenFailure(err, { forCleanup, allowMemoryFallback }, resolve, reject) {
  const shouldReject =
    forCleanup && (!allowMemoryFallback || everOpenedIndexedDb || transientOpenTimeout);
  if (shouldReject) {
    reject(err instanceof Error ? err : new Error("Failed to open IndexedDB."));
    return;
  }
  indexedDbUnavailable = true;
  resolve(null);
}

function openAuthDb({ forCleanup = false, allowMemoryFallback = true } = {}) {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === "undefined" || (!forCleanup && indexedDbUnavailable)) {
      resolve(null);
      return;
    }
    let settled = false;
    const timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      transientOpenTimeout = true;
      reject(createStorageTimeoutError());
    }, IDB_OPEN_TIMEOUT_MS);

    try {
      const req = indexedDB.open(DB_NAME, 1);
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains(STORE_NAME)) {
          db.createObjectStore(STORE_NAME);
        }
      };
      req.onsuccess = () => {
        clearTimeout(timer);
        if (settled) {
          handleLateOpenSuccess(req.result);
          return;
        }
        settled = true;
        everOpenedIndexedDb = true;
        indexedDbUnavailable = false;
        transientOpenTimeout = false;
        resolve(req.result);
      };
      req.onerror = () => {
        clearTimeout(timer);
        if (settled) return;
        settled = true;
        handleOpenFailure(req.error, { forCleanup, allowMemoryFallback }, resolve, reject);
      };
    } catch (err) {
      clearTimeout(timer);
      if (settled) return;
      settled = true;
      handleOpenFailure(err, { forCleanup, allowMemoryFallback }, resolve, reject);
    }
  });
}

function persistMetaIfNewerInDb(db, incomingMeta, defaultErrorMessage) {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, "readwrite");
    const store = tx.objectStore(STORE_NAME);
    const getReq = store.get(TOKEN_KEY);
    let applied = false;
    let winningMeta = incomingMeta;
    let putReq = null;

    getReq.onsuccess = () => {
      const { meta: persistedMeta } = extractStoredRecord(getReq.result);
      const currentSessionMeta = getCurrentSessionMeta();
      const isTombstone = incomingMeta.value === null;
      const sessionAllowsWrite =
        compareWriteMeta(incomingMeta, currentSessionMeta) >= 0 &&
        (!isTombstone || !currentSessionMeta.value);
      const isNewerThanPersisted =
        !persistedMeta || compareWriteMeta(incomingMeta, persistedMeta) >= 0;

      if (sessionAllowsWrite && isNewerThanPersisted) {
        applied = true;
        winningMeta = incomingMeta;
        putReq = store.put(toStoredRecord(incomingMeta), TOKEN_KEY);
      } else {
        applied = false;
        if (persistedMeta && compareWriteMeta(persistedMeta, currentSessionMeta) >= 0) {
          winningMeta = persistedMeta;
        } else {
          winningMeta = currentSessionMeta;
        }
      }
    };

    tx.oncomplete = () => {
      db.close();
      applyWinningMetaInMemory(winningMeta);
      resolve({ applied, winningMeta });
    };
    tx.onabort = () => {
      const err = tx.error ?? putReq?.error ?? getReq.error ?? new Error(defaultErrorMessage);
      db.close();
      reject(err);
    };
    tx.onerror = () => {
      const err = tx.error ?? putReq?.error ?? getReq.error ?? new Error(defaultErrorMessage);
      db.close();
      reject(err);
    };
  });
}

function removeTokenFromMemoryIfMatch(expectedToken) {
  const current = extractStoredRecord(memoryFallback.get(TOKEN_KEY));
  if (loggedOutOverride || current.token !== expectedToken) {
    return false;
  }
  const nextMeta = createNextWriteMeta(null, current.meta);
  const applied = applyWinningMetaInMemory(nextMeta, { memoryOnly: true });
  if (applied) {
    notifyChange(nextMeta);
  }
  return applied;
}

function getEffectiveRecordForMatch(persistedRaw) {
  const current = extractStoredRecord(persistedRaw);
  const hasInMemoryState = memoryFallback.has(TOKEN_KEY) || latestWriteMeta.revision > 0;
  const inMemoryRecord = hasInMemoryState
    ? extractStoredRecord(memoryFallback.get(TOKEN_KEY))
    : null;
  const inMemoryWins = Boolean(
    inMemoryRecord?.meta &&
      (!current.meta || compareWriteMeta(inMemoryRecord.meta, current.meta) > 0)
  );
  return {
    effectiveRecord: inMemoryWins ? inMemoryRecord : current,
    hasInMemoryState,
    inMemoryRecord,
  };
}

function removeTokenFromDbIfMatch(db, expectedToken) {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, "readwrite");
    const store = tx.objectStore(STORE_NAME);
    const getReq = store.get(TOKEN_KEY);
    let matched = false;
    let nextMeta = null;
    let putReq = null;

    getReq.onsuccess = () => {
      const { effectiveRecord, hasInMemoryState, inMemoryRecord } = getEffectiveRecordForMatch(
        getReq.result
      );
      const inMemoryMatches = !hasInMemoryState || inMemoryRecord.token === expectedToken;
      if (!loggedOutOverride && effectiveRecord.token === expectedToken && inMemoryMatches) {
        matched = true;
        nextMeta = createNextWriteMeta(null, effectiveRecord.meta);
        putReq = store.put(toStoredRecord(nextMeta), TOKEN_KEY);
      }
    };

    const fail = () => {
      const err =
        tx.error ??
        putReq?.error ??
        getReq.error ??
        new Error("Failed to conditionally remove token from IndexedDB.");
      db.close();
      reject(err);
    };

    tx.oncomplete = () => {
      db.close();
      resolve({ removed: matched, winningMeta: nextMeta });
    };
    tx.onabort = fail;
    tx.onerror = fail;
  });
}

export const defaultSessionStorage = {
  async getToken() {
    if (loggedOutOverride) return null;
    const db = await openAuthDb();
    if (!db) {
      const token = extractStoredRecord(memoryFallback.get(TOKEN_KEY)).token;
      updateActiveAccountFromToken(token);
      return token;
    }
    return new Promise((resolve, reject) => {
      const tx = db.transaction(STORE_NAME, "readonly");
      const req = tx.objectStore(STORE_NAME).get(TOKEN_KEY);
      let record = { token: null, meta: null };
      req.onsuccess = () => {
        record = extractStoredRecord(req.result);
      };
      tx.oncomplete = () => {
        db.close();
        if (record.meta) {
          applyWinningMetaInMemory(record.meta);
        }
        const effectiveToken = memoryFallback.has(TOKEN_KEY)
          ? extractStoredRecord(memoryFallback.get(TOKEN_KEY)).token
          : record.token;
        const resolved = loggedOutOverride ? null : effectiveToken;
        updateActiveAccountFromToken(resolved);
        resolve(resolved);
      };
      tx.onabort = () => {
        const err = tx.error ?? req.error ?? new Error("Failed to read token from IndexedDB.");
        db.close();
        reject(err);
      };
      tx.onerror = () => {
        const err = tx.error ?? req.error ?? new Error("Failed to read token from IndexedDB.");
        db.close();
        reject(err);
      };
    });
  },
  async setToken(token) {
    const incomingMeta = createNextWriteMeta(token);
    const db = await openAuthDb();
    if (!db) {
      const memoryOnly = !everOpenedIndexedDb && !transientOpenTimeout;
      const appliedInMemory = applyWinningMetaInMemory(incomingMeta, { memoryOnly });
      if (appliedInMemory) {
        notifyChange(incomingMeta);
      }
      return appliedInMemory;
    }
    const { applied, winningMeta } = await persistMetaIfNewerInDb(
      db,
      incomingMeta,
      "Failed to store token in IndexedDB."
    );
    const isCurrentWinner =
      applied && compareWriteMeta(incomingMeta, latestWriteMeta) >= 0;
    if (isCurrentWinner) {
      notifyChange(winningMeta);
    }
    return isCurrentWinner;
  },
  async removeToken() {
    const incomingMeta = createNextWriteMeta(null);
    applyWinningMetaInMemory(incomingMeta);
    let shouldNotify = false;
    try {
      const db = await openAuthDb({ forCleanup: true });
      if (!db) {
        shouldNotify = true;
        return;
      }
      const { applied } = await persistMetaIfNewerInDb(
        db,
        incomingMeta,
        "Failed to remove token from IndexedDB."
      );
      if (!applied) {
        throw new Error("Failed to remove token from IndexedDB because a newer session token exists.");
      }
      shouldNotify = true;
    } finally {
      if (shouldNotify) {
        notifyChange(incomingMeta);
      }
    }
  },
  async removeTokenIfMatch(expectedToken) {
    if (!expectedToken) return false;
    const inMemoryEntry = extractStoredRecord(memoryFallback.get(TOKEN_KEY));
    const isConfirmedMemoryOnly =
      !everOpenedIndexedDb &&
      !transientOpenTimeout &&
      inMemoryEntry.memoryOnly &&
      inMemoryEntry.token === expectedToken;

    const db = await openAuthDb({
      forCleanup: true,
      allowMemoryFallback: isConfirmedMemoryOnly,
    });
    if (!db) {
      if (!isConfirmedMemoryOnly && typeof indexedDB !== "undefined") {
        throw new Error("Failed to open IndexedDB for conditional token removal.");
      }
      return removeTokenFromMemoryIfMatch(expectedToken);
    }
    const { removed, winningMeta } = await removeTokenFromDbIfMatch(db, expectedToken);
    if (removed && winningMeta) {
      const applied = applyWinningMetaInMemory(winningMeta);
      if (applied) {
        notifyChange(winningMeta);
      }
      return applied;
    }
    return removed;
  },
};

let activeSessionStorage = defaultSessionStorage;

export function setActiveSessionStorage(storage) {
  activeSessionStorage = storage ?? defaultSessionStorage;
}

export function getActiveSessionStorage() {
  return activeSessionStorage;
}
