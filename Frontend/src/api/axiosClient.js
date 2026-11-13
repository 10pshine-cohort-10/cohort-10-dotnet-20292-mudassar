import axios from "axios";

const client = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || "/api",
});
const PUBLIC_PATHS = ["/auth/login", "/auth/register"];

client.interceptors.request.use((config) => {
  const token = localStorage.getItem("token");
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

client.interceptors.response.use(
  (res) => res,
  (error) => {
    const url = error.config?.url ?? "";
    const isPublic = PUBLIC_PATHS.some((p) => url.includes(p));
    // A wrong password also returns 401: keep the page so the message stays visible.
    if (error.response?.status === 401 && !isPublic) {
      localStorage.removeItem("token");
      window.location.href = "/login";
    }
    return Promise.reject(error);
  }
);

export default client;
