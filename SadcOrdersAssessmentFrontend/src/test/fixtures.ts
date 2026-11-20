import type { Customer, Order, PagedCustomers, PagedOrders, SadcCountry } from '../api/schemas';

export const countries: SadcCountry[] = [
  { code: 'ZA', name: 'South Africa', currencies: ['ZAR'], isCommonMonetaryArea: true },
  { code: 'BW', name: 'Botswana', currencies: ['BWP'], isCommonMonetaryArea: false },
  { code: 'ZW', name: 'Zimbabwe', currencies: ['ZWL', 'USD', 'ZWG'], isCommonMonetaryArea: false },
];

export const customer = (overrides: Partial<Customer> = {}): Customer => ({
  id: '3f2b8c1e-8d1a-4c39-9f0e-6f1d2a3b4c5d',
  name: 'Tendai Moyo',
  email: 'tendai@example.zw',
  countryCode: 'ZW',
  createdAt: '2026-11-01T08:00:00Z',
  ...overrides,
});

export const pagedCustomers = (items: Customer[], overrides: Partial<PagedCustomers> = {}): PagedCustomers => ({
  items,
  page: 1,
  pageSize: 10,
  totalCount: items.length,
  totalPages: items.length === 0 ? 0 : 1,
  ...overrides,
});

export const order = (overrides: Partial<Order> = {}): Order => ({
  id: '9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d',
  customerId: customer().id,
  customerName: 'Tendai Moyo',
  status: 'Pending',
  createdAt: '2026-11-10T09:30:00Z',
  updatedAt: null,
  currencyCode: 'USD',
  totalAmount: 105.97,
  lineItems: [
    { id: '1b2c3d4e-5f6a-4b7c-8d9e-0f1a2b3c4d5e', productSku: 'SOLAR-LAMP', quantity: 3, unitPrice: 24.99, lineTotal: 74.97 },
    { id: '2c3d4e5f-6a7b-4c8d-9e0f-1a2b3c4d5e6f', productSku: 'SEED-PACK', quantity: 10, unitPrice: 3.1, lineTotal: 31 },
  ],
  allowedTransitions: ['Paid', 'Cancelled'],
  ...overrides,
});

export const pagedOrders = (items: Order[], overrides: Partial<PagedOrders> = {}): PagedOrders => ({
  items: items.map((o) => ({
    id: o.id,
    customerId: o.customerId,
    customerName: o.customerName,
    status: o.status,
    createdAt: o.createdAt,
    currencyCode: o.currencyCode,
    totalAmount: o.totalAmount,
    lineItemCount: o.lineItems.length,
  })),
  page: 1,
  pageSize: 20,
  totalCount: items.length,
  totalPages: 1,
  ...overrides,
});

/** formatMoney output with whitespace normalised the way Testing Library normalises DOM text. */
export const moneyText = (amount: number, currency: string) =>
  new Intl.NumberFormat('en-ZA', { style: 'currency', currency, currencyDisplay: 'code' }).format(amount).replace(/\s+/g, ' ');
