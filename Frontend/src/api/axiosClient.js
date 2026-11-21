import axios from "axios";
import { getActiveSessionStorage } from "../auth/tokenStorage";

const client = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || "/api",
  timeout: 15000,
});
const PUBLIC_PATHS = ["/auth/login", "/auth/register"];

client.interceptors.request.use(async (config) => {
  const token = await getActiveSessionStorage().getToken();
  config._requestToken = token;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

client.interceptors.response.use(
  (res) => res,
  async (error) => {
    const url = error.config?.url ?? "";
    const isPublic = PUBLIC_PATHS.some((p) => url.includes(p));
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
