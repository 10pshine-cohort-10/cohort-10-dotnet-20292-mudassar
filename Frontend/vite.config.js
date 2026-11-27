import fs from "node:fs";
import https from "node:https";
import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

function isHttpsUrl(targetUrl) {
  try {
    return new URL(targetUrl).protocol === "https:";
  } catch {
    return false;
  }
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
