import { z } from 'zod';

/*
 * Runtime-checked API contract. These names mirror src/Core/DTOs/Dtos.cs exactly
 * (camelCase JSON). Every response is parsed with these schemas, so if the backend contract
 * drifts the UI fails loudly at the boundary instead of rendering `undefined`.
 */

export const ORDER_STATUSES = ['Pending', 'Paid', 'Fulfilled', 'Cancelled'] as const;
export const OrderStatusSchema = z.enum(ORDER_STATUSES);
export type OrderStatus = z.infer<typeof OrderStatusSchema>;

export const ORDER_SORTS = ['-createdAt', 'createdAt', '-totalAmount', 'totalAmount'] as const;
export type OrderSort = (typeof ORDER_SORTS)[number];

export const MAX_PAGE_SIZE = 100;

export const CustomerSchema = z.object({
  id: z.guid(),
  name: z.string(),
  email: z.string(),
  countryCode: z.string().length(2),
  createdAt: z.string(),
});
export type Customer = z.infer<typeof CustomerSchema>;

export const OrderLineItemSchema = z.object({
  id: z.guid(),
  productSku: z.string(),
  quantity: z.number().int(),
  unitPrice: z.number(),
  lineTotal: z.number(),
});
export type OrderLineItem = z.infer<typeof OrderLineItemSchema>;

export const OrderSchema = z.object({
  id: z.guid(),
  customerId: z.guid(),
  customerName: z.string(),
  status: OrderStatusSchema,
  createdAt: z.string(),
  updatedAt: z.string().nullable(),
  currencyCode: z.string().length(3),
  totalAmount: z.number(),
  lineItems: z.array(OrderLineItemSchema),
  allowedTransitions: z.array(OrderStatusSchema),
});
export type Order = z.infer<typeof OrderSchema>;

export const OrderSummarySchema = z.object({
  id: z.guid(),
  customerId: z.guid(),
  customerName: z.string(),
  status: OrderStatusSchema,
  createdAt: z.string(),
  currencyCode: z.string().length(3),
  totalAmount: z.number(),
  lineItemCount: z.number().int(),
});
export type OrderSummary = z.infer<typeof OrderSummarySchema>;

export const pagedSchema = <T extends z.ZodType>(item: T) =>
  z.object({
    items: z.array(item),
    page: z.number().int(),
    pageSize: z.number().int(),
    totalCount: z.number().int(),
    totalPages: z.number().int(),
  });

export const PagedCustomersSchema = pagedSchema(CustomerSchema);
export type PagedCustomers = z.infer<typeof PagedCustomersSchema>;

export const PagedOrdersSchema = pagedSchema(OrderSummarySchema);
export type PagedOrders = z.infer<typeof PagedOrdersSchema>;

export const SadcCountrySchema = z.object({
  code: z.string().length(2),
  name: z.string(),
  currencies: z.array(z.string().length(3)),
  isCommonMonetaryArea: z.boolean(),
});
export type SadcCountry = z.infer<typeof SadcCountrySchema>;

export const DevTokenSchema = z.object({
  accessToken: z.string(),
  expiresAt: z.string(),
  roles: z.array(z.string()),
});
export type DevToken = z.infer<typeof DevTokenSchema>;

/** RFC 7807 Problem Details as returned by the API (see GlobalExceptionHandler). */
export const ProblemDetailsSchema = z
  .object({
    type: z.string().optional(),
    title: z.string().optional(),
    status: z.number().optional(),
    detail: z.string().optional(),
    code: z.string().optional(),
    traceId: z.string().optional(),
    correlationId: z.string().optional(),
    errors: z.record(z.string(), z.array(z.string())).optional(),
    allowedTransitions: z.array(z.string()).optional(),
  })
  .loose();
export type ProblemDetails = z.infer<typeof ProblemDetailsSchema>;

// Request bodies (TypeScript types are enough: the server validates them).
export interface CreateCustomerRequest {
  name: string;
  email: string;
  countryCode: string;
}

export interface CreateOrderLineRequest {
  productSku: string;
  quantity: number;
  unitPrice: number;
}

export interface CreateOrderRequest {
  customerId: string;
  currencyCode: string;
  lineItems: CreateOrderLineRequest[];
}

export interface UpdateOrderStatusRequest {
  status: OrderStatus;
}
