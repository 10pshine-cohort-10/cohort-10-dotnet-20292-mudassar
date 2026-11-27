import fs from "node:fs";
import https from "node:https";
import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

const LOOPBACK_HOSTS = new Set(["localhost", "127.0.0.1", "[::1]", "::1"]);

function isHttpsUrl(targetUrl) {
  try {
    return new URL(targetUrl).protocol === "https:";
  } catch {
    return false;
  }
}

function validateApiTarget(targetUrl) {
  let parsed;
  try {
    parsed = new URL(targetUrl);
  } catch {
    throw new Error(`[vite] Invalid VITE_DEV_API_TARGET URL: "${targetUrl}"`);
  }
  if (parsed.protocol === "https:") {
    return;
  }
  if (parsed.protocol === "http:" && LOOPBACK_HOSTS.has(parsed.hostname.toLowerCase())) {
    return;
  }
  throw new Error(
    `[vite] Insecure VITE_DEV_API_TARGET "${targetUrl}". Plain HTTP is only allowed for localhost, 127.0.0.1, or [::1]; use HTTPS for remote hosts.`
  );
}

function createProxyAgent(caPath) {
  if (!caPath) return undefined;
  try {
    return new https.Agent({ ca: fs.readFileSync(caPath) });
  } catch (err) {
    console.warn(`[vite] Unable to read CA certificate at "${caPath}": ${err.message}`);
    return undefined;
  }
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");
  const apiTarget = env.VITE_DEV_API_TARGET || "https://localhost:5001";
  validateApiTarget(apiTarget);
  const caPath = env.VITE_DEV_CA_CERT || env.NODE_EXTRA_CA_CERTS;
  const proxyAgent = isHttpsUrl(apiTarget) ? createProxyAgent(caPath) : undefined;

  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        "/api": {
          target: apiTarget,
          changeOrigin: true,
          ...(proxyAgent ? { agent: proxyAgent } : {}),
        },
      },
    },
  };
});
