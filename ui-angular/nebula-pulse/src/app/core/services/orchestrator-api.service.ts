import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { RiskDecision, RiskLimits, TradeIntentRequest } from '../models/risk.model';
import type { OrderCommandRequest } from '../models/order.model';
import { getOrchestratorBaseUrl } from './orchestrator-config';

export type OrderStatusResponse = Record<string, unknown>;

@Injectable({ providedIn: 'root' })
export class OrchestratorApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = this.resolveBaseUrl();

  health(): Observable<unknown> {
    return this.http.get(this.resolve('/health'));
  }

  getRiskLimits(): Observable<RiskLimits> {
    return this.http.get<RiskLimits>(this.resolve('/api/v1/limits'));
  }

  evaluateTradeIntent(intent: TradeIntentRequest): Observable<RiskDecision> {
    return this.http.post<RiskDecision>(this.resolve('/api/v1/trade/intents'), intent);
  }

  submitOrder(command: OrderCommandRequest): Observable<unknown> {
    return this.http.post(this.resolve('/api/v1/orders'), command, { observe: 'response' });
  }

  getOrderStatus(orderId: string): Observable<OrderStatusResponse> {
    return this.http.get<OrderStatusResponse>(this.resolve(`/api/v1/orders/${encodeURIComponent(orderId)}`));
  }

  private resolve(path: string): string {
    if (/^https?:\/\//i.test(path)) {
      return path;
    }

    if (!path.startsWith('/')) {
      path = `/${path}`;
    }

    return `${this.baseUrl}${path}`;
  }

  private resolveBaseUrl(): string {
    return getOrchestratorBaseUrl();
  }
}
