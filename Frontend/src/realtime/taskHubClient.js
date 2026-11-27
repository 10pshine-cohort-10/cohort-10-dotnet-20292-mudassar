import { useEffect, useRef, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { getActiveSessionStorage } from "../auth/tokenStorage";

const TASK_STATUS_UPDATED_EVENT = "TaskStatusUpdated";
const HUB_PATH = "/hubs/tasks";

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

let sharedConnection = null;
let sharedUserId = null;
let connectionState = "disconnected";
let lifecycleSeq = 0;

function setConnectionState(nextState) {
  if (connectionState === nextState) return;
  connectionState = nextState;
  for (const fn of stateListeners) {
    fn(nextState);
  }
}

async function stopSharedConnection() {
  lifecycleSeq += 1;
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
      setConnectionState("connected");
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
    setConnectionState("connected");
  } catch {
    if (sharedConnection === conn) {
      sharedConnection = null;
      sharedUserId = null;
      setConnectionState("disconnected");
    }
  }
}

export function useTaskStatusRealtime(userId, onStatusUpdated) {
  const [state, setState] = useState(connectionState);
  const callbackRef = useRef(onStatusUpdated);
  callbackRef.current = onStatusUpdated;

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

    statusListeners.add(handleStatus);
    stateListeners.add(handleState);
    setState(connectionState);
    void ensureSharedConnection(userId);

    return () => {
      statusListeners.delete(handleStatus);
      stateListeners.delete(handleState);
      if (statusListeners.size === 0) {
        void stopSharedConnection();
      }
    };
  }, [userId]);

  return state;
}

