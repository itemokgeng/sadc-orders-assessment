import { useState, type FormEvent } from 'react';
import { api } from '../api/endpoints';
import { useSadcCountries } from '../api/hooks';
import type { Customer } from '../api/schemas';
import { ErrorAlert } from '../components/ErrorAlert';
import { Field } from '../components/Field';
import { fieldError } from '../components/fieldError';

type Errors = Partial<Record<'name' | 'email' | 'countryCode', string>>;

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function validate(name: string, email: string, countryCode: string): Errors {
  const errors: Errors = {};
  if (!name.trim()) errors.name = 'Name is required.';
  if (!email.trim()) errors.email = 'Email is required.';
  else if (!EMAIL_PATTERN.test(email.trim())) errors.email = 'Enter a valid email address.';
  if (!countryCode) errors.countryCode = 'Choose a country.';
  return errors;
}

export function CreateCustomerForm({ onCreated }: { onCreated: (customer: Customer) => void }) {
  const { data: countries } = useSadcCountries();
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [countryCode, setCountryCode] = useState('');
  const [clientErrors, setClientErrors] = useState<Errors>({});
  const [serverError, setServerError] = useState<unknown>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const errors = validate(name, email, countryCode);
    setClientErrors(errors);
    setServerError(null);
    if (Object.keys(errors).length > 0) return;

    setSubmitting(true);
    try {
      const customer = await api.createCustomer({ name: name.trim(), email: email.trim(), countryCode });
      setName('');
      setEmail('');
      setCountryCode('');
      onCreated(customer);
    } catch (error) {
      setServerError(error);
    } finally {
      setSubmitting(false);
    }
  }

  const errorFor = (field: keyof Errors) => clientErrors[field] ?? fieldError(serverError, field);
  const hasFieldErrors = (['name', 'email', 'countryCode'] as const).some((f) => fieldError(serverError, f));

  return (
    <form onSubmit={handleSubmit} noValidate aria-labelledby="create-customer-heading" className="card">
      <h2 id="create-customer-heading">New customer</h2>
      <Field label="Name" error={errorFor('name')}>
        <input value={name} onChange={(e) => setName(e.target.value)} maxLength={200} autoComplete="name" />
      </Field>
      <Field label="Email" error={errorFor('email')}>
        <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} maxLength={320} autoComplete="email" />
      </Field>
      <Field label="Country" error={errorFor('countryCode')}>
        <select value={countryCode} onChange={(e) => setCountryCode(e.target.value)}>
          <option value="">Select an SADC country…</option>
          {countries?.map((c) => (
            <option key={c.code} value={c.code}>
              {c.name} ({c.code})
            </option>
          ))}
        </select>
      </Field>
      {!hasFieldErrors && <ErrorAlert error={serverError} title="Could not create customer" />}
      <button type="submit" disabled={submitting} aria-busy={submitting}>
        {submitting ? 'Creating…' : 'Create customer'}
      </button>
    </form>
  );
}
