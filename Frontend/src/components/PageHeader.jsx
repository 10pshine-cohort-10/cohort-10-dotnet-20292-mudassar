/** Shared page heading so every section has the same title spacing and style. */
export default function PageHeader({ title, subtitle, actions }) {
  return (
    <div className="head">
      <div>
        <h1>{title}</h1>
        {subtitle && <p>{subtitle}</p>}
      </div>
      {actions}
    </div>
  );
}

