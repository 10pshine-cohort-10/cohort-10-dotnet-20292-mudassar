import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { createTask, getTaskById, updateTask } from "../api/tasksApi";
import { getCategories } from "../api/categoriesApi";
import { getUserLookup } from "../api/usersApi";
import { getErrorMessage } from "../utils/errorMessage";
import { deletePageCacheByPrefix } from "../utils/pageCache";
import ErrorAlert from "../components/ErrorAlert";
import Field from "../components/Field";

const toDateInput = (iso) => (iso ? String(iso).slice(0, 10) : "");
const utcTodayStr = () => new Date().toISOString().slice(0, 10);

const toArray = (value) => (Array.isArray(value) ? value : []);

const SKIPPED_LOOKUP = { attempted: false, ok: true, value: null };

const settleLookup = (promise) =>
  promise.then(
    (value) => ({ attempted: true, ok: true, value }),
    (reason) => ({ attempted: true, ok: false, reason })
  );

const getFirstLookupError = (catsOutcome, usersOutcome) => {
  if (!catsOutcome.ok) return getErrorMessage(catsOutcome.reason);
  if (!usersOutcome.ok) return getErrorMessage(usersOutcome.reason);
  return "";
};

const extractTaskFields = (taskRes) => ({
  title: taskRes.title || "",
  description: taskRes.description || "",
  status: taskRes.status || "Pending",
  priority: taskRes.priority || "Medium",
  dueDate: toDateInput(taskRes.dueDate),
  categoryId: taskRes.categoryId ? String(taskRes.categoryId) : "",
  assignedToId: taskRes.assignedToId || "",
  rowVersion: taskRes.rowVersion || "",
});

export default function TaskForm() {
  const { id } = useParams();
  const isEdit = Boolean(id);
  const navigate = useNavigate();
  const { user } = useAuth();

  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [status, setStatus] = useState("Pending");
  const [priority, setPriority] = useState("Medium");
  const [dueDate, setDueDate] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [assignedToId, setAssignedToId] = useState("");
  const [rowVersion, setRowVersion] = useState("");

  const [categories, setCategories] = useState([]);
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [lookupError, setLookupError] = useState("");
  const [retryingLookups, setRetryingLookups] = useState(false);
  const [fieldErrors, setFieldErrors] = useState({});

  const requestGenRef = useRef(0);
  const saveGenRef = useRef(0);
  const lookupGenRef = useRef(0);
  const failedLookupsRef = useRef({ categories: false, users: false });

  const retryLookups = useCallback(async () => {
    const gen = ++lookupGenRef.current;
    const { categories: needCategories, users: needUsers } = failedLookupsRef.current;
    const retryBoth = !needCategories && !needUsers;

    setRetryingLookups(true);
    setLookupError("");

    const [catsOutcome, usersOutcome] = await Promise.all([
      needCategories || retryBoth ? settleLookup(getCategories()) : Promise.resolve(SKIPPED_LOOKUP),
      needUsers || retryBoth ? settleLookup(getUserLookup()) : Promise.resolve(SKIPPED_LOOKUP),
    ]);

    if (lookupGenRef.current !== gen) return;

    if (catsOutcome.attempted && catsOutcome.ok) setCategories(toArray(catsOutcome.value));
    if (usersOutcome.attempted && usersOutcome.ok) setUsers(toArray(usersOutcome.value));

    failedLookupsRef.current = {
      categories: catsOutcome.attempted ? !catsOutcome.ok : needCategories,
      users: usersOutcome.attempted ? !usersOutcome.ok : needUsers,
    };
    setLookupError(getFirstLookupError(catsOutcome, usersOutcome));
    setRetryingLookups(false);
  }, []);

  const loadForm = useCallback(async () => {
    const gen = ++requestGenRef.current;
    lookupGenRef.current += 1;
    saveGenRef.current += 1;
    setSaving(false);
    setRetryingLookups(false);
    setLoading(true);
    setLoadFailed(false);
    setError("");
    setLookupError("");
    try {
      const [catsOutcome, usersOutcome, taskRes] = await Promise.all([
        settleLookup(getCategories()),
        settleLookup(getUserLookup()),
        isEdit ? getTaskById(id) : Promise.resolve(null),
      ]);
      if (requestGenRef.current !== gen) return;

      setCategories(catsOutcome.ok ? toArray(catsOutcome.value) : []);
      setUsers(usersOutcome.ok ? toArray(usersOutcome.value) : []);
      failedLookupsRef.current = {
        categories: !catsOutcome.ok,
        users: !usersOutcome.ok,
      };
      setLookupError(getFirstLookupError(catsOutcome, usersOutcome));

      if (taskRes) {
        const fields = extractTaskFields(taskRes);
        setTitle(fields.title);
        setDescription(fields.description);
        setStatus(fields.status);
        setPriority(fields.priority);
        setDueDate(fields.dueDate);
        setCategoryId(fields.categoryId);
        setAssignedToId(fields.assignedToId);
        setRowVersion(fields.rowVersion);
      }
    } catch (e) {
      if (requestGenRef.current === gen) {
        setError(getErrorMessage(e));
        if (isEdit) setLoadFailed(true);
      }
    } finally {
      if (requestGenRef.current === gen) setLoading(false);
    }
  }, [id, isEdit]);

  useEffect(() => {
    void loadForm();
    return () => {
      requestGenRef.current += 1;
      saveGenRef.current += 1;
      lookupGenRef.current += 1;
    };
  }, [loadForm]);

  const validate = () => {
    const errs = {};
    const trimmed = title.trim();
    if (trimmed.length < 2) errs.title = "Title must be at least 2 characters.";
    else if (trimmed.length > 200) errs.title = "Title must not exceed 200 characters.";

    if (!isEdit && dueDate && dueDate < utcTodayStr()) {
      errs.dueDate = "Due date cannot be in the past.";
    }
    setFieldErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!validate()) return;

    const saveGen = ++saveGenRef.current;
    const originatingUserIdKey = user?.id ?? "anon";
    setSaving(true);
    setError("");
    try {
      const payload = {
        title: title.trim(),
        description: description.trim() || null,
        priority,
        dueDate: dueDate || null,
        categoryId: categoryId ? Number(categoryId) : null,
        assignedToId: assignedToId || null
      };

      if (isEdit) {
        const updated = await updateTask(id, {
          ...payload,
          status,
          rowVersion: rowVersion || null
        });
        deletePageCacheByPrefix(`tasks:${originatingUserIdKey}:`);
        deletePageCacheByPrefix(`dashboard:${originatingUserIdKey}`);
        if (saveGenRef.current === saveGen) {
          navigate(`/tasks/${updated.id}`);
        }
      } else {
        const created = await createTask(payload);
        deletePageCacheByPrefix(`tasks:${originatingUserIdKey}:`);
        deletePageCacheByPrefix(`dashboard:${originatingUserIdKey}`);
        if (saveGenRef.current === saveGen) {
          navigate(`/tasks/${created.id}`);
        }
      }
    } catch (err) {
      if (saveGenRef.current === saveGen) {
        setError(getErrorMessage(err));
      }
    } finally {
      if (saveGenRef.current === saveGen) {
        setSaving(false);
      }
    }
  };

  if (loading) {
    return (
      <div className="form-card">
        <div className="skel" style={{ height: 28, width: "45%", marginBottom: 18 }} />
        <div className="skel" style={{ height: 44, marginBottom: 14 }} />
        <div className="skel" style={{ height: 96, marginBottom: 14 }} />
      </div>
    );
  }

  if (loadFailed) {
    return (
      <>
        <ErrorAlert message={error || "Could not load task for editing."} />
        <div className="row-actions">
          <button type="button" className="btn-primary" onClick={() => void loadForm()}>
            Retry
          </button>
          <Link to="/tasks" className="btn-ghost">Back to Tasks</Link>
        </div>
      </>
    );
  }

  const submitLabel = isEdit ? "Save Changes" : "Create Task";

  return (
    <>
      <div className="head">
        <div>
          <h1>{isEdit ? "Edit Task" : "Create New Task"}</h1>
          <p>{isEdit ? "Update task details, assignment, priority, and due date" : "Fill in the task details below"}</p>
        </div>
        <Link to={isEdit ? `/tasks/${id}` : "/tasks"} className="btn-ghost">Cancel</Link>
      </div>

      <div className="form-card">
        <ErrorAlert message={error} onClose={() => setError("")} />
        {lookupError && (
          <div className="alert error" role="alert">
            <span>{lookupError}</span>
            <div className="row-actions">
              <button
                type="button"
                className="retry"
                disabled={retryingLookups}
                onClick={() => void retryLookups()}
              >
                {retryingLookups ? "Retrying..." : "Retry lookups"}
              </button>
            </div>
          </div>
        )}

        <form onSubmit={handleSubmit} noValidate>
          <Field
            id="task-title"
            label="Title *"
            placeholder="e.g., Prepare Q4 sprint release notes"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            error={fieldErrors.title}
            required
          />

          <div className="field">
            <label htmlFor="task-description">Description</label>
            <textarea
              id="task-description"
              className="textarea-field"
              rows={4}
              placeholder="Add context, acceptance criteria, or notes..."
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </div>

          <div className="form-grid">
            <div className="field">
              <label htmlFor="task-priority">Priority</label>
              <select
                id="task-priority"
                className="select-field"
                value={priority}
                onChange={(e) => setPriority(e.target.value)}
              >
                <option value="Low">Low</option>
                <option value="Medium">Medium</option>
                <option value="High">High</option>
                <option value="Critical">Critical</option>
              </select>
            </div>

            {isEdit && (
              <div className="field">
                <label htmlFor="task-status">Status</label>
                <select
                  id="task-status"
                  className="select-field"
                  value={status}
                  onChange={(e) => setStatus(e.target.value)}
                >
                  <option value="Pending">Pending</option>
                  <option value="InProgress">In Progress</option>
                  <option value="Completed">Completed</option>
                </select>
              </div>
            )}

            <div className="field">
              <label htmlFor="task-category">Category</label>
              <select
                id="task-category"
                className="select-field"
                value={categoryId}
                onChange={(e) => setCategoryId(e.target.value)}
              >
                <option value="">Uncategorized</option>
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>
            </div>

            <div className="field">
              <label htmlFor="task-assignee">Assign To</label>
              <select
                id="task-assignee"
                className="select-field"
                value={assignedToId}
                onChange={(e) => setAssignedToId(e.target.value)}
              >
                <option value="">Unassigned</option>
                {users.map((u) => (
                  <option key={u.id} value={u.id}>{u.fullName}</option>
                ))}
              </select>
            </div>

            <Field
              id="task-duedate"
              label="Due Date"
              type="date"
              min={isEdit ? undefined : utcTodayStr()}
              value={dueDate}
              onChange={(e) => setDueDate(e.target.value)}
              error={fieldErrors.dueDate}
            />
          </div>

          <div className="form-actions">
            <Link to={isEdit ? `/tasks/${id}` : "/tasks"} className="btn-ghost">Cancel</Link>
            <button type="submit" className="btn-primary" disabled={saving}>
              {saving ? "Saving..." : submitLabel}
            </button>
          </div>
        </form>
      </div>
    </>
  );
}

