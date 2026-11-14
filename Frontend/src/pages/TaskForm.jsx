import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { createTask, getTaskById, updateTask } from "../api/tasksApi";
import { getCategories } from "../api/categoriesApi";
import { getUserLookup } from "../api/usersApi";
import { getErrorMessage } from "../utils/errorMessage";
import ErrorAlert from "../components/ErrorAlert";
import Field from "../components/Field";

const toDateInput = (iso) => (iso ? new Date(iso).toISOString().slice(0, 10) : "");
const todayStr = () => new Date().toISOString().slice(0, 10);

export default function TaskForm() {
  const { id } = useParams();
  const isEdit = Boolean(id);
  const navigate = useNavigate();

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
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [fieldErrors, setFieldErrors] = useState({});

  useEffect(() => {
    let active = true;
    const init = async () => {
      setLoading(true);
      setError("");
      try {
        const [catsRes, usersRes, taskRes] = await Promise.all([
          getCategories().catch(() => []),
          getUserLookup().catch(() => []),
          isEdit ? getTaskById(id) : Promise.resolve(null)
        ]);
        if (!active) return;
        setCategories(catsRes);
        setUsers(usersRes);

        if (taskRes) {
          setTitle(taskRes.title || "");
          setDescription(taskRes.description || "");
          setStatus(taskRes.status || "Pending");
          setPriority(taskRes.priority || "Medium");
          setDueDate(toDateInput(taskRes.dueDate));
          setCategoryId(taskRes.categoryId ? String(taskRes.categoryId) : "");
          setAssignedToId(taskRes.assignedToId || "");
          setRowVersion(taskRes.rowVersion || "");
        }
      } catch (e) {
        if (active) setError(getErrorMessage(e));
      } finally {
        if (active) setLoading(false);
      }
    };
    void init();
    return () => {
      active = false;
    };
  }, [id, isEdit]);

  const validate = () => {
    const errs = {};
    const trimmed = title.trim();
    if (trimmed.length < 2) errs.title = "Title must be at least 2 characters.";
    else if (trimmed.length > 200) errs.title = "Title must not exceed 200 characters.";

    if (!isEdit && dueDate && dueDate < todayStr()) {
      errs.dueDate = "Due date cannot be in the past.";
    }
    setFieldErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!validate()) return;

    setSaving(true);
    setError("");
    try {
      const payload = {
        title: title.trim(),
        description: description.trim() || null,
        priority,
        dueDate: dueDate ? new Date(`${dueDate}T00:00:00Z`).toISOString() : null,
        categoryId: categoryId ? Number(categoryId) : null,
        assignedToId: assignedToId || null
      };

      if (isEdit) {
        const updated = await updateTask(id, {
          ...payload,
          status,
          rowVersion: rowVersion || null
        });
        navigate(`/tasks/${updated.id}`);
      } else {
        const created = await createTask(payload);
        navigate(`/tasks/${created.id}`);
      }
    } catch (err) {
      setError(getErrorMessage(err));
    } finally {
      setSaving(false);
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
              min={isEdit ? undefined : todayStr()}
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

