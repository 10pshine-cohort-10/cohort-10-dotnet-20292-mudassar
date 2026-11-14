import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { getSummary } from "../api/dashboardApi";
import { getTasks } from "../api/tasksApi";
import { getErrorMessage } from "../utils/errorMessage";
import { getPageCache, setPageCache } from "../utils/pageCache";
import PageHeader from "../components/PageHeader";
import SummaryCard from "../components/SummaryCard";
import StatusBadge from "../components/StatusBadge";
import PriorityBadge from "../components/PriorityBadge";

const RANK = { Critical: 4, High: 3, Medium: 2, Low: 1 };
const fmt = (d) =>
  d
    ? new Date(d).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" })
    : "No due date";

const ZERO_SUMMARY = { completed: 0, inProgress: 0, pending: 0, total: 0 };

function computeSummaryFromTasks(items = [], totalCount) {
  const completed = items.filter((t) => t.status === "Completed").length;
  const inProgress = items.filter((t) => t.status === "InProgress").length;
  const pending = items.filter((t) => t.status === "Pending").length;
  const total = typeof totalCount === "number" ? totalCount : items.length;
  return { completed, inProgress, pending, total };
}

export default function Dashboard() {
  const { user, isAdmin } = useAuth();
  const cacheKey = `dashboard:${user?.id ?? "anon"}`;
  const cached = getPageCache(cacheKey);

  const [summary, setSummary] = useState(() => cached?.summary ?? ZERO_SUMMARY);
  const [recent, setRecent] = useState(() => cached?.recent ?? []);
  const [hasLoaded, setHasLoaded] = useState(() => Boolean(cached));
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const controllerRef = useRef(null);

  const load = useCallback(async () => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    const { signal } = controller;

    setLoading(true);
    setError("");

    const [summaryRes, tasksRes] = await Promise.allSettled([
      getSummary({ signal }),
      getTasks({ pageSize: 5 }, { signal }),
    ]);
    if (signal.aborted) return;

    const tasksCanceled =
      tasksRes.status === "rejected" &&
      (tasksRes.reason?.code === "ERR_CANCELED" || tasksRes.reason?.name === "CanceledError");
    const summaryCanceled =
      summaryRes.status === "rejected" &&
      (summaryRes.reason?.code === "ERR_CANCELED" || summaryRes.reason?.name === "CanceledError");
    if (tasksCanceled || summaryCanceled) return;

    let nextRecent = cached?.recent ?? [];
    let nextSummary = cached?.summary ?? ZERO_SUMMARY;

    if (tasksRes.status === "fulfilled") {
      nextRecent = tasksRes.value?.items ?? [];
    }

    if (summaryRes.status === "fulfilled") {
      nextSummary = summaryRes.value ?? ZERO_SUMMARY;
    } else if (summaryRes.reason?.response?.status === 404) {
      // Graceful fallback if the running backend process hasn't loaded DashboardController yet.
      nextSummary =
        tasksRes.status === "fulfilled"
          ? computeSummaryFromTasks(nextRecent, tasksRes.value?.totalCount)
          : ZERO_SUMMARY;
    }

    setRecent(nextRecent);
    setSummary(nextSummary);
    setHasLoaded(true);
    setPageCache(cacheKey, { summary: nextSummary, recent: nextRecent });

    const realFailure = [summaryRes, tasksRes].find(
      (r) => r.status === "rejected" && r.reason?.response?.status !== 404
    );
    setError(realFailure ? getErrorMessage(realFailure.reason) : "");
    setLoading(false);
  }, [cacheKey]);

  useEffect(() => {
    load();
    return () => controllerRef.current?.abort();
  }, [load]);

  const next = recent
    .filter((t) => t.status !== "Completed")
    .sort((a, b) => (RANK[b.priority] ?? 0) - (RANK[a.priority] ?? 0))[0];

  return (
    <>
      <PageHeader
        title={`Welcome back, ${user?.fullName ?? ""}`}
        subtitle={isAdmin ? "Showing all tasks across the organization" : "Showing your tasks"}
        actions={<Link to="/tasks/new" className="btn-primary">+ New Task</Link>}
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

      <section className="cards" aria-label="Task summary">
        <SummaryCard tone="done" label="Completed" to="/tasks?status=Completed" value={summary.completed} hint="Finished tasks" loading={false} />
        <SummaryCard tone="doing" label="In Progress" to="/tasks?status=InProgress" value={summary.inProgress} hint="Being worked on" loading={false} />
        <SummaryCard tone="pending" label="Pending" to="/tasks?status=Pending" value={summary.pending} hint="Not started yet" loading={false} />
        <SummaryCard tone="total" label="Total" to="/tasks" value={summary.total} hint="All tasks" loading={false} />
      </section>

      {hasLoaded && recent.length === 0 ? (
        <div className="empty">
          <h2>No tasks yet</h2>
          <p>Create a task to start tracking your work.</p>
          <Link to="/tasks/new" className="btn-primary">Create your first task</Link>
        </div>
      ) : recent.length > 0 ? (
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
      ) : null}
    </>
  );
}
