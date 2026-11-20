import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApiError } from '../api/client';
import { api } from '../api/endpoints';
import { customer, moneyText, order, pagedOrders } from '../test/fixtures';
import { renderWithProviders } from '../test/render';
import { OrdersPage } from './OrdersPage';

vi.mock('../api/endpoints', () => ({ api: { listOrders: vi.fn(), getCustomer: vi.fn() } }));

describe('OrdersPage', () => {
  afterEach(() => vi.clearAllMocks());

  it('reads filters from the URL and shows the matching orders', async () => {
    vi.mocked(api.listOrders).mockResolvedValue(pagedOrders([order({ status: 'Paid' })]));
    vi.mocked(api.getCustomer).mockResolvedValue(customer());
    renderWithProviders(<OrdersPage />, { route: `/orders?customerId=${customer().id}&status=Paid&sort=-totalAmount` });

    expect(await screen.findByText(moneyText(105.97, 'USD'))).toBeInTheDocument();
    expect(api.listOrders).toHaveBeenCalledWith({ customerId: customer().id, status: 'Paid', sort: '-totalAmount', page: 1, pageSize: 20 });
    expect(await screen.findByText('Tendai Moyo', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.getByLabelText('Status')).toHaveValue('Paid');
  });

  it('filters by status and changes sort', async () => {
    const user = userEvent.setup();
    vi.mocked(api.listOrders).mockResolvedValue(pagedOrders([order()]));
    renderWithProviders(<OrdersPage />, { route: '/orders' });
    await screen.findByText('Tendai Moyo');

    await user.selectOptions(screen.getByLabelText('Status'), 'Cancelled');
    await waitFor(() => expect(api.listOrders).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'Cancelled', page: 1 })));

    await user.selectOptions(screen.getByLabelText('Sort'), 'Total: low to high');
    await waitFor(() => expect(api.listOrders).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'Cancelled', sort: 'totalAmount' })));
  });

  it('pages through results', async () => {
    const user = userEvent.setup();
    vi.mocked(api.listOrders).mockResolvedValue(pagedOrders([order()], { totalCount: 45, totalPages: 3 }));
    renderWithProviders(<OrdersPage />, { route: '/orders' });
    await screen.findByText('Tendai Moyo');

    await user.click(screen.getByRole('button', { name: 'Next' }));

    await waitFor(() => expect(api.listOrders).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 })));
  });

  it('shows API errors accessibly', async () => {
    vi.mocked(api.listOrders).mockRejectedValue(new ApiError(500, { title: 'Internal server error', detail: 'An unexpected error occurred.', traceId: 'abc' }));
    renderWithProviders(<OrdersPage />, { route: '/orders' });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not load orders');
    expect(alert).toHaveTextContent('Reference: abc');
  });
});
