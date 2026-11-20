import { useMemo, useState, type FormEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router';
import { useSWRConfig } from 'swr';
import { ApiError } from '../api/client';
import { api } from '../api/endpoints';
import { useCustomer, useCustomers, useSadcCountries } from '../api/hooks';
import type { Customer } from '../api/schemas';
import { ErrorAlert } from '../components/ErrorAlert';
import { Field } from '../components/Field';
import { fieldError } from '../components/fieldError';
import { formatMoney } from '../format';
import { useDebouncedValue } from '../useDebouncedValue';

interface LineDraft {
  key: number;
  productSku: string;
  quantity: string;
  unitPrice: string;
}

let nextLineKey = 1;
const newLine = (): LineDraft => ({ key: nextLineKey++, productSku: '', quantity: '1', unitPrice: '' });

function CustomerPicker({ selected, onSelect }: { selected: Customer | null; onSelect: (c: Customer | null) => void }) {
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search.trim());
  const { data } = useCustomers({ search: debounced || undefined, page: 1, pageSize: 10 });
  const options = data?.items ?? [];
  const withSelected = selected && !options.some((c) => c.id === selected.id) ? [selected, ...options] : options;

  return (
    <fieldset>
      <legend>Customer</legend>
      <div className="field">
        <label htmlFor="customer-picker-search">Find customer</label>
        <input id="customer-picker-search" type="search" value={search} onChange={(e) => setSearch(e.target.value)} />
      </div>
      <div className="field">
        <label htmlFor="customer-picker">Customer</label>
        <select
          id="customer-picker"
          value={selected?.id ?? ''}
          onChange={(e) => onSelect(withSelected.find((c) => c.id === e.target.value) ?? null)}
        >
          <option value="">Select a customer…</option>
          {withSelected.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name} ({c.countryCode})
            </option>
          ))}
        </select>
      </div>
    </fieldset>
  );
}

export function CreateOrderPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const { mutate } = useSWRConfig();
  const { data: preselected } = useCustomer(params.get('customerId') ?? undefined);
  const { data: countries } = useSadcCountries();

  const [picked, setPicked] = useState<Customer | null | undefined>(undefined);
  const customer = picked === undefined ? (preselected ?? null) : picked;
  const [currencyChoice, setCurrencyChoice] = useState('');
  const [lines, setLines] = useState<LineDraft[]>(() => [newLine()]);
  const [serverError, setServerError] = useState<unknown>(null);
  const [submitting, setSubmitting] = useState(false);

  // Only offer currencies the API accepts for this customer's country (the same table the server
  // checks against). For example, a Zimbabwe customer can choose ZWL or USD.
  const allowedCurrencies = useMemo(
    () => countries?.find((c) => c.code === customer?.countryCode)?.currencies ?? [],
    [countries, customer],
  );
  const currency = allowedCurrencies.includes(currencyChoice) ? currencyChoice : (allowedCurrencies[0] ?? '');

  // Preview only. The server computes the real total.
  const previewTotal = lines.reduce((sum, l) => {
    const qty = Number(l.quantity);
    const price = Number(l.unitPrice);
    return Number.isFinite(qty) && Number.isFinite(price) ? sum + Math.round(qty * price * 100) : sum;
  }, 0) / 100;

  const hasFieldErrors = serverError instanceof ApiError && Object.keys(serverError.fieldErrors).length > 0;

  function updateLine(key: number, changes: Partial<LineDraft>) {
    setLines((current) => current.map((l) => (l.key === key ? { ...l, ...changes } : l)));
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (!customer) {
      setServerError(new Error('Select a customer first.'));
      return;
    }
    setSubmitting(true);
    setServerError(null);
    try {
      const order = await api.createOrder({
        customerId: customer.id,
        currencyCode: currency,
        lineItems: lines.map((l) => ({
          productSku: l.productSku.trim(),
          quantity: Number(l.quantity),
          unitPrice: Number(l.unitPrice),
        })),
      });
      void mutate((key) => Array.isArray(key) && key[0] === 'orders');
      navigate(`/orders/${order.id}`);
    } catch (error) {
      setServerError(error);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} noValidate className="card" aria-labelledby="new-order-heading">
      <h2 id="new-order-heading">New order</h2>
      <CustomerPicker selected={customer} onSelect={setPicked} />
      {fieldError(serverError, 'customerId') && <p className="field-error">{fieldError(serverError, 'customerId')}</p>}

      <Field label="Currency" error={fieldError(serverError, 'currencyCode')}>
        <select value={currency} onChange={(e) => setCurrencyChoice(e.target.value)} disabled={!customer}>
          {allowedCurrencies.length === 0 && <option value="">Select a customer first</option>}
          {allowedCurrencies.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
      </Field>

      <fieldset>
        <legend>Line items</legend>
        {fieldError(serverError, 'lineItems') && <p className="field-error">{fieldError(serverError, 'lineItems')}</p>}
        {lines.map((line, index) => (
          <div className="line-row" key={line.key} role="group" aria-label={`Line ${index + 1}`}>
            <Field label="SKU" error={fieldError(serverError, `lineItems[${index}].productSku`)}>
              <input value={line.productSku} onChange={(e) => updateLine(line.key, { productSku: e.target.value })} />
            </Field>
            <Field label="Quantity" error={fieldError(serverError, `lineItems[${index}].quantity`)}>
              <input type="number" min={1} step={1} value={line.quantity} onChange={(e) => updateLine(line.key, { quantity: e.target.value })} />
            </Field>
            <Field label="Unit price" error={fieldError(serverError, `lineItems[${index}].unitPrice`)}>
              <input type="number" min={0} step="0.01" value={line.unitPrice} onChange={(e) => updateLine(line.key, { unitPrice: e.target.value })} />
            </Field>
            <button
              type="button"
              className="link"
              onClick={() => setLines((current) => current.filter((l) => l.key !== line.key))}
              disabled={lines.length === 1}
              aria-label={`Remove line ${index + 1}`}
            >
              Remove
            </button>
          </div>
        ))}
        <button type="button" onClick={() => setLines((current) => [...current, newLine()])}>
          Add line
        </button>
      </fieldset>

      <p>
        Estimated total: <strong data-testid="preview-total">{currency ? formatMoney(previewTotal, currency) : '-'}</strong>
      </p>
      {/* Field errors appear next to their inputs; anything else (network, 403, 500) shows here. */}
      <ErrorAlert error={hasFieldErrors ? null : serverError} title="Could not create order" />
      <button type="submit" disabled={submitting || !customer} aria-busy={submitting}>
        {submitting ? 'Creating…' : 'Create order'}
      </button>
    </form>
  );
}
