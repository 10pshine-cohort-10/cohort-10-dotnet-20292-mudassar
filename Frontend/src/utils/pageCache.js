// Bounded in-memory LRU cache cleared on login/logout so tab switches (Dashboard <-> Tasks <-> Profile)
// render immediately without growing indefinitely across search queries.
const MAX_CACHE_ENTRIES = 50;
const store = new Map();

export function getPageCache(key) {
  if (!store.has(key)) return undefined;
  const value = store.get(key);
  store.delete(key);
  store.set(key, value);
  return value;
}

export function setPageCache(key, value) {
  if (store.has(key)) {
    store.delete(key);
  } else if (store.size >= MAX_CACHE_ENTRIES) {
    const oldestKey = store.keys().next().value;
    if (oldestKey !== undefined) {
      store.delete(oldestKey);
    }
  }
  store.set(key, value);
}

export function deletePageCacheByPrefix(prefix) {
  for (const key of Array.from(store.keys())) {
    if (String(key).startsWith(prefix)) {
      store.delete(key);
    }
  }
}

export function clearPageCache() {
  store.clear();
}
