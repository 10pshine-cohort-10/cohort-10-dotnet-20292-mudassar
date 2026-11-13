import { forwardRef, useState } from "react";

const Field = forwardRef(function Field({ label, id, error, type = "text", ...rest }, ref) {
  const [show, setShow] = useState(false);
  const isPassword = type === "password";
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <div className="input-wrap">
        <input
          id={id}
          ref={ref}
          type={isPassword && show ? "text" : type}
          className={isPassword ? "has-toggle" : undefined}
          aria-invalid={!!error}
          aria-describedby={error ? `${id}-error` : undefined}
          {...rest}
        />
        {isPassword && (
          <button type="button" className="toggle" onClick={() => setShow((s) => !s)}
            aria-label={show ? "Hide password" : "Show password"}>
            {show ? "Hide" : "Show"}
          </button>
        )}
      </div>
      {error && <p id={`${id}-error`} className="field-error">{error}</p>}
    </div>
  );
});
export default Field;
