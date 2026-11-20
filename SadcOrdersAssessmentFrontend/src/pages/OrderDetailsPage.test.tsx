import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApiError } from '../api/client';
import { api } from '../api/endpoints';
import { moneyText, order } from '../test/fixtures';
import { renderWithProviders } from '../test/render';
import { OrderDetailsPage } from './OrderDetailsPage';

vi.mock('../api/endpoints', () => ({ api: { getOrder: vi.fn(), updateOrderStatus: vi.fn() } }));

const pending = order();
const route = { route: `/orders/${pending.id}/view`, path: '/orders/:id/view' };

describe('OrderDetailsPage', () => {
  beforeEach(() => {
    vi.mocked(api.getOrder).mockResolvedValue(pending);
  });

  afterEach(() => vi.clearAllMocks());

  it('shows line items, the total and the allowed transitions', async () => {
    renderWithProviders(<OrderDetailsPage />, route);

    expect(await screen.findByRole('cell', { name: 'SOLAR-LAMP' })).toBeInTheDocument();
    expect(screen.getByText(moneyText(74.97, 'USD'))).toBeInTheDocument();
    expect(screen.getByText(moneyText(105.97, 'USD'))).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Mark as paid' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Cancel order' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Mark as fulfilled' })).not.toBeInTheDocument();
  });

  it('sends an Idempotency-Key and reuses it when retrying after a transient failure', async () => {
    const user = userEvent.setup();
    vi.mocked(api.updateOrderStatus)
      .mockRejectedValueOnce(new ApiError(0, { title: 'Network error', detail: 'Could not reach the server.' }))
      .mockResolvedValueOnce({ order: order({ status: 'Paid', allowedTransitions: ['Fulfilled', 'Cancelled'] }), replayed: true });
    renderWithProviders(<OrderDetailsPage />, route);

    await user.click(await screen.findByRole('button', { name: 'Mark as paid' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not reach the server.');

    await user.click(screen.getByRole('button', { name: 'Mark as paid' }));
    expect(await screen.findByText(/earlier request had gone through/)).toBeInTheDocument();

    const [first, second] = vi.mocked(api.updateOrderStatus).mock.calls;
    expect(first![0]).toBe(pending.id);
    expect(first![1]).toBe('Paid');
    expect(first![2]).toMatch(/^[0-9a-f-]{36}$/);
    expect(second![2]).toBe(first![2]);
    expect(await screen.findByRole('button', { name: 'Mark as fulfilled' })).toBeInTheDocument();
  });

  it('uses a fresh key for a new change after a definite rejection', async () => {
    const user = userEvent.setup();
    vi.mocked(api.updateOrderStatus).mockRejectedValue(
      new ApiError(403, { title: 'Forbidden', detail: 'Cancelling a paid order requires the Orders.Admin role.' }),
    );
    renderWithProviders(<OrderDetailsPage />, route);

    await user.click(await screen.findByRole('button', { name: 'Cancel order' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Orders.Admin');
    await user.click(screen.getByRole('button', { name: 'Cancel order' }));
    await waitFor(() => expect(api.updateOrderStatus).toHaveBeenCalledTimes(2));

    const [first, second] = vi.mocked(api.updateOrderStatus).mock.calls;
    expect(second![2]).not.toBe(first![2]);
  });

  it('hides status actions from readers', async () => {
    renderWithProviders(<OrderDetailsPage />, { ...route, profile: 'Reader' });

    expect(await screen.findByText('Your role can view this order but not change it.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Mark as paid' })).not.toBeInTheDocument();
  });

  it('explains terminal states', async () => {
    vi.mocked(api.getOrder).mockResolvedValue(order({ status: 'Fulfilled', allowedTransitions: [] }));
    renderWithProviders(<OrderDetailsPage />, route);

    expect(await screen.findByText("This order is fulfilled and can't change any more.")).toBeInTheDocument();
  });
});
