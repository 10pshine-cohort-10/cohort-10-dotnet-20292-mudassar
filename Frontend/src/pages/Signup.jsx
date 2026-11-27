import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { zodResolver } from "@hookform/resolvers/zod";
import AuthLayout from "../components/AuthLayout";
import Field from "../components/Field";
import ErrorAlert from "../components/ErrorAlert";
import { registerRequest } from "../api/authApi";
import { getErrorMessage } from "../utils/errorMessage";

const schema = z
  .object({
    fullName: z.string().trim().min(2, "Use at least 2 characters").max(100, "Keep it under 100 characters"),
    email: z.string().min(1, "Email is required").email("Enter a valid email address"),
    password: z.string()
      .min(8, "Use at least 8 characters")
      .regex(/[A-Z]/, "Add an uppercase letter")
      .regex(/[a-z]/, "Add a lowercase letter")
      .regex(/[0-9]/, "Add a number")
      .regex(/[^A-Za-z0-9]/, "Add a special character"),
    confirmPassword: z.string().min(1, "Confirm your password"),
  })
  .refine((d) => d.password === d.confirmPassword, { path: ["confirmPassword"], message: "Passwords do not match" });

export default function Signup() {
  const navigate = useNavigate();
  const [error, setError] = useState("");
  const [successMessage, setSuccessMessage] = useState("");
  const success = Boolean(successMessage);
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm({ resolver: zodResolver(schema) });

  useEffect(() => {
    if (!success) return;
    const t = setTimeout(() => navigate("/login", { replace: true }), 1500);
    return () => clearTimeout(t);
  }, [success, navigate]);

  const onSubmit = async ({ fullName, email, password }) => {
    setError("");
    try {
      const res = await registerRequest({ fullName, email, password });
      setSuccessMessage(
        res?.message ||
          "If the provided details are valid, registration has been accepted. You can now sign in."
      );
    } catch (e) {
      setError(getErrorMessage(e));
    }
  };

  return (
    <AuthLayout title="Create your account" subtitle="Start organizing your work in a minute.">
      {success && <ErrorAlert type="success" message={successMessage} />}
      <ErrorAlert message={error} onClose={() => setError("")} />
      <form onSubmit={handleSubmit(onSubmit)} noValidate>
        <Field id="fullName" label="Full name" autoComplete="name" placeholder="Jane Doe" error={errors.fullName?.message} {...register("fullName")} />
        <Field id="email" label="Email" type="email" autoComplete="email" placeholder="you@company.com" error={errors.email?.message} {...register("email")} />
        <Field id="password" label="Password" type="password" autoComplete="new-password" placeholder="8+ characters" error={errors.password?.message} {...register("password")} />
        <Field id="confirmPassword" label="Confirm password" type="password" autoComplete="new-password" placeholder="Repeat your password" error={errors.confirmPassword?.message} {...register("confirmPassword")} />
        <button className="btn" type="submit" disabled={isSubmitting || success}>
          {isSubmitting && <span className="spinner" aria-hidden="true" />}
          {isSubmitting ? "Creating account" : "Create account"}
        </button>
      </form>
      <p className="switch">Already have an account? <Link to="/login">Log in</Link></p>
    </AuthLayout>
  );
}
