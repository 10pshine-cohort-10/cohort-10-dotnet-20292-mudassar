import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { getSummary } from "../api/dashboardApi";
import { getTasks } from "../api/tasksApi";
import { getErrorMessage } from "../utils/errorMessage";
import SummaryCard from "../components/SummaryCard";
import StatusBadge from "../components/StatusBadge";
import PriorityBadge from "../components/PriorityBadge";

const RANK = { Critical: 4, High: 3, Medium: 2, Low: 1 };
const fmt = (d) => (d ? new Date(d).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" }) : "No due date");

export default function Dashboard() {
  const { user, isAdmin } = useAuth();
  const [summary, setSummary] = useState(null);
  const [recent, setRecent] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const [s, t] = await Promise.all([getSummary(), getTasks({ pageSize: 5 })]);
      setSummary(s);
      setRecent(t.items ?? []);
    } catch (e) {
      if (e.response?.status === 404) {
        setSummary({ completed: 0, inProgress: 0, pending: 0, total: 0 });
        setRecent([]);
      } else {
        setError(getErrorMessage(e));
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  // Light touch of "smart" guidance: surface the most urgent open task from the recent list.
  const next = recent
    .filter((t) => t.status !== "Completed")
    .sort((a, b) => (RANK[b.priority] ?? 0) - (RANK[a.priority] ?? 0))[0];

  const isEmpty = !loading && !error && summary?.total === 0;

  return (
    <>
      <div className="head">
        <div>
          <h1>Welcome back, {user.fullName}</h1>
          <p>{isAdmin ? "Showing all tasks across the organization" : "Showing your tasks"}</p>
        </div>
        <Link to="/tasks/new" className="btn-primary">+ New Task</Link>
      </div>

      {error && (
        <div className="alert error" role="alert">
          <p>{error}</p>
          <button className="retry" onClick={load}>Retry</button>
        </div>
      )}

      {next && (
        <div className="focus">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 2l2.2 6.3L20.5 10l-6.3 1.7L12 18l-2.2-6.3L3.5 10l6.3-1.7L12 2z" /></svg>
          <span>Suggested focus: <Link to={`/tasks/${next.id}`}><strong>{next.title}</strong></Link> ({next.priority} priority).</span>
        </div>
      )}

      {isEmpty ? (
        <div className="empty">
          <h2>No tasks yet</h2>
          <p>Create a task to start tracking your work.</p>
          <Link to="/tasks/new" className="btn-primary">Create your first task</Link>
        </div>
      ) : (
        <>
          <section className="cards" aria-label="Task summary">
            <SummaryCard tone="done" label="Completed" to="/tasks?status=Completed" value={summary?.completed} hint="Finished tasks" loading={loading} />
            <SummaryCard tone="doing" label="In Progress" to="/tasks?status=InProgress" value={summary?.inProgress} hint="Being worked on" loading={loading} />
            <SummaryCard tone="pending" label="Pending" to="/tasks?status=Pending" value={summary?.pending} hint="Not started yet" loading={loading} />
            <SummaryCard tone="total" label="Total" to="/tasks" value={summary?.total} hint="All tasks" loading={loading} />
          </section>

          <section aria-labelledby="recent-h">
            <div className="section-h">
              <h2 id="recent-h">Recent tasks</h2>
              <Link to="/tasks">View all tasks</Link>
            </div>
            <div className="tbl-wrap">
              <table>
                <thead><tr><th>Title</th><th>Priority</th><th>Status</th><th>Due date</th></tr></thead>
                <tbody>
                  {loading
                    ? Array.from({ length: 5 }, (_, i) => (
                        <tr key={i}><td colSpan={4}><div className="skel" style={{ height: 18 }} /></td></tr>
                      ))
                    : recent.map((t) => (
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
        </>
      )}
    </>
  );
}
