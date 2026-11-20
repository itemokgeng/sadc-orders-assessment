import { z } from 'zod';
import { apiRequest, toQueryString } from './client';
import {
  CustomerSchema,
  DevTokenSchema,
  OrderSchema,
  PagedCustomersSchema,
  PagedOrdersSchema,
  SadcCountrySchema,
  type CreateCustomerRequest,
  type CreateOrderRequest,
  type Customer,
  type DevToken,
  type Order,
  type OrderSort,
  type OrderStatus,
  type PagedCustomers,
  type PagedOrders,
  type SadcCountry,
} from './schemas';

export interface CustomerListParams {
  search?: string;
  page: number;
  pageSize: number;
}

export interface OrderListParams {
  customerId?: string;
  status?: OrderStatus;
  sort: OrderSort;
  page: number;
  pageSize: number;
}

export const api = {
  async listCustomers(params: CustomerListParams): Promise<PagedCustomers> {
    return (await apiRequest(`/api/customers${toQueryString({ ...params })}`, { schema: PagedCustomersSchema })).data;
  },

  async getCustomer(id: string): Promise<Customer> {
    return (await apiRequest(`/api/customers/${encodeURIComponent(id)}`, { schema: CustomerSchema })).data;
  },

  async createCustomer(body: CreateCustomerRequest): Promise<Customer> {
    return (await apiRequest('/api/customers', { method: 'POST', body, schema: CustomerSchema })).data;
  },

  async listOrders(params: OrderListParams): Promise<PagedOrders> {
    return (await apiRequest(`/api/orders${toQueryString({ ...params })}`, { schema: PagedOrdersSchema })).data;
  },

  async getOrder(id: string): Promise<Order> {
    return (await apiRequest(`/api/orders/${encodeURIComponent(id)}`, { schema: OrderSchema })).data;
  },

  async createOrder(body: CreateOrderRequest): Promise<Order> {
    return (await apiRequest('/api/orders', { method: 'POST', body, schema: OrderSchema })).data;
  },

  /**
   * Idempotent status change. The caller supplies the key and must reuse it when retrying the same
   * change, so a retry after a timeout can never apply the change twice.
   */
  async updateOrderStatus(id: string, status: OrderStatus, idempotencyKey: string): Promise<{ order: Order; replayed: boolean }> {
    const { data, headers } = await apiRequest(`/api/orders/${encodeURIComponent(id)}/status`, {
      method: 'PUT',
      body: { status },
      headers: { 'Idempotency-Key': idempotencyKey },
      schema: OrderSchema,
    });
    return { order: data, replayed: headers.get('Idempotent-Replayed') === 'true' };
  },

  async listSadcCountries(): Promise<SadcCountry[]> {
    return (await apiRequest('/api/reference/sadc-countries', { schema: z.array(SadcCountrySchema), anonymous: true })).data;
  },

  async issueDevToken(roles: string[]): Promise<DevToken> {
    return (await apiRequest('/api/dev/token', { method: 'POST', body: { roles }, schema: DevTokenSchema, anonymous: true })).data;
  },
};

export type Api = typeof api;
