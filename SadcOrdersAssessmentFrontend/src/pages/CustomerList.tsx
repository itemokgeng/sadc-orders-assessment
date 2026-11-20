import { useState } from 'react';
import { Link } from 'react-router';
import { useCustomers } from '../api/hooks';
import { ErrorAlert } from '../components/ErrorAlert';
import { Pagination } from '../components/Pagination';
import { formatDateTime } from '../format';
import { useDebouncedValue } from '../useDebouncedValue';

const PAGE_SIZE = 10;

export function CustomerList() {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const debouncedSearch = useDebouncedValue(search.trim());
  const { data, error, isLoading } = useCustomers({ search: debouncedSearch || undefined, page, pageSize: PAGE_SIZE });

  return (
    <section aria-labelledby="customers-heading" className="card">
      <h2 id="customers-heading">Customers</h2>
      <div className="field">
        <label htmlFor="customer-search">Search by name or email</label>
        <input
          id="customer-search"
          type="search"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
        />
      </div>

      <ErrorAlert error={error} title="Could not load customers" />
      {isLoading && <p role="status">Loading customers…</p>}

      {data && (
        <>
          <table>
            <caption className="visually-hidden">Customers</caption>
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Email</th>
                <th scope="col">Country</th>
                <th scope="col">Created</th>
                <th scope="col">
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {data.items.length === 0 && (
                <tr>
                  <td colSpan={5}>No customers found.</td>
                </tr>
              )}
              {data.items.map((c) => (
                <tr key={c.id}>
                  <td>{c.name}</td>
                  <td>{c.email}</td>
                  <td>{c.countryCode}</td>
                  <td>{formatDateTime(c.createdAt)}</td>
                  <td>
                    <Link to={`/orders?customerId=${c.id}`} aria-label={`Orders for ${c.name}`}>
                      Orders
                    </Link>{' '}
                    <Link to={`/orders/new?customerId=${c.id}`} aria-label={`New order for ${c.name}`}>
                      New order
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination label="Customers" page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onPageChange={setPage} />
        </>
      )}
    </section>
  );
}
