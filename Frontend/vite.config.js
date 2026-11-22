import fs from "node:fs";
import https from "node:https";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const apiTarget = process.env.VITE_DEV_API_TARGET || "https://localhost:5001";
const caPath = process.env.VITE_DEV_CA_CERT || process.env.NODE_EXTRA_CA_CERTS;

function createProxyAgent() {
  if (!caPath) return undefined;
  try {
    return new https.Agent({ ca: fs.readFileSync(caPath) });
  } catch (err) {
    console.warn(`[vite] Unable to read CA certificate at "${caPath}": ${err.message}`);
    return undefined;
  }
}

const proxyAgent = createProxyAgent();

export default defineConfig({
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
});
