const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/**
 * Formats a date-only or ISO date string directly from its YYYY-MM-DD calendar parts
 * so due dates always display the exact selected calendar day in every timezone.
 */
export function formatDueDate(value, emptyLabel = "No due date") {
  if (!value) return emptyLabel;
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(value));
  if (match) {
    const year = Number(match[1]);
    const monthIndex = Number(match[2]) - 1;
    const day = Number(match[3]);
    if (monthIndex >= 0 && monthIndex < 12) {
      return `${MONTHS[monthIndex]} ${day}, ${year}`;
    }
  }
  return String(value);
}

