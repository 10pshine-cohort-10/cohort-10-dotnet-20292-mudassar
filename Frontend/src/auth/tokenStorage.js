const TOKEN_KEY = "token";
const DB_NAME = "taskflow_auth_db";
const STORE_NAME = "session";
const CHANNEL_NAME = "taskflow_auth_channel";

const TAB_ID =
  typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`;

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

function toStoredRecord(meta) {
  return {
    token: meta.value ?? null,
    value: meta.value ?? null,
    timestamp: meta.timestamp,
    revision: meta.revision,
    originId: meta.originId ?? "",
  };
}

function extractStoredRecord(raw) {
  if (raw === null || raw === undefined) {
    return { token: null, meta: null };
  }
  if (typeof raw === "string") {
    return {
      token: raw,
      meta: { timestamp: 0, revision: 0, originId: "", value: raw },
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
    };
  }
  return { token: null, meta: null };
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

function applyWinningMetaInMemory(winningMeta) {
  const currentStoredMeta = extractStoredRecord(memoryFallback.get(TOKEN_KEY)).meta;
  const referenceMeta =
    currentStoredMeta && compareWriteMeta(currentStoredMeta, latestWriteMeta) > 0
      ? currentStoredMeta
      : latestWriteMeta;

  if (compareWriteMeta(winningMeta, referenceMeta) < 0) {
    return false;
  }

  latestWriteMeta = winningMeta;
  memoryFallback.set(TOKEN_KEY, toStoredRecord(winningMeta));
  loggedOutOverride = !winningMeta.value;
  return true;
}

const channel =
  typeof BroadcastChannel !== "undefined" ? new BroadcastChannel(CHANNEL_NAME) : null;

if (channel) {
  channel.onmessage = (event) => {
    const payload = event?.data;
    if (!payload || payload.key !== TOKEN_KEY) return;

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

export function subscribeTokenChanges(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

const IDB_OPEN_TIMEOUT_MS = 3000;
let indexedDbUnavailable = false;
let everOpenedIndexedDb = false;
let transientOpenTimeout = false;

function openAuthDb({ forCleanup = false } = {}) {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === "undefined" || indexedDbUnavailable) {
      resolve(null);
      return;
    }
    if (!forCleanup && transientOpenTimeout) {
      resolve(null);
      return;
    }
    let settled = false;
    const timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      transientOpenTimeout = true;
      if (forCleanup) {
        reject(new Error("Timed out opening IndexedDB."));
        return;
      }
      resolve(null);
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
          req.result?.close();
          return;
        }
        settled = true;
        everOpenedIndexedDb = true;
        transientOpenTimeout = false;
        resolve(req.result);
      };
      req.onerror = () => {
        clearTimeout(timer);
        if (settled) return;
        settled = true;
        if (forCleanup && (everOpenedIndexedDb || transientOpenTimeout)) {
          reject(req.error ?? new Error("Failed to open IndexedDB."));
          return;
        }
        indexedDbUnavailable = true;
        resolve(null);
      };
    } catch (err) {
      clearTimeout(timer);
      if (!settled) {
        settled = true;
        if (forCleanup && (everOpenedIndexedDb || transientOpenTimeout)) {
          reject(err instanceof Error ? err : new Error("Failed to open IndexedDB."));
          return;
        }
        indexedDbUnavailable = true;
        resolve(null);
      }
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
      if (!persistedMeta || compareWriteMeta(incomingMeta, persistedMeta) >= 0) {
        applied = true;
        winningMeta = incomingMeta;
        putReq = store.put(toStoredRecord(incomingMeta), TOKEN_KEY);
      } else {
        applied = false;
        winningMeta = persistedMeta;
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

export const defaultSessionStorage = {
  async getToken() {
    if (loggedOutOverride) return null;
    const db = await openAuthDb();
    if (!db) {
      return extractStoredRecord(memoryFallback.get(TOKEN_KEY)).token;
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
        resolve(loggedOutOverride ? null : effectiveToken);
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
      const appliedInMemory = applyWinningMetaInMemory(incomingMeta);
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
    let shouldNotify = true;
    try {
      const db = await openAuthDb({ forCleanup: true });
      if (!db) {
        return;
      }
      const { applied } = await persistMetaIfNewerInDb(
        db,
        incomingMeta,
        "Failed to remove token from IndexedDB."
      );
      shouldNotify = applied;
    } finally {
      if (shouldNotify) {
        notifyChange(incomingMeta);
      }
    }
  },
  async removeTokenIfMatch(expectedToken) {
    if (!expectedToken) return false;
    const db = await openAuthDb({ forCleanup: true });
    if (!db) {
      const current = extractStoredRecord(memoryFallback.get(TOKEN_KEY));
      if (!loggedOutOverride && current.token === expectedToken) {
        const nextMeta = createNextWriteMeta(null, current.meta);
        const applied = applyWinningMetaInMemory(nextMeta);
        if (applied) {
          notifyChange(nextMeta);
        }
        return applied;
      }
      return false;
    }
    const { removed, winningMeta } = await new Promise((resolve, reject) => {
      const tx = db.transaction(STORE_NAME, "readwrite");
      const store = tx.objectStore(STORE_NAME);
      const getReq = store.get(TOKEN_KEY);
      let matched = false;
      let nextMeta = null;
      let putReq = null;
      getReq.onsuccess = () => {
        const current = extractStoredRecord(getReq.result);
        const hasInMemoryState = memoryFallback.has(TOKEN_KEY) || latestWriteMeta.revision > 0;
        const inMemoryRecord = hasInMemoryState
          ? extractStoredRecord(memoryFallback.get(TOKEN_KEY))
          : null;
        const inMemoryWins = Boolean(
          inMemoryRecord?.meta &&
            (!current.meta || compareWriteMeta(inMemoryRecord.meta, current.meta) > 0)
        );
        const effectiveRecord = inMemoryWins ? inMemoryRecord : current;
        const inMemoryMatches = !hasInMemoryState || inMemoryRecord.token === expectedToken;
        if (!loggedOutOverride && effectiveRecord.token === expectedToken && inMemoryMatches) {
          matched = true;
          nextMeta = createNextWriteMeta(null, effectiveRecord.meta);
          putReq = store.put(toStoredRecord(nextMeta), TOKEN_KEY);
        }
      };
      tx.oncomplete = () => {
        db.close();
        resolve({ removed: matched, winningMeta: nextMeta });
      };
      tx.onabort = () => {
        const err =
          tx.error ??
          putReq?.error ??
          getReq.error ??
          new Error("Failed to conditionally remove token from IndexedDB.");
        db.close();
        reject(err);
      };
      tx.onerror = () => {
        const err =
          tx.error ??
          putReq?.error ??
          getReq.error ??
          new Error("Failed to conditionally remove token from IndexedDB.");
        db.close();
        reject(err);
      };
    });
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
