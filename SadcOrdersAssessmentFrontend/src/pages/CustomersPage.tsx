import { useState } from 'react';
import { useSWRConfig } from 'swr';
import type { Customer } from '../api/schemas';
import { CreateCustomerForm } from './CreateCustomerForm';
import { CustomerList } from './CustomerList';

export function CustomersPage() {
  const { mutate } = useSWRConfig();
  const [created, setCreated] = useState<Customer | null>(null);

  return (
    <div className="two-column">
      <CreateCustomerForm
        onCreated={(customer) => {
          setCreated(customer);
          // Refresh every cached customer list page so the new customer shows up.
          void mutate((key) => Array.isArray(key) && key[0] === 'customers');
        }}
      />
      <div>
        {created && (
          <p role="status" className="success">
            Created {created.name} ({created.countryCode}).
          </p>
        )}
        <CustomerList />
      </div>
    </div>
  );
}
