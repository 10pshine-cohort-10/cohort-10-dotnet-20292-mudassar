import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { getMe, updateMe } from "../api/authApi";
import { getErrorMessage } from "../utils/errorMessage";
import PageHeader from "../components/PageHeader";
import ErrorAlert from "../components/ErrorAlert";
import Field from "../components/Field";

function getInitials(name = "") {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "U";
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
}

export default function Profile() {
  const { user, setUser, logout } = useAuth();
  const navigate = useNavigate();

  const [profile, setProfile] = useState(user);
  const [fullName, setFullName] = useState(user?.fullName ?? "");
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  // Operation: Get user info from backend and display
  useEffect(() => {
    const controller = new AbortController();
    getMe({ signal: controller.signal })
      .then((freshUser) => {
        setProfile(freshUser);
        setFullName(freshUser.fullName ?? "");
        setUser?.(freshUser);
      })
      .catch((err) => {
        if (controller.signal.aborted || err?.code === "ERR_CANCELED" || err?.name === "CanceledError") return;
        setError(getErrorMessage(err));
      });
    return () => controller.abort();
  }, [setUser]);

  const handleLogout = async () => {
    await logout();
    navigate("/login", { replace: true });
  };

  const handleSaveName = async (e) => {
    e.preventDefault();
    const trimmed = fullName.trim();
    if (trimmed.length < 2) {
      setError("Full name must be at least 2 characters.");
      return;
    }

    setSaving(true);
    setError("");
    setSuccess("");
    try {
      const updated = await updateMe({ fullName: trimmed });
      setProfile(updated);
      setFullName(updated.fullName);
      setUser?.(updated);
      setEditing(false);
      setSuccess("Profile updated successfully.");
    } catch (err) {
      setError(getErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  const roles = profile?.roles?.length ? profile.roles : ["User"];

  return (
    <>
      <PageHeader
        title="Profile"
        subtitle="Manage your account details and session"
        actions={
          <button type="button" className="btn-primary" onClick={handleLogout}>
            Log out
          </button>
        }
      />

      <ErrorAlert message={error} onClose={() => setError("")} />
      <ErrorAlert type="success" message={success} onClose={() => setSuccess("")} />

      <section className="detail-card profile-card" aria-label="User details">
        <div className="profile-top">
          <div className="profile-avatar" aria-hidden="true">
            {getInitials(profile?.fullName)}
          </div>
          <div className="profile-heading">
            <h2>{profile?.fullName}</h2>
            <p>{profile?.email}</p>
            <div className="profile-roles">
              {roles.map((r) => (
                <span key={r} className="role">{r}</span>
              ))}
            </div>
          </div>
        </div>

        <dl className="detail-meta profile-meta">
          <div>
            <dt>Full Name</dt>
            <dd>{profile?.fullName}</dd>
          </div>
          <div>
            <dt>Email Address</dt>
            <dd>{profile?.email}</dd>
          </div>
          <div>
            <dt>Role</dt>
            <dd>{roles.join(", ")}</dd>
          </div>
          <div>
            <dt>Account Status</dt>
            <dd><span className="badge b-completed">Active</span></dd>
          </div>
          {profile?.id && (
            <div>
              <dt>User ID</dt>
              <dd className="mono-id">{profile.id}</dd>
            </div>
          )}
        </dl>

        {editing ? (
          <form className="profile-edit-form" onSubmit={handleSaveName} noValidate>
            <Field
              id="profile-fullname"
              label="Update Full Name"
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              required
            />
            <div className="row-actions">
              <button type="submit" className="btn-primary" disabled={saving}>
                {saving ? "Saving..." : "Save"}
              </button>
              <button
                type="button"
                className="btn-ghost"
                onClick={() => {
                  setEditing(false);
                  setFullName(profile?.fullName ?? "");
                }}
              >
                Cancel
              </button>
            </div>
          </form>
        ) : (
          <div className="form-actions profile-actions">
            <button type="button" className="btn-ghost" onClick={() => setEditing(true)}>
              Edit Name
            </button>
            <button type="button" className="btn-ghost btn-danger" onClick={handleLogout}>
              Log out of TaskFlow
            </button>
          </div>
        )}
      </section>
    </>
  );
}

