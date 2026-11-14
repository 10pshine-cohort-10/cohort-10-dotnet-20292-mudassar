// In-memory cache cleared on login/logout so tab switches (Dashboard <-> Tasks <-> Profile)
// render immediately without flashing skeleton tables or collapsing empty states.
const store = new Map();

export function getPageCache(key) {
  return store.get(key);
}

export function setPageCache(key, value) {
  store.set(key, value);
}

export function clearPageCache() {
  store.clear();
}

