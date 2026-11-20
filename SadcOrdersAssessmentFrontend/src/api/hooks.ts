import useSWR from 'swr';
import { api, type CustomerListParams, type OrderListParams } from './endpoints';

// SWR gives request deduping, caching per key, revalidation on focus and keepPreviousData (no
// table flicker while paging). Keys include every parameter, so each page/filter is cached on its own.

export function useCustomers(params: CustomerListParams) {
  return useSWR(['customers', params], () => api.listCustomers(params), { keepPreviousData: true });
}

export function useOrders(params: OrderListParams) {
  return useSWR(['orders', params], () => api.listOrders(params), { keepPreviousData: true });
}

export function useOrder(id: string | undefined) {
  return useSWR(id ? ['order', id] : null, () => api.getOrder(id!));
}

export function useCustomer(id: string | undefined) {
  return useSWR(id ? ['customer', id] : null, () => api.getCustomer(id!));
}

export function useSadcCountries() {
  // Reference data changes only on deploy, so fetch it once per session.
  return useSWR('sadc-countries', () => api.listSadcCountries(), {
    revalidateOnFocus: false,
    revalidateIfStale: false,
    dedupingInterval: 60 * 60 * 1000,
  });
}
