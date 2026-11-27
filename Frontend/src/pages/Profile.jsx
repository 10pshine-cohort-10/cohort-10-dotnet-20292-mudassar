import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { getActiveAccountIdentity } from "../auth/tokenStorage";
import { getMe, updateMe } from "../api/authApi";
import { getErrorMessage } from "../utils/errorMessage";
import PageHeader from "../components/PageHeader";
import ErrorAlert from "../components/ErrorAlert";
import Field from "../components/Field";

function getInitials(name = "") {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "U";
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return `${parts[0][0]}${parts.at(-1)[0]}`.toUpperCase();
}

export default function Profile() {
  const { user, setUser, getSessionVersion, retryRestore, logout } = useAuth();
  const navigate = useNavigate();

  const [profile, setProfile] = useState(user);
  const [fullName, setFullName] = useState(user?.fullName ?? "");
  const [nameError, setNameError] = useState("");
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const hasInteractedRef = useRef(false);
  const logoutPromiseRef = useRef(null);
  const editAccountIdentityRef = useRef(user?.id ?? getActiveAccountIdentity());

  // Operation: Get user info from backend and display
  useEffect(() => {
    const controller = new AbortController();
    const sessionAtStart = getSessionVersion();
    getMe({ signal: controller.signal })
      .then((freshUser) => {
        if (hasInteractedRef.current || getSessionVersion() !== sessionAtStart) return;
        setProfile(freshUser);
        setFullName(freshUser.fullName ?? "");
        editAccountIdentityRef.current = freshUser.id ?? getActiveAccountIdentity();
        setUser?.(freshUser, sessionAtStart);
      })
      .catch((err) => {
        if (
          hasInteractedRef.current ||
          controller.signal.aborted ||
          getSessionVersion() !== sessionAtStart ||
          err?.code === "ERR_CANCELED" ||
          err?.name === "CanceledError"
        ) {
          return;
        }
        setError(getErrorMessage(err));
      });
    return () => controller.abort();
  }, [setUser, getSessionVersion]);

  const handleLogout = async () => {
    const isInitiator = !logoutPromiseRef.current;
    const pendingLogout =
      logoutPromiseRef.current ?? (logoutPromiseRef.current = Promise.resolve(logout()));
    try {
      await pendingLogout;
      if (isInitiator) {
        navigate("/login", { replace: true });
      }
    } catch (err) {
      if (isInitiator) {
        setError(getErrorMessage(err));
      }
    } finally {
      if (logoutPromiseRef.current === pendingLogout) {
        logoutPromiseRef.current = null;
      }
    }
  };

  const handleSaveName = async (e) => {
    e.preventDefault();
    hasInteractedRef.current = true;
    const trimmed = fullName.trim();
    if (trimmed.length < 2) {
      setNameError("Full name must be at least 2 characters.");
      return;
    }

    const sessionAtStart = getSessionVersion();
    const expectedAccountIdentity =
      editAccountIdentityRef.current ?? profile?.id ?? user?.id ?? null;
    setSaving(true);
    setNameError("");
    setError("");
    setSuccess("");
    try {
      const updated = await updateMe(
        { fullName: trimmed },
        { _expectedAccountIdentity: expectedAccountIdentity }
      );
      if (getSessionVersion() !== sessionAtStart) return;
      setProfile(updated);
      setFullName(updated.fullName);
      editAccountIdentityRef.current = updated.id ?? expectedAccountIdentity;
      setUser?.(updated, sessionAtStart);
      setEditing(false);
      setSuccess("Profile updated successfully.");
    } catch (err) {
      if (err?.sessionChanged) {
        setEditing(false);
        setError("Your active session changed. Reconciling account state...");
        await retryRestore?.();
        return;
      }
      if (getSessionVersion() === sessionAtStart) {
        setError(getErrorMessage(err));
      }
    } finally {
      if (getSessionVersion() === sessionAtStart) {
        setSaving(false);
      }
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
              disabled={saving}
              error={nameError}
              onChange={(e) => {
                const nextValue = e.target.value;
                setFullName(nextValue);
                if (nameError && nextValue.trim().length >= 2) {
                  setNameError("");
                }
              }}
              required
            />
            <div className="row-actions">
              <button type="submit" className="btn-primary" disabled={saving}>
                {saving ? "Saving..." : "Save"}
              </button>
              <button
                type="button"
                className="btn-ghost"
                disabled={saving}
                onClick={() => {
                  setEditing(false);
                  setNameError("");
                  setFullName(profile?.fullName ?? "");
                }}
              >
                Cancel
              </button>
            </div>
          </form>
        ) : (
          <div className="form-actions profile-actions">
            <button
              type="button"
              className="btn-ghost"
              onClick={() => {
                hasInteractedRef.current = true;
                editAccountIdentityRef.current =
                  profile?.id ?? user?.id ?? getActiveAccountIdentity();
                setNameError("");
                setEditing(true);
              }}
            >
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

