import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { zodResolver } from "@hookform/resolvers/zod";
import AuthLayout from "../components/AuthLayout";
import Field from "../components/Field";
import ErrorAlert from "../components/ErrorAlert";
import { useAuth } from "../auth/AuthContext";
import { getErrorMessage } from "../utils/errorMessage";

const schema = z.object({
  email: z.string().min(1, "Email is required").email("Enter a valid email address"),
  password: z.string().min(1, "Password is required").min(8, "Use at least 8 characters"),
});

export default function Login() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [error, setError] = useState("");
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm({ resolver: zodResolver(schema) });

  const onSubmit = async ({ email, password }) => {
    setError("");
    try {
      const result = await login(email, password);
      if (result?.interrupted || result?.ok === false || result?.status === "interrupted") {
        setError(result.message || "Sign-in was interrupted by sign-out. Please sign in again.");
        return;
      }
      navigate("/dashboard", { replace: true });
    } catch (e) {
      setError(getErrorMessage(e)); // the form is not reset, so the email stays
    }
  };

  return (
    <AuthLayout title="Welcome back" subtitle="Log in to see your tasks.">
      <ErrorAlert message={error} onClose={() => setError("")} />
      <form onSubmit={handleSubmit(onSubmit)} noValidate>
        <Field id="email" label="Email" type="email" autoComplete="email" placeholder="you@company.com" aria-required="true" error={errors.email?.message} {...register("email")} />
        <Field id="password" label="Password" type="password" autoComplete="current-password" placeholder="Your password" aria-required="true" error={errors.password?.message} {...register("password")} />
        <button className="btn" type="submit" disabled={isSubmitting}>
          {isSubmitting && <span className="spinner" aria-hidden="true" />}
          {isSubmitting ? "Logging in" : "Log in"}
        </button>
      </form>
      <p className="switch">Don't have an account? <Link to="/signup">Sign up</Link></p>
    </AuthLayout>
  );
}
