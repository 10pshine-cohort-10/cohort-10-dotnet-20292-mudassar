import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { deleteTask, getTaskById, updateTaskStatus } from "../api/tasksApi";
import { getErrorMessage } from "../utils/errorMessage";
import { formatDueDate } from "../utils/formatDate";
import { deletePageCacheByPrefix } from "../utils/pageCache";
import ErrorAlert from "../components/ErrorAlert";
import StatusBadge from "../components/StatusBadge";
import PriorityBadge from "../components/PriorityBadge";

const fmtDate = (d) => formatDueDate(d);

export default function TaskDetail() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { user, isAdmin } = useAuth();

  const [task, setTask] = useState(null);
  const [loading, setLoading] = useState(true);
  const [updatingStatus, setUpdatingStatus] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState("");

  const routeGenRef = useRef(0);
  const statusGenRef = useRef(0);
  const activeIdRef = useRef(id);
  activeIdRef.current = id;

  const isCurrentRouteGen = (routeGen, targetId) =>
    routeGenRef.current === routeGen && activeIdRef.current === targetId;

  const isCurrentStatusGen = (routeGen, statusGen, targetId) =>
    isCurrentRouteGen(routeGen, targetId) && statusGenRef.current === statusGen;

  const load = useCallback(async () => {
    const routeGen = ++routeGenRef.current;
    statusGenRef.current += 1;
    const targetId = id;
    setTask(null);
    setUpdatingStatus(false);
    setDeleting(false);
    setLoading(true);
    setError("");
    try {
      const res = await getTaskById(targetId);
      if (!isCurrentRouteGen(routeGen, targetId)) return;
      setTask(res);
    } catch (e) {
      if (!isCurrentRouteGen(routeGen, targetId)) return;
      setError(getErrorMessage(e));
    } finally {
      if (isCurrentRouteGen(routeGen, targetId)) {
        setLoading(false);
      }
    }
  }, [id]);

  useEffect(() => {
    void load();
    return () => {
      routeGenRef.current += 1;
      statusGenRef.current += 1;
    };
  }, [load]);

  const invalidateTaskCaches = () => {
    const userIdKey = user?.id ?? "anon";
    deletePageCacheByPrefix(`tasks:${userIdKey}:`);
    deletePageCacheByPrefix(`dashboard:${userIdKey}`);
  };

  const handleStatusChange = async (newStatus) => {
    if (!task || updatingStatus || deleting) return;
    const targetId = id;
    const routeGen = routeGenRef.current;
    const statusGen = ++statusGenRef.current;
    setUpdatingStatus(true);
    setError("");
    try {
      const updated = await updateTaskStatus(targetId, newStatus);
      invalidateTaskCaches();
      if (!isCurrentStatusGen(routeGen, statusGen, targetId)) return;
      setTask(updated);
    } catch (e) {
      if (!isCurrentStatusGen(routeGen, statusGen, targetId)) return;
      setError(getErrorMessage(e));
    } finally {
      if (isCurrentStatusGen(routeGen, statusGen, targetId)) {
        setUpdatingStatus(false);
      }
    }
  };

  const handleDelete = async () => {
    if (!task || deleting || !window.confirm(`Delete task "${task.title}"?`)) return;
    const targetId = id;
    const routeGen = routeGenRef.current;
    setDeleting(true);
    setError("");
    try {
      await deleteTask(targetId);
      invalidateTaskCaches();
      if (!isCurrentRouteGen(routeGen, targetId)) return;
      navigate("/tasks", { replace: true });
    } catch (e) {
      if (!isCurrentRouteGen(routeGen, targetId)) return;
      setError(getErrorMessage(e));
    } finally {
      if (isCurrentRouteGen(routeGen, targetId)) {
        setDeleting(false);
      }
    }
  };

  if (loading) {
    return (
      <div className="detail-card">
        <div className="skel" style={{ height: 28, width: "55%", marginBottom: 16 }} />
        <div className="skel" style={{ height: 80, marginBottom: 16 }} />
      </div>
    );
  }

  if (!task) {
    return (
      <>
        <ErrorAlert message={error || "Task not found."} />
        <Link to="/tasks" className="btn-ghost">← Back to Tasks</Link>
      </>
    );
  }

  const canDelete = isAdmin || task.createdById === user?.id;

  return (
    <>
      <div className="head">
        <div>
          <Link to="/tasks" className="back-link">← Back to Tasks</Link>
          <h1>{task.title}</h1>
        </div>
        <div className="row-actions">
          <Link to={`/tasks/${task.id}/edit`} className="btn-primary">Edit Task</Link>
          {canDelete && (
            <button type="button" className="btn-ghost btn-danger" onClick={handleDelete} disabled={deleting}>
              Delete Task
            </button>
          )}
        </div>
      </div>

      <ErrorAlert message={error} onClose={() => setError("")} />

      <div className="detail-card">
        <div className="detail-badges">
          <PriorityBadge priority={task.priority} />
          <StatusBadge status={task.status} />
          <div className="status-switch">
            <label htmlFor="detail-status">Update status:</label>
            <select
              id="detail-status"
              className="filter-select"
              value={task.status}
              disabled={updatingStatus || deleting}
              onChange={(e) => handleStatusChange(e.target.value)}
            >
              <option value="Pending">Pending</option>
              <option value="InProgress">In Progress</option>
              <option value="Completed">Completed</option>
            </select>
          </div>
        </div>

        <div className="detail-section">
          <h2>Description</h2>
          <p className="detail-description">
            {task.description || "No description provided."}
          </p>
        </div>

        <dl className="detail-meta">
          <div>
            <dt>Category</dt>
            <dd>{task.categoryName || "Uncategorized"}</dd>
          </div>
          <div>
            <dt>Assigned To</dt>
            <dd>{task.assignedToName || "Unassigned"}</dd>
          </div>
          <div>
            <dt>Due Date</dt>
            <dd>{fmtDate(task.dueDate)}</dd>
          </div>
          <div>
            <dt>Created By</dt>
            <dd>{task.createdByName}</dd>
          </div>
          <div>
            <dt>Created At</dt>
            <dd>{fmtDate(task.createdAt)}</dd>
          </div>
          {task.updatedAt && (
            <div>
              <dt>Last Updated</dt>
              <dd>{fmtDate(task.updatedAt)}</dd>
            </div>
          )}
        </dl>
      </div>
    </>
  );
}

