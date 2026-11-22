import axios from "axios";
import {
  getActiveAccountIdentity,
  getActiveSessionRevision,
  getActiveSessionStorage,
  getTokenAccountIdentity,
  hasActiveSessionSnapshot,
} from "../auth/tokenStorage";

const client = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || "/api",
  timeout: 15000,
});
const PUBLIC_PATHS = ["/auth/login", "/auth/register"];

const isPublicAuthRequest = (config) => {
  const method = (config?.method ?? "get").toLowerCase();
  const url = config?.url ?? "";
  return method === "post" && PUBLIC_PATHS.some((p) => url === p || url.endsWith(p));
};

function createSessionChangedError() {
  const err = new Error("Request aborted because the active account session changed.");
  err.code = "ERR_CANCELED";
  err.name = "CanceledError";
  return err;
}

client.interceptors.request.use(async (config) => {
  if (isPublicAuthRequest(config)) {
    config._requestToken = null;
    return config;
  }
  const hadSnapshotAtStart = hasActiveSessionSnapshot();
  const expectedIdentity =
    config._expectedAccountIdentity !== undefined
      ? config._expectedAccountIdentity
      : getActiveAccountIdentity();
  const startingRevision = getActiveSessionRevision();

  const token = await getActiveSessionStorage().getToken();
  const tokenIdentity = getTokenAccountIdentity(token);

  const sessionChangedSinceStart =
    hadSnapshotAtStart || config._expectedAccountIdentity !== undefined
      ? tokenIdentity !== expectedIdentity
      : getActiveSessionRevision() !== startingRevision &&
        getActiveAccountIdentity() !== tokenIdentity;

  if (sessionChangedSinceStart) {
    config._requestToken = null;
    if (config.headers) {
      delete config.headers.Authorization;
    }
    return Promise.reject(createSessionChangedError());
  }

  config._requestToken = token;
  config._requestAccountIdentity = tokenIdentity;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

client.interceptors.response.use(
  (res) => res,
  async (error) => {
    const isPublic = isPublicAuthRequest(error.config);
    const requestToken = error.config?._requestToken;
    const storage = getActiveSessionStorage();
    // A wrong password also returns 401: keep the page so the message stays visible.
    // Atomically compare-and-delete so we only clear credentials and redirect if the failing
    // request's token still matches the currently stored session token.
    if (error.response?.status === 401 && !isPublic && requestToken) {
      let removed = false;
      if (typeof storage.removeTokenIfMatch === "function") {
        try {
          removed = await storage.removeTokenIfMatch(requestToken);
        } catch {
          // Keep the original 401 rejection if conditional token cleanup fails.
        }
      }
      if (removed) {
        window.location.href = "/login";
      }
    }
    return Promise.reject(error);
  }
);

export default client;
