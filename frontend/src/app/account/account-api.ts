import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, map, Observable } from 'rxjs';
import { StrictHttpResponse } from '../api/strict-http-response';
import { getAccount } from '../api/fn/account/get-account';
import { resendConfirmation } from '../api/fn/account/resend-confirmation';
import { confirmEmail } from '../api/fn/auth/confirm-email';
import { getCsrf } from '../api/fn/auth/get-csrf';
import { login } from '../api/fn/auth/login';
import { logout } from '../api/fn/auth/logout';
import { register } from '../api/fn/auth/register';
import { AccountResponse } from '../api/models/account-response';
import { ConfirmationDeliveryResponse } from '../api/models/confirmation-delivery-response';
import { ConfirmEmailResponse } from '../api/models/confirm-email-response';

export type ConfirmationDelivery = ConfirmationDeliveryResponse;
export type AccountProfile = AccountResponse;

export interface AccountProblem {
  code?: string;
  errors?: Record<string, string[]>;
  retryAfterSeconds?: number;
}

@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly http = inject(HttpClient);

  register(body: {
    email: string;
    displayName: string;
    password: string;
  }): Promise<AccountProfile> {
    return this.write((token) =>
      this.request(register(this.http, '', { body, 'X-CSRF-TOKEN': token })),
    );
  }

  login(body: { email: string; password: string; rememberMe: boolean }): Promise<AccountProfile> {
    return this.write((token) =>
      this.request(login(this.http, '', { body, 'X-CSRF-TOKEN': token })),
    );
  }

  logout(): Promise<void> {
    return this.write((token) => this.request(logout(this.http, '', { 'X-CSRF-TOKEN': token })));
  }

  profile(): Promise<AccountProfile> {
    return this.request(getAccount(this.http, ''));
  }

  resend(): Promise<ConfirmationDelivery> {
    return this.write((token) =>
      this.request(resendConfirmation(this.http, '', { 'X-CSRF-TOKEN': token })),
    );
  }

  confirm(body: { linkId: string; token: string }): Promise<ConfirmEmailResponse> {
    return this.write((token) =>
      this.request(confirmEmail(this.http, '', { body, 'X-CSRF-TOKEN': token })),
    );
  }

  private async write<T>(operation: (token: string) => Promise<T>): Promise<T> {
    const csrf = await this.request(getCsrf(this.http, ''));
    return operation(csrf.token);
  }

  private request<T>(response: Observable<StrictHttpResponse<T>>): Promise<T> {
    return firstValueFrom(response.pipe(map((value) => value.body)));
  }
}

export function accountProblem(error: unknown): AccountProblem {
  return error instanceof HttpErrorResponse &&
    typeof error.error === 'object' &&
    error.error !== null
    ? (error.error as AccountProblem)
    : {};
}

export function errorMessage(error: unknown): string {
  const problem = accountProblem(error);
  if (error instanceof HttpErrorResponse && error.status === 429) {
    const seconds = problem.retryAfterSeconds ?? Number(error.headers.get('Retry-After'));
    return Number.isFinite(seconds) && seconds > 0
      ? `Слишком много запросов. Повторите через ${Math.ceil(seconds)} сек.`
      : 'Слишком много запросов. Повторите позже.';
  }
  if (error instanceof HttpErrorResponse && error.status === 401)
    return 'Неверный Email или пароль.';
  return 'Не удалось выполнить запрос. Повторите попытку.';
}
