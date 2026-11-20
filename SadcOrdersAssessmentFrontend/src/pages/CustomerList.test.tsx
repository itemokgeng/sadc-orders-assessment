import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api/endpoints';
import { customer, pagedCustomers } from '../test/fixtures';
import { renderWithProviders } from '../test/render';
import { CustomerList } from './CustomerList';

vi.mock('../api/endpoints', () => ({ api: { listCustomers: vi.fn() } }));

describe('CustomerList', () => {
  afterEach(() => vi.clearAllMocks());

  it('shows a loading state, then the customers', async () => {
    vi.mocked(api.listCustomers).mockResolvedValue(pagedCustomers([customer(), customer({ id: 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee', name: 'Lerato Molefe', countryCode: 'ZA' })]));
    renderWithProviders(<CustomerList />);

    expect(screen.getByRole('status')).toHaveTextContent('Loading customers');
    expect(await screen.findByRole('cell', { name: 'Tendai Moyo' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Lerato Molefe' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Orders for Tendai Moyo' })).toHaveAttribute('href', `/orders?customerId=${customer().id}`);
    expect(api.listCustomers).toHaveBeenCalledWith({ search: undefined, page: 1, pageSize: 10 });
  });

  it('searches (debounced) and resets to page 1', async () => {
    const user = userEvent.setup();
    vi.mocked(api.listCustomers).mockResolvedValue(pagedCustomers([customer()], { totalCount: 25, totalPages: 3 }));
    renderWithProviders(<CustomerList />);
    await screen.findByRole('cell', { name: 'Tendai Moyo' });

    await user.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(api.listCustomers).toHaveBeenLastCalledWith({ search: undefined, page: 2, pageSize: 10 }));

    await user.type(screen.getByLabelText('Search by name or email'), 'moyo');
    await waitFor(() => expect(api.listCustomers).toHaveBeenLastCalledWith({ search: 'moyo', page: 1, pageSize: 10 }));
  });

  it('shows an empty state', async () => {
    vi.mocked(api.listCustomers).mockResolvedValue(pagedCustomers([]));
    renderWithProviders(<CustomerList />);

    expect(await screen.findByText('No customers found.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
  });
});
