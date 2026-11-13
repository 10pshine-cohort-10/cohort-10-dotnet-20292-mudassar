import { createContext, useContext, useEffect, useMemo, useState } from "react";
import { getMe, loginRequest, logoutRequest } from "../api/authApi";

const AuthContext = createContext(null);
export const useAuth = () => useContext(AuthContext);

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!localStorage.getItem("token")) {
      setLoading(false);
      return;
    }
    getMe()
      .then(setUser)
      .catch(() => localStorage.removeItem("token"))
      .finally(() => setLoading(false));
  }, []);

  const login = async (email, password) => {
    const data = await loginRequest(email, password);
    localStorage.setItem("token", data.token);
    setUser(data.user);
  };

  const logout = async () => {
    try {
      await logoutRequest();
    } catch {
      /* the token is discarded either way */
    } finally {
      localStorage.removeItem("token");
      setUser(null);
    }
  };

  const value = useMemo(
    () => ({ user, loading, login, logout, isAdmin: !!user?.roles?.includes("Admin") }),
    [user, loading]
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
