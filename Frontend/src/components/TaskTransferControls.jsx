import { useRef, useState } from "react";
import { exportTasks, importTasks } from "../api/tasksApi";
import { getErrorMessage } from "../utils/errorMessage";

function extractDownloadFileName(contentDisposition, fallbackName) {
  if (!contentDisposition) return fallbackName;
  const utf8Match = /filename\*=UTF-8''([^;\s]+)/i.exec(contentDisposition);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1]);
    } catch {
      return fallbackName;
    }
  }
  const plainMatch = /filename="?([^";\n]+)"?/i.exec(contentDisposition);
  return plainMatch?.[1]?.trim() || fallbackName;
}

function triggerBlobDownload(blob, fileName) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

async function resolveBlobErrorMessage(err) {
  const blob = err?.response?.data;
  if (blob instanceof Blob) {
    try {
      const text = await blob.text();
      const parsed = JSON.parse(text);
      return getErrorMessage({ ...err, response: { ...err.response, data: parsed } });
    } catch {
      return getErrorMessage(err);
    }
  }
  return getErrorMessage(err);
}

export function ImportResultAlert({ result, onClose }) {
  if (!result) return null;
  const hasFailures = result.failedCount > 0;
  const shownErrors = (result.errors ?? []).slice(0, 5);
  const remainingCount = Math.max(0, (result.errors?.length ?? 0) - shownErrors.length);

  return (
    <div className={`alert ${hasFailures ? "error" : "success"}`} role="status">
      <div>
        <strong>
          Imported {result.importedCount} {result.importedCount === 1 ? "task" : "tasks"}
          {hasFailures ? ` (${result.failedCount} failed)` : ""}.
        </strong>
        {shownErrors.length > 0 && (
          <ul className="import-error-list">
            {shownErrors.map((item) => (
              <li key={`${item.rowNumber}-${item.error}`}>
                Row {item.rowNumber}: {item.error}
              </li>
            ))}
            {remainingCount > 0 && <li>...and {remainingCount} more row error(s).</li>}
          </ul>
        )}
      </div>
      <div className="row-actions">
        <button type="button" className="btn-ghost btn-sm" onClick={onClose}>
          Dismiss
        </button>
      </div>
    </div>
  );
}

export default function TaskTransferControls({ filterParams, onImported, onError }) {
  const [exportingFormat, setExportingFormat] = useState("");
  const [importing, setImporting] = useState(false);
  const fileInputRef = useRef(null);

  const handleExport = async (format) => {
    if (exportingFormat || importing) return;
    setExportingFormat(format);
    onError?.("");
    try {
      const response = await exportTasks(format, filterParams);
      const fallbackName = `tasks-export.${format}`;
      const fileName = extractDownloadFileName(
        response.headers?.["content-disposition"],
        fallbackName
      );
      triggerBlobDownload(response.data, fileName);
    } catch (e) {
      onError?.(await resolveBlobErrorMessage(e));
    } finally {
      setExportingFormat("");
    }
  };

  const handleFileChange = async (event) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file || importing) return;

    setImporting(true);
    onError?.("");
    try {
      const result = await importTasks(file);
      onImported?.(result);
    } catch (e) {
      onError?.(getErrorMessage(e));
    } finally {
      setImporting(false);
    }
  };

  const busy = Boolean(exportingFormat) || importing;

  return (
    <>
      <button
        type="button"
        className="btn-ghost btn-sm"
        disabled={busy}
        onClick={() => void handleExport("json")}
      >
        {exportingFormat === "json" ? "Exporting..." : "Export JSON"}
      </button>
      <button
        type="button"
        className="btn-ghost btn-sm"
        disabled={busy}
        onClick={() => void handleExport("csv")}
      >
        {exportingFormat === "csv" ? "Exporting..." : "Export CSV"}
      </button>
      <button
        type="button"
        className="btn-ghost btn-sm"
        disabled={busy}
        onClick={() => fileInputRef.current?.click()}
      >
        {importing ? "Importing..." : "Import"}
      </button>
      <input
        ref={fileInputRef}
        type="file"
        accept=".json,.csv,application/json,text/csv"
        aria-label="Import tasks from JSON or CSV file"
        style={{ display: "none" }}
        onChange={(e) => void handleFileChange(e)}
      />
    </>
  );
}
