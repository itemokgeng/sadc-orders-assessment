import type { OrderStatus } from '../api/schemas';

export function StatusBadge({ status }: { status: OrderStatus }) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{status}</span>;
}
