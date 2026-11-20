import { useRef, useState } from 'react';
import { Link, useParams } from 'react-router';
import { useSWRConfig } from 'swr';
import { ApiError } from '../api/client';
import { api } from '../api/endpoints';
import { useOrder } from '../api/hooks';
import type { OrderStatus } from '../api/schemas';
import { useAuth } from '../auth/useAuth';
import { ErrorAlert } from '../components/ErrorAlert';
import { StatusBadge } from '../components/StatusBadge';
import { formatDateTime, formatMoney } from '../format';

const ACTION_LABELS: Record<OrderStatus, string> = {
  Pending: 'Mark pending',
  Paid: 'Mark as paid',
  Fulfilled: 'Mark as fulfilled',
  Cancelled: 'Cancel order',
};

export function OrderDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const { data: order, error, isLoading, mutate: refresh } = useOrder(id);
  const { mutate } = useSWRConfig();
  const { can } = useAuth();
  const [pending, setPending] = useState<OrderStatus | null>(null);
  const [actionError, setActionError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // One idempotency key per intended change, kept until that change succeeds. If the request
  // fails transiently (timeout, 5xx, dropped connection) and the user clicks again, the same key
  // goes out, so the server applies the change at most once even if the first request got through.
  const idempotencyKeys = useRef(new Map<OrderStatus, string>());

  async function transition(target: OrderStatus) {
    if (!order) return;
    const key = idempotencyKeys.current.get(target) ?? crypto.randomUUID();
    idempotencyKeys.current.set(target, key);

    setPending(target);
    setActionError(null);
    setNotice(null);
    try {
      const { order: updated, replayed } = await api.updateOrderStatus(order.id, target, key);
      idempotencyKeys.current.delete(target);
      await refresh(updated, { revalidate: false });
      void mutate((k) => Array.isArray(k) && k[0] === 'orders');
      setNotice(replayed ? `Already ${updated.status}; the earlier request had gone through.` : `Order is now ${updated.status}.`);
    } catch (err) {
      if (err instanceof ApiError && !err.isTransient) {
        // Definite rejection (409/403/412/...): this attempt is over, so a later attempt gets a new key.
        idempotencyKeys.current.delete(target);
        if (err.status === 409) {
          void refresh();
        }
      }
      setActionError(err);
    } finally {
      setPending(null);
    }
  }

  if (isLoading) return <p role="status">Loading order…</p>;
  if (error) return <ErrorAlert error={error} title="Could not load order" />;
  if (!order) return null;

  const canWrite = can('Orders.Write') || can('Orders.Admin');

  return (
    <article className="card" aria-labelledby="order-heading">
      <p>
        <Link to="/orders">← Back to orders</Link>
      </p>
      <h2 id="order-heading">
        Order <code>{order.id.slice(0, 8)}</code> <StatusBadge status={order.status} />
      </h2>
      <dl className="details">
        <dt>Customer</dt>
        <dd>
          <Link to={`/orders?customerId=${order.customerId}`}>{order.customerName}</Link>
        </dd>
        <dt>Created</dt>
        <dd>{formatDateTime(order.createdAt)}</dd>
        {order.updatedAt && (
          <>
            <dt>Updated</dt>
            <dd>{formatDateTime(order.updatedAt)}</dd>
          </>
        )}
        <dt>Currency</dt>
        <dd>{order.currencyCode}</dd>
      </dl>

      <table>
        <caption>Line items</caption>
        <thead>
          <tr>
            <th scope="col">SKU</th>
            <th scope="col" className="numeric">
              Quantity
            </th>
            <th scope="col" className="numeric">
              Unit price
            </th>
            <th scope="col" className="numeric">
              Line total
            </th>
          </tr>
        </thead>
        <tbody>
          {order.lineItems.map((li) => (
            <tr key={li.id}>
              <td>{li.productSku}</td>
              <td className="numeric">{li.quantity}</td>
              <td className="numeric">{formatMoney(li.unitPrice, order.currencyCode)}</td>
              <td className="numeric">{formatMoney(li.lineTotal, order.currencyCode)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row" colSpan={3}>
              Total
            </th>
            <td className="numeric">
              <strong>{formatMoney(order.totalAmount, order.currencyCode)}</strong>
            </td>
          </tr>
        </tfoot>
      </table>

      <section aria-labelledby="actions-heading">
        <h3 id="actions-heading">Status</h3>
        {order.allowedTransitions.length === 0 ? (
          <p>This order is {order.status.toLowerCase()} and can't change any more.</p>
        ) : !canWrite ? (
          <p>Your role can view this order but not change it.</p>
        ) : (
          <div className="actions">
            {order.allowedTransitions.map((target) => (
              <button
                key={target}
                type="button"
                className={target === 'Cancelled' ? 'danger' : undefined}
                disabled={pending !== null}
                aria-busy={pending === target}
                onClick={() => void transition(target)}
              >
                {pending === target ? 'Saving…' : ACTION_LABELS[target]}
              </button>
            ))}
          </div>
        )}
        {notice && (
          <p role="status" className="success">
            {notice}
          </p>
        )}
        <ErrorAlert error={actionError} title="Status change failed" />
      </section>
    </article>
  );
}
