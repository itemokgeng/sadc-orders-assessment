import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApiError } from '../api/client';
import { api } from '../api/endpoints';
import { countries, customer } from '../test/fixtures';
import { renderWithProviders } from '../test/render';
import { CreateCustomerForm } from './CreateCustomerForm';

vi.mock('../api/endpoints', () => ({
  api: { listSadcCountries: vi.fn(), createCustomer: vi.fn() },
}));

describe('CreateCustomerForm', () => {
  beforeEach(() => {
    vi.mocked(api.listSadcCountries).mockResolvedValue(countries);
  });

  afterEach(() => vi.clearAllMocks());

  it('validates required fields before calling the API', async () => {
    const user = userEvent.setup();
    renderWithProviders(<CreateCustomerForm onCreated={vi.fn()} />);

    await user.click(screen.getByRole('button', { name: 'Create customer' }));

    expect(screen.getByText('Name is required.')).toBeInTheDocument();
    expect(screen.getByText('Email is required.')).toBeInTheDocument();
    expect(screen.getByText('Choose a country.')).toBeInTheDocument();
    expect(screen.getByLabelText('Name')).toHaveAttribute('aria-invalid', 'true');
    expect(api.createCustomer).not.toHaveBeenCalled();
  });

  it('lists SADC countries and submits a valid customer', async () => {
    const user = userEvent.setup();
    const created = customer({ name: 'Naledi Dlamini', countryCode: 'BW' });
    vi.mocked(api.createCustomer).mockResolvedValue(created);
    const onCreated = vi.fn();
    renderWithProviders(<CreateCustomerForm onCreated={onCreated} />);

    await user.type(screen.getByLabelText('Name'), '  Naledi Dlamini ');
    await user.type(screen.getByLabelText('Email'), 'naledi@example.bw');
    await user.selectOptions(screen.getByLabelText('Country'), await screen.findByRole('option', { name: 'Botswana (BW)' }));
    await user.click(screen.getByRole('button', { name: 'Create customer' }));

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(created));
    expect(api.createCustomer).toHaveBeenCalledWith({ name: 'Naledi Dlamini', email: 'naledi@example.bw', countryCode: 'BW' });
    expect(screen.getByLabelText('Name')).toHaveValue('');
  });

  it('shows server-side field errors next to the field', async () => {
    const user = userEvent.setup();
    vi.mocked(api.createCustomer).mockRejectedValue(
      new ApiError(409, { status: 409, code: 'duplicate_email', errors: { email: ['A customer with this email already exists.'] } }),
    );
    renderWithProviders(<CreateCustomerForm onCreated={vi.fn()} />);

    await user.type(screen.getByLabelText('Name'), 'Dup');
    await user.type(screen.getByLabelText('Email'), 'dup@example.za');
    await user.selectOptions(screen.getByLabelText('Country'), await screen.findByRole('option', { name: 'South Africa (ZA)' }));
    await user.click(screen.getByRole('button', { name: 'Create customer' }));

    expect(await screen.findByText('A customer with this email already exists.')).toBeInTheDocument();
    expect(screen.getByLabelText('Email')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows a general alert for non-field errors', async () => {
    const user = userEvent.setup();
    vi.mocked(api.createCustomer).mockRejectedValue(new ApiError(403, { title: 'Forbidden', detail: 'You do not have access.' }));
    renderWithProviders(<CreateCustomerForm onCreated={vi.fn()} />);

    await user.type(screen.getByLabelText('Name'), 'Reader');
    await user.type(screen.getByLabelText('Email'), 'reader@example.za');
    await user.selectOptions(screen.getByLabelText('Country'), await screen.findByRole('option', { name: 'South Africa (ZA)' }));
    await user.click(screen.getByRole('button', { name: 'Create customer' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('You do not have access.');
  });
});
