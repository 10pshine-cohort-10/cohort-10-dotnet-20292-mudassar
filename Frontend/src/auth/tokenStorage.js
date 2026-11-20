const TOKEN_KEY = "token";
const DB_NAME = "taskflow_auth_db";
const STORE_NAME = "session";
const CHANNEL_NAME = "taskflow_auth_channel";

const memoryFallback = new Map();
const listeners = new Set();

const channel =
  typeof BroadcastChannel !== "undefined" ? new BroadcastChannel(CHANNEL_NAME) : null;

if (channel) {
  channel.onmessage = (event) => {
    const payload = event?.data;
    if (!payload || payload.key !== TOKEN_KEY) return;
    if (payload.newValue) {
      memoryFallback.set(TOKEN_KEY, payload.newValue);
    } else {
      memoryFallback.delete(TOKEN_KEY);
    }
    for (const fn of listeners) {
      fn({ key: TOKEN_KEY, newValue: payload.newValue ?? null });
    }
  };
}

function notifyChange(newValue) {
  if (channel) {
    channel.postMessage({ key: TOKEN_KEY, newValue: newValue ?? null });
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
        resolve(value);
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
    const db = await openAuthDb();
    if (!db) {
      memoryFallback.set(TOKEN_KEY, token);
      notifyChange(token);
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
    notifyChange(token);
  },
  async removeToken() {
    const db = await openAuthDb();
    if (!db) {
      memoryFallback.delete(TOKEN_KEY);
      notifyChange(null);
      return;
    }
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
    notifyChange(null);
  },
};

let activeSessionStorage = defaultSessionStorage;

export function setActiveSessionStorage(storage) {
  activeSessionStorage = storage ?? defaultSessionStorage;
}

export function getActiveSessionStorage() {
  return activeSessionStorage;
}
