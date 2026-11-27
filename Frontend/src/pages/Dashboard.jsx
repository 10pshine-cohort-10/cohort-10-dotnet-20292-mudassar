import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { getSummary } from "../api/dashboardApi";
import { getTasks } from "../api/tasksApi";
import { getErrorMessage } from "../utils/errorMessage";
import { formatDueDate } from "../utils/formatDate";
import { deletePageCacheByPrefix, getPageCache, setPageCache } from "../utils/pageCache";
import { useTaskStatusRealtime } from "../realtime/taskHubClient";
import PageHeader from "../components/PageHeader";
import LiveBadge from "../components/LiveBadge";
import SummaryCard from "../components/SummaryCard";
import StatusBadge from "../components/StatusBadge";
import PriorityBadge from "../components/PriorityBadge";

const RANK = { Critical: 4, High: 3, Medium: 2, Low: 1 };
const fmt = (d) => formatDueDate(d);

const ZERO_SUMMARY = { completed: 0, inProgress: 0, pending: 0, total: 0 };

function isCanceledResult(res) {
  return (
    res.status === "rejected" &&
    (res.reason?.code === "ERR_CANCELED" || res.reason?.name === "CanceledError")
  );
}

export default function Dashboard() {
  const { user, isAdmin } = useAuth();
  const cacheKey = `dashboard:${user?.id ?? "anon"}`;
  const cached = getPageCache(cacheKey);

  const [summary, setSummary] = useState(() => cached?.summary ?? null);
  const [recent, setRecent] = useState(() => cached?.recent ?? []);
  const [tasksLoadedOk, setTasksLoadedOk] = useState(false);
  const [loading, setLoading] = useState(() => !cached);
  const [summaryPending, setSummaryPending] = useState(() => !cached?.summary);
  const [error, setError] = useState("");

  const controllerRef = useRef(null);

  const load = useCallback(async () => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    const { signal } = controller;

    setLoading(true);
    setSummaryPending(true);
    setError("");

    const [summaryRes, tasksRes] = await Promise.allSettled([
      getSummary({ signal }),
      getTasks({ pageSize: 5 }, { signal }),
    ]);
    if (signal.aborted || isCanceledResult(tasksRes) || isCanceledResult(summaryRes)) return;

    if (tasksRes.status === "fulfilled") {
      setRecent(tasksRes.value?.items ?? []);
      setTasksLoadedOk(true);
    } else {
      setRecent((prev) => prev);
      setTasksLoadedOk(false);
    }

    if (summaryRes.status === "fulfilled") {
      setSummary(summaryRes.value ?? ZERO_SUMMARY);
    } else {
      setSummary((prev) => prev);
    }
    setSummaryPending(false);

    if (summaryRes.status === "fulfilled" && tasksRes.status === "fulfilled") {
      setPageCache(cacheKey, {
        summary: summaryRes.value ?? ZERO_SUMMARY,
        recent: tasksRes.value?.items ?? [],
      });
    }

    const firstFailure = [summaryRes, tasksRes].find((r) => r.status === "rejected");
    setError(firstFailure ? getErrorMessage(firstFailure.reason) : "");
    setLoading(false);
  }, [cacheKey]);

  const loadRef = useRef(load);
  loadRef.current = load;

  useEffect(() => {
    void load();
    return () => controllerRef.current?.abort();
  }, [load]);

  const handleRealtimeStatusUpdate = useCallback(
    (event) => {
      if (!event?.taskId) return;
      const owner = user?.id ?? "anon";
      deletePageCacheByPrefix(`dashboard:${owner}`);
      deletePageCacheByPrefix(`tasks:${owner}:`);
      setRecent((prev) =>
        prev.map((item) =>
          item.id === event.taskId
            ? {
                ...item,
                status: event.newStatus,
                updatedAt: event.updatedAt ?? item.updatedAt,
                rowVersion: event.rowVersion ?? item.rowVersion,
              }
            : item
        )
      );
      void loadRef.current();
    },
    [user?.id]
  );

  const realtimeState = useTaskStatusRealtime(user?.id, handleRealtimeStatusUpdate);

  const next = recent
    .filter((t) => t.status !== "Completed")
    .sort((a, b) => (RANK[b.priority] ?? 0) - (RANK[a.priority] ?? 0))[0];

  return (
    <>
      <PageHeader
        title={`Welcome back, ${user?.fullName ?? ""}`}
        subtitle={isAdmin ? "Showing all tasks across the organization" : "Showing your tasks"}
        actions={
          <div className="row-actions">
            <LiveBadge state={realtimeState} />
            <Link to="/tasks/new" className="btn-primary">+ New Task</Link>
          </div>
        }
      />

      {error && (
        <div className="alert error" role="alert">
          <p>{error}</p>
          <button className="retry" onClick={load} disabled={loading}>Retry</button>
        </div>
      )}

      {next && (
        <div className="focus">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 2l2.2 6.3L20.5 10l-6.3 1.7L12 18l-2.2-6.3L3.5 10l6.3-1.7L12 2z" /></svg>
          <span>Suggested focus: <Link to={`/tasks/${next.id}`}><strong>{next.title}</strong></Link> ({next.priority} priority).</span>
        </div>
      )}

      <section className="cards" aria-label="Task status summary">
        <SummaryCard tone="done" label="Completed" to="/tasks?status=Completed" value={summary?.completed} hint="Finished tasks" loading={summaryPending && summary === null} />
        <SummaryCard tone="doing" label="In Progress" to="/tasks?status=InProgress" value={summary?.inProgress} hint="Being worked on" loading={summaryPending && summary === null} />
        <SummaryCard tone="pending" label="Pending" to="/tasks?status=Pending" value={summary?.pending} hint="Not started yet" loading={summaryPending && summary === null} />
        <SummaryCard tone="total" label="Total" to="/tasks" value={summary?.total} hint="All tasks" loading={summaryPending && summary === null} />
      </section>

      {tasksLoadedOk && recent.length === 0 && (
        <div className="empty">
          <h2>No tasks yet</h2>
          <p>Create a task to start tracking your work.</p>
          <Link to="/tasks/new" className="btn-primary">Create your first task</Link>
        </div>
      )}

      {recent.length > 0 && (
        <section aria-labelledby="recent-h">
          <div className="section-h">
            <h2 id="recent-h">Recent tasks</h2>
            <Link to="/tasks">View all tasks</Link>
          </div>
          <div className="tbl-wrap">
            <table>
              <thead><tr><th>Title</th><th>Priority</th><th>Status</th><th>Due date</th></tr></thead>
              <tbody>
                {recent.map((t) => (
                  <tr key={t.id}>
                    <td><Link className="row-link" to={`/tasks/${t.id}`}>{t.title}</Link></td>
                    <td><PriorityBadge priority={t.priority} /></td>
                    <td><StatusBadge status={t.status} /></td>
                    <td className="due">{fmt(t.dueDate)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </>
  );
}
