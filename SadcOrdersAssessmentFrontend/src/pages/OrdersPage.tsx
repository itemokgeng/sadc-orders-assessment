import { Link, useSearchParams } from 'react-router';
import { useCustomer, useOrders } from '../api/hooks';
import { ORDER_SORTS, ORDER_STATUSES, type OrderSort, type OrderStatus } from '../api/schemas';
import { ErrorAlert } from '../components/ErrorAlert';
import { Pagination } from '../components/Pagination';
import { StatusBadge } from '../components/StatusBadge';
import { formatDateTime, formatMoney } from '../format';

const PAGE_SIZE = 20;

const SORT_LABELS: Record<OrderSort, string> = {
  '-createdAt': 'Newest first',
  createdAt: 'Oldest first',
  '-totalAmount': 'Total: high to low',
  totalAmount: 'Total: low to high',
};

function parseStatus(value: string | null): OrderStatus | undefined {
  return ORDER_STATUSES.find((s) => s === value);
}

function parseSort(value: string | null): OrderSort {
  return ORDER_SORTS.find((s) => s === value) ?? '-createdAt';
}

/** Filters live in the URL, so a filtered view can be bookmarked or shared and Back works. */
export function OrdersPage() {
  const [params, setParams] = useSearchParams();
  const customerId = params.get('customerId') ?? undefined;
  const status = parseStatus(params.get('status'));
  const sort = parseSort(params.get('sort'));
  const page = Math.max(1, Number(params.get('page')) || 1);

  const { data, error, isLoading } = useOrders({ customerId, status, sort, page, pageSize: PAGE_SIZE });
  const { data: customer } = useCustomer(customerId);

  function update(changes: Record<string, string | undefined>) {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }
    if (!('page' in changes)) next.delete('page');
    setParams(next);
  }

  return (
    <section aria-labelledby="orders-heading" className="card">
      <div className="heading-row">
        <h2 id="orders-heading">Orders</h2>
        <Link className="button" to={customerId ? `/orders/new?customerId=${customerId}` : '/orders/new'}>
          New order
        </Link>
      </div>

      <div className="filters">
        {customerId && (
          <p>
            Customer: <strong>{customer?.name ?? customerId}</strong>{' '}
            <button type="button" className="link" onClick={() => update({ customerId: undefined })}>
              Clear
            </button>
          </p>
        )}
        <div className="field">
          <label htmlFor="status-filter">Status</label>
          <select id="status-filter" value={status ?? ''} onChange={(e) => update({ status: e.target.value || undefined })}>
            <option value="">All statuses</option>
            {ORDER_STATUSES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="sort">Sort</label>
          <select id="sort" value={sort} onChange={(e) => update({ sort: e.target.value })}>
            {ORDER_SORTS.map((s) => (
              <option key={s} value={s}>
                {SORT_LABELS[s]}
              </option>
            ))}
          </select>
        </div>
      </div>

      <ErrorAlert error={error} title="Could not load orders" />
      {isLoading && <p role="status">Loading orders…</p>}

      {data && (
        <>
          <table>
            <caption className="visually-hidden">Orders</caption>
            <thead>
              <tr>
                <th scope="col">Created</th>
                <th scope="col">Customer</th>
                <th scope="col">Status</th>
                <th scope="col">Lines</th>
                <th scope="col" className="numeric">
                  Total
                </th>
              </tr>
            </thead>
            <tbody>
              {data.items.length === 0 && (
                <tr>
                  <td colSpan={5}>No orders match these filters.</td>
                </tr>
              )}
              {data.items.map((o) => (
                <tr key={o.id}>
                  <td>
                    <Link to={`/orders/${o.id}`}>{formatDateTime(o.createdAt)}</Link>
                  </td>
                  <td>{o.customerName}</td>
                  <td>
                    <StatusBadge status={o.status} />
                  </td>
                  <td>{o.lineItemCount}</td>
                  <td className="numeric">{formatMoney(o.totalAmount, o.currencyCode)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination
            label="Orders"
            page={data.page}
            totalPages={data.totalPages}
            totalCount={data.totalCount}
            onPageChange={(p) => update({ page: String(p) })}
          />
        </>
      )}
    </section>
  );
}
