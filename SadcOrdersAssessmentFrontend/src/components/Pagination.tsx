interface PaginationProps {
  page: number;
  totalPages: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  label: string;
}

export function Pagination({ page, totalPages, totalCount, onPageChange, label }: PaginationProps) {
  return (
    <nav className="pagination" aria-label={`${label} pagination`}>
      <button type="button" onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
        Previous
      </button>
      <span aria-live="polite">
        Page {totalPages === 0 ? 0 : page} of {totalPages} ({totalCount} total)
      </span>
      <button type="button" onClick={() => onPageChange(page + 1)} disabled={page >= totalPages}>
        Next
      </button>
    </nav>
  );
}
