import PageHeader from "../components/PageHeader";

/** Placeholder for sections that aren't built yet; uses the same page structure as real pages. */
export default function ComingSoon({ title, subtitle }) {
  return (
    <>
      <PageHeader title={title} subtitle={subtitle} />
      <div className="empty">
        <h2>Coming soon</h2>
        <p>This section is under construction.</p>
      </div>
    </>
  );
}

