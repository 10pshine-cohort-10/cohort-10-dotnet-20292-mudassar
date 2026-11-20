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

function recordLocalWrite(newValue) {
  const normalized = newValue ?? null;
  const nextTimestamp = Math.max(Date.now(), latestWriteMeta.timestamp);
  const nextRevision = latestWriteMeta.revision + 1;
  latestWriteMeta = {
    timestamp: nextTimestamp,
    revision: nextRevision,
    originId: TAB_ID,
    value: normalized,
  };
  return latestWriteMeta;
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
      : {
          timestamp: Math.max(Date.now(), latestWriteMeta.timestamp),
          revision: latestWriteMeta.revision + 1,
          originId: payload.originId ?? "",
          value: normalizedValue,
        };

    // Deterministic winner rule: ignore concurrent losing writes so all tabs converge
    // on the same memoryFallback token, while any causal follow-up write has a higher
    // timestamp or revision and replaces the local value.
    if (compareWriteMeta(incomingMeta, latestWriteMeta) < 0) {
      return;
    }

    latestWriteMeta = incomingMeta;
    if (normalizedValue) {
      loggedOutOverride = false;
      memoryFallback.set(TOKEN_KEY, normalizedValue);
    } else {
      loggedOutOverride = true;
      memoryFallback.delete(TOKEN_KEY);
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

function openAuthDb() {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === "undefined") {
      resolve(null);
      return;
    }
    const req = indexedDB.open(DB_NAME, 1);
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME);
      }
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error ?? new Error("Failed to open IndexedDB."));
  });
}

export const defaultSessionStorage = {
  async getToken() {
    if (loggedOutOverride) return null;
    const db = await openAuthDb();
    if (!db) return memoryFallback.get(TOKEN_KEY) ?? null;
    return new Promise((resolve, reject) => {
      const tx = db.transaction(STORE_NAME, "readonly");
      const req = tx.objectStore(STORE_NAME).get(TOKEN_KEY);
      let value = null;
      req.onsuccess = () => {
        value = req.result ?? null;
      };
      tx.oncomplete = () => {
        db.close();
        resolve(loggedOutOverride ? null : value);
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
    loggedOutOverride = false;
    memoryFallback.set(TOKEN_KEY, token);
    const meta = recordLocalWrite(token);
    const db = await openAuthDb();
    if (!db) {
      notifyChange(meta);
      return;
    }
    await new Promise((resolve, reject) => {
      const tx = db.transaction(STORE_NAME, "readwrite");
      const req = tx.objectStore(STORE_NAME).put(token, TOKEN_KEY);
      tx.oncomplete = () => {
        db.close();
        resolve();
      };
      tx.onabort = () => {
        const err = tx.error ?? req.error ?? new Error("Failed to store token in IndexedDB.");
        db.close();
        reject(err);
      };
      tx.onerror = () => {
        const err = tx.error ?? req.error ?? new Error("Failed to store token in IndexedDB.");
        db.close();
        reject(err);
      };
    });
    notifyChange(meta);
  },
  async removeToken() {
    loggedOutOverride = true;
    memoryFallback.delete(TOKEN_KEY);
    const meta = recordLocalWrite(null);
    const db = await openAuthDb();
    if (!db) {
      notifyChange(meta);
      return;
    }
    try {
      await new Promise((resolve, reject) => {
        const tx = db.transaction(STORE_NAME, "readwrite");
        const req = tx.objectStore(STORE_NAME).delete(TOKEN_KEY);
        tx.oncomplete = () => {
          db.close();
          resolve();
        };
        tx.onabort = () => {
          const err = tx.error ?? req.error ?? new Error("Failed to remove token from IndexedDB.");
          db.close();
          reject(err);
        };
        tx.onerror = () => {
          const err = tx.error ?? req.error ?? new Error("Failed to remove token from IndexedDB.");
          db.close();
          reject(err);
        };
      });
    } finally {
      notifyChange(meta);
    }
  },
  async removeTokenIfMatch(expectedToken) {
    if (!expectedToken) return false;
    const db = await openAuthDb();
    if (!db) {
      if ( !loggedOutOverride && memoryFallback.get(TOKEN_KEY) === expectedToken) {
        loggedOutOverride = true;
        memoryFallback.delete(TOKEN_KEY);
        const meta = recordLocalWrite(null);
        notifyChange(meta);
        return true;
      }
      return false;
    }
    const removed = await new Promise((resolve, reject) => {
      const tx = db.transaction(STORE_NAME, "readwrite");
      const store = tx.objectStore(STORE_NAME);
      const getReq = store.get(TOKEN_KEY);
      let matched = false;
      let deleteReq = null;
      getReq.onsuccess = () => {
        if (!loggedOutOverride && getReq.result === expectedToken) {
          matched = true;
          deleteReq = store.delete(TOKEN_KEY);
        }
      };
      tx.oncomplete = () => {
        db.close();
        resolve(matched);
      };
      tx.onabort = () => {
        const err =
          tx.error ??
          deleteReq?.error ??
          getReq.error ??
          new Error("Failed to conditionally remove token from IndexedDB.");
        db.close();
        reject(err);
      };
      tx.onerror = () => {
        const err =
          tx.error ??
          deleteReq?.error ??
          getReq.error ??
          new Error("Failed to conditionally remove token from IndexedDB.");
        db.close();
        reject(err);
      };
    });
    if (removed) {
      loggedOutOverride = true;
      memoryFallback.delete(TOKEN_KEY);
      const meta = recordLocalWrite(null);
      notifyChange(meta);
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
