import { useEffect, useRef, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { getActiveSessionStorage } from "../auth/tokenStorage";

const TASK_STATUS_UPDATED_EVENT = "TaskStatusUpdated";
const HUB_PATH = "/hubs/tasks";
const INITIAL_RETRY_DELAYS_MS = [2000, 5000, 10000, 30000];

export function resolveTaskHubUrl() {
  const apiBase = import.meta.env.VITE_API_BASE_URL || "/api";
  if (/^https?:\/\//i.test(apiBase)) {
    try {
      const parsed = new URL(apiBase);
      const trimmedPath = parsed.pathname.replace(/\/api\/?$/i, "");
      parsed.pathname = `${trimmedPath}${HUB_PATH}`;
      parsed.search = "";
      parsed.hash = "";
      return parsed.toString();
    } catch {
      return HUB_PATH;
    }
  }
  return HUB_PATH;
}

const statusListeners = new Set();
const stateListeners = new Set();
const reconnectListeners = new Set();

let sharedConnection = null;
let sharedUserId = null;
let connectionState = "disconnected";
let lifecycleSeq = 0;
let retryTimerId = null;
let retryAttempt = 0;

function clearRetryTimer() {
  if (retryTimerId !== null) {
    clearTimeout(retryTimerId);
    retryTimerId = null;
  }
}

function setConnectionState(nextState) {
  if (connectionState === nextState) return;
  connectionState = nextState;
  for (const fn of stateListeners) {
    fn(nextState);
  }
}

function notifyReconnectListeners() {
  for (const fn of reconnectListeners) {
    fn();
  }
}

async function stopSharedConnection() {
  lifecycleSeq += 1;
  clearRetryTimer();
  retryAttempt = 0;
  const conn = sharedConnection;
  sharedConnection = null;
  sharedUserId = null;
  setConnectionState("disconnected");
  if (!conn) return;
  try {
    await conn.stop();
  } catch {
    // Ignore stop errors during teardown or reconnect races.
  }
}

function buildHubConnection() {
  return new HubConnectionBuilder()
    .withUrl(resolveTaskHubUrl(), {
      accessTokenFactory: async () => (await getActiveSessionStorage().getToken()) ?? "",
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}

function scheduleInitialRetry(userId, seq) {
  if (
    lifecycleSeq !== seq ||
    statusListeners.size === 0 ||
    retryAttempt >= INITIAL_RETRY_DELAYS_MS.length
  ) {
    return;
  }
  const delayMs = INITIAL_RETRY_DELAYS_MS[retryAttempt];
  retryAttempt += 1;
  clearRetryTimer();
  retryTimerId = setTimeout(() => {
    retryTimerId = null;
    if (lifecycleSeq !== seq || statusListeners.size === 0) return;
    void ensureSharedConnection(userId);
  }, delayMs);
}

async function ensureSharedConnection(userId) {
  if (!userId) {
    await stopSharedConnection();
    return;
  }

  if (sharedConnection && sharedUserId === userId) {
    return;
  }

  if (sharedConnection) {
    await stopSharedConnection();
  }

  clearRetryTimer();
  const seq = ++lifecycleSeq;
  const conn = buildHubConnection();
  sharedConnection = conn;
  sharedUserId = userId;
  setConnectionState("connecting");

  conn.on(TASK_STATUS_UPDATED_EVENT, (payload) => {
    if (sharedConnection !== conn || !payload) return;
    for (const fn of statusListeners) {
      fn(payload);
    }
  });

  conn.onreconnecting(() => {
    if (sharedConnection === conn) {
      setConnectionState("reconnecting");
    }
  });

  conn.onreconnected(() => {
    if (sharedConnection === conn) {
      retryAttempt = 0;
      setConnectionState("connected");
      notifyReconnectListeners();
    }
  });

  conn.onclose(() => {
    if (sharedConnection === conn) {
      sharedConnection = null;
      sharedUserId = null;
      setConnectionState("disconnected");
    }
  });

  try {
    await conn.start();
    if (lifecycleSeq !== seq || sharedConnection !== conn) {
      await conn.stop().catch(() => {});
      return;
    }
    const wasRetry = retryAttempt > 0;
    retryAttempt = 0;
    setConnectionState("connected");
    if (wasRetry) {
      notifyReconnectListeners();
    }
  } catch {
    if (sharedConnection === conn) {
      sharedConnection = null;
      sharedUserId = null;
      setConnectionState("disconnected");
    }
    scheduleInitialRetry(userId, seq);
  }
}

export function useTaskStatusRealtime(userId, onStatusUpdated, onReconnected) {
  const [state, setState] = useState(connectionState);
  const callbackRef = useRef(onStatusUpdated);
  callbackRef.current = onStatusUpdated;
  const reconnectRef = useRef(onReconnected);
  reconnectRef.current = onReconnected;

  useEffect(() => {
    if (!userId) {
      setState("disconnected");
      return undefined;
    }

    const handleStatus = (event) => {
      callbackRef.current?.(event);
    };
    const handleState = (nextState) => {
      setState(nextState);
    };
    const handleReconnect = () => {
      reconnectRef.current?.();
    };

    statusListeners.add(handleStatus);
    stateListeners.add(handleState);
    reconnectListeners.add(handleReconnect);
    setState(connectionState);
    void ensureSharedConnection(userId);

    return () => {
      statusListeners.delete(handleStatus);
      stateListeners.delete(handleState);
      reconnectListeners.delete(handleReconnect);
      if (statusListeners.size === 0) {
        void stopSharedConnection();
      }
    };
  }, [userId]);

  return state;
}
