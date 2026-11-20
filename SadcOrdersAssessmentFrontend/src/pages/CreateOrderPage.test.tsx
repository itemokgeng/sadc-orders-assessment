import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApiError } from '../api/client';
import { api } from '../api/endpoints';
import { countries, customer, moneyText, order, pagedCustomers } from '../test/fixtures';
import { renderWithProviders } from '../test/render';
import { CreateOrderPage } from './CreateOrderPage';

vi.mock('../api/endpoints', () => ({
  api: { listCustomers: vi.fn(), getCustomer: vi.fn(), listSadcCountries: vi.fn(), createOrder: vi.fn() },
}));

const zimbabwean = customer();
const motswana = customer({ id: 'bbbbbbbb-cccc-4ddd-8eee-ffffffffffff', name: 'Kagiso Banda', countryCode: 'BW' });

describe('CreateOrderPage', () => {
  beforeEach(() => {
    vi.mocked(api.listSadcCountries).mockResolvedValue(countries);
    vi.mocked(api.listCustomers).mockResolvedValue(pagedCustomers([zimbabwean, motswana]));
    vi.mocked(api.getCustomer).mockResolvedValue(zimbabwean);
  });

  afterEach(() => vi.clearAllMocks());

  it("offers only the currencies allowed for the customer's country", async () => {
    const user = userEvent.setup();
    renderWithProviders(<CreateOrderPage />, { route: '/orders/new' });

    const currency = screen.getByLabelText('Currency');
    expect(currency).toBeDisabled();

    await user.selectOptions(screen.getByLabelText('Customer'), await screen.findByRole('option', { name: 'Tendai Moyo (ZW)' }));
    await waitFor(() => expect(currency).toBeEnabled());
    expect(Array.from((currency as HTMLSelectElement).options).map((o) => o.value)).toEqual(['ZWL', 'USD', 'ZWG']);

    await user.selectOptions(screen.getByLabelText('Customer'), 'Kagiso Banda (BW)');
    await waitFor(() => expect(Array.from((currency as HTMLSelectElement).options).map((o) => o.value)).toEqual(['BWP']));
  });

  it('previews the total, then submits the order and navigates to it', async () => {
    const user = userEvent.setup();
    vi.mocked(api.createOrder).mockResolvedValue(order());
    renderWithProviders(<CreateOrderPage />, { route: `/orders/new?customerId=${zimbabwean.id}`, path: '/orders/new' });

    await waitFor(() => expect(screen.getByLabelText('Customer')).toHaveValue(zimbabwean.id));
    await user.selectOptions(screen.getByLabelText('Currency'), 'USD');

    const line1 = screen.getByRole('group', { name: 'Line 1' });
    await user.type(within(line1).getByLabelText('SKU'), 'SOLAR-LAMP');
    await user.clear(within(line1).getByLabelText('Quantity'));
    await user.type(within(line1).getByLabelText('Quantity'), '3');
    await user.type(within(line1).getByLabelText('Unit price'), '24.99');

    await user.click(screen.getByRole('button', { name: 'Add line' }));
    const line2 = screen.getByRole('group', { name: 'Line 2' });
    await user.type(within(line2).getByLabelText('SKU'), 'SEED-PACK');
    await user.clear(within(line2).getByLabelText('Quantity'));
    await user.type(within(line2).getByLabelText('Quantity'), '10');
    await user.type(within(line2).getByLabelText('Unit price'), '3.10');

    expect(screen.getByTestId('preview-total')).toHaveTextContent(moneyText(105.97, 'USD'));

    await user.click(screen.getByRole('button', { name: 'Create order' }));

    expect(await screen.findByText('Order details route')).toBeInTheDocument();
    expect(api.createOrder).toHaveBeenCalledWith({
      customerId: zimbabwean.id,
      currencyCode: 'USD',
      lineItems: [
        { productSku: 'SOLAR-LAMP', quantity: 3, unitPrice: 24.99 },
        { productSku: 'SEED-PACK', quantity: 10, unitPrice: 3.1 },
      ],
    });
  });

  it('maps server validation errors onto the line item fields', async () => {
    const user = userEvent.setup();
    vi.mocked(api.createOrder).mockRejectedValue(
      new ApiError(400, { status: 400, errors: { 'lineItems[0].quantity': ["'Quantity' must be greater than '0'."] } }),
    );
    renderWithProviders(<CreateOrderPage />, { route: `/orders/new?customerId=${zimbabwean.id}` });
    await waitFor(() => expect(screen.getByLabelText('Customer')).toHaveValue(zimbabwean.id));

    await user.type(screen.getByLabelText('SKU'), 'X');
    await user.click(screen.getByRole('button', { name: 'Create order' }));

    expect(await screen.findByText("'Quantity' must be greater than '0'.")).toBeInTheDocument();
    expect(screen.getByLabelText('Quantity')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
