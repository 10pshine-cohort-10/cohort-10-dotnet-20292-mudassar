const TOKEN_KEY = "token";

export const defaultSessionStorage = {
  async getToken() {
    return localStorage.getItem(TOKEN_KEY);
  },
  async setToken(token) {
    localStorage.setItem(TOKEN_KEY, token);
  },
  async removeToken() {
    localStorage.removeItem(TOKEN_KEY);
  },
};

let activeSessionStorage = defaultSessionStorage;

export function setActiveSessionStorage(storage) {
  activeSessionStorage = storage ?? defaultSessionStorage;
}

export function getActiveSessionStorage() {
  return activeSessionStorage;
}
