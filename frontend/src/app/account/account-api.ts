import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, map, Observable } from 'rxjs';
import { StrictHttpResponse } from '../api/strict-http-response';
import { ApiWrite } from '../api-write';
import { getAccount } from '../api/fn/account/get-account';
import { resendConfirmation } from '../api/fn/account/resend-confirmation';
import { confirmEmail } from '../api/fn/auth/confirm-email';
import { login } from '../api/fn/auth/login';
import { logout } from '../api/fn/auth/logout';
import { register } from '../api/fn/auth/register';
import { forgotPassword } from '../api/fn/auth/forgot-password';
import { resetPassword } from '../api/fn/auth/reset-password';
import { changeDisplayName } from '../api/fn/account/change-display-name';
import { changePassword } from '../api/fn/account/change-password';
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
  private readonly write = inject(ApiWrite);

  register(body: {
    email: string;
    displayName: string;
    password: string;
  }): Promise<AccountProfile> {
    return this.write.invoke(register, { body });
  }

  login(body: { email: string; password: string; rememberMe: boolean }): Promise<AccountProfile> {
    return this.write.invoke(login, { body });
  }

  logout(): Promise<void> {
    return this.write.invoke(logout, {});
  }

  profile(): Promise<AccountProfile> {
    return this.request(getAccount(this.http, ''));
  }

  resend(): Promise<ConfirmationDelivery> {
    return this.write.invoke(resendConfirmation, {});
  }

  confirm(body: { linkId: string; token: string }): Promise<ConfirmEmailResponse> {
    return this.write.invoke(confirmEmail, { body });
  }

  forgotPassword(email: string): Promise<void> {
    return this.write.invoke(forgotPassword, { body: { email } });
  }

  resetPassword(body: { linkId: string; token: string; newPassword: string }): Promise<void> {
    return this.write.invoke(resetPassword, { body });
  }

  changeDisplayName(
    displayName: string,
    version: AccountProfile['version'],
  ): Promise<AccountProfile> {
    return this.write.invoke(changeDisplayName, { body: { displayName, version } });
  }

  changePassword(
    currentPassword: string,
    newPassword: string,
    version: AccountProfile['version'],
  ): Promise<void> {
    return this.write.invoke(changePassword, { body: { currentPassword, newPassword, version } });
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
  if (problem.code === 'account_blocked')
    return 'Контент доступен только для чтения. Причина блокировки указана в профиле.';
  if (problem.code === 'version_conflict')
    return 'Профиль изменился. Проверьте актуальные данные и повторите изменение.';
  if (problem.code === 'invalid_current_password') return 'Текущий пароль указан неверно.';
  if (problem.code === 'email_unconfirmed') return 'Подтвердите Email перед изменением профиля.';
  if (problem.code === 'expired_link') return 'Срок действия ссылки истёк. Запросите новое письмо.';
  if (problem.code === 'invalid_link')
    return 'Ссылка недействительна или уже использована. Запросите новое письмо.';
  if (problem.errors?.['newPassword'])
    return 'Пароль должен содержать 8–30 символов ASCII без пробелов и хотя бы один знак.';
  if (problem.errors?.['displayName'])
    return 'Используйте 3–20 букв или цифр без пробелов и других знаков.';
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
