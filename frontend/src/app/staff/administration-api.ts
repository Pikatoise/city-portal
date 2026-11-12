import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map, Observable } from 'rxjs';
import { getCsrf } from '../api/fn/auth/get-csrf';
import { listAccounts } from '../api/fn/administration/list-accounts';
import { getAdminAccount } from '../api/fn/administration/get-admin-account';
import { changeRoles } from '../api/fn/administration/change-roles';
import { getRoleHistory } from '../api/fn/administration/get-role-history';
import { listEmailDeliveries } from '../api/fn/administration/list-email-deliveries';
import { replayEmail } from '../api/fn/administration/replay-email';
import { StrictHttpResponse } from '../api/strict-http-response';
import { AccountPage as GeneratedAccountPage } from '../api/models/account-page';
import { AccountSummary } from '../api/models/account-summary';
import { RoleHistoryPage as GeneratedRoleHistoryPage } from '../api/models/role-history-page';
import { EmailDeliveryPage as GeneratedEmailDeliveryPage } from '../api/models/email-delivery-page';
import { EmailDeliverySummary } from '../api/models/email-delivery-summary';
import { accountProblem, errorMessage } from '../account/account-api';

export type { AccountSummary, EmailDeliverySummary };
type NumericPage<T> = Omit<T, 'page' | 'pageSize' | 'totalCount'> & {
  page: number;
  pageSize: number;
  totalCount: number;
};
export type AccountPage = NumericPage<GeneratedAccountPage>;
export type RoleHistoryPage = NumericPage<GeneratedRoleHistoryPage>;
export type EmailDeliveryPage = NumericPage<GeneratedEmailDeliveryPage>;

function numericPage<
  T extends { page: string | number; pageSize: string | number; totalCount: string | number },
>(value: T): NumericPage<T> {
  return {
    ...value,
    page: Number(value.page),
    pageSize: Number(value.pageSize),
    totalCount: Number(value.totalCount),
  };
}

export const roleOptions = [
  { value: 'Author', label: 'Автор' },
  { value: 'Editor', label: 'Редактор' },
  { value: 'Moderator', label: 'Модератор' },
  { value: 'Administrator', label: 'Администратор' },
];

export function rolesText(roles: string[]): string {
  return roles
    .map((role) =>
      role === 'RegularUser'
        ? 'Пользователь'
        : (roleOptions.find((option) => option.value === role)?.label ?? role),
    )
    .join(', ');
}

export function administrationError(error: unknown): string {
  if (error instanceof HttpErrorResponse && error.status === 401)
    return 'Сеанс завершён. Войдите снова.';
  if (error instanceof HttpErrorResponse && error.status === 403)
    return 'Доступ разрешён только администратору с подтверждённым Email.';
  const code = accountProblem(error).code;
  if (code === 'last_administrator') return 'Нельзя снять роль у последнего администратора.';
  if (code === 'version_conflict')
    return 'Данные изменились. Проверьте актуальную версию и повторите действие.';
  if (code === 'invalid_link') return 'Ссылка больше не действует. Повторная отправка невозможна.';
  if (code === 'validation_failed') return 'Проверьте фильтры и параметры запроса.';
  if (error instanceof HttpErrorResponse && error.status === 404) return 'Запись не найдена.';
  return errorMessage(error);
}

@Injectable({ providedIn: 'root' })
export class AdministrationApi {
  private readonly http = inject(HttpClient);

  accounts(
    query: string,
    role: string,
    emailConfirmed: string,
    page: number,
  ): Promise<AccountPage> {
    return this.request(
      listAccounts(this.http, '', {
        query: query || undefined,
        role: role || undefined,
        emailConfirmed: emailConfirmed === '' ? undefined : emailConfirmed === 'true',
        page,
        pageSize: 20,
      }),
    ).then(numericPage);
  }

  account(id: string): Promise<AccountSummary> {
    return this.request(getAdminAccount(this.http, '', { id }));
  }

  history(id: string, page: number): Promise<RoleHistoryPage> {
    return this.request(getRoleHistory(this.http, '', { id, page, pageSize: 20 })).then(
      numericPage,
    );
  }

  roles(id: string, roles: string[], version: AccountSummary['version']): Promise<AccountSummary> {
    return this.write((token) =>
      this.request(
        changeRoles(this.http, '', { id, body: { roles, version }, 'X-CSRF-TOKEN': token }),
      ),
    );
  }

  deliveries(state: string, page: number): Promise<EmailDeliveryPage> {
    return this.request(
      listEmailDeliveries(this.http, '', { state: state || undefined, page, pageSize: 20 }),
    ).then(numericPage);
  }

  replay(delivery: EmailDeliverySummary): Promise<void> {
    return this.write((token) =>
      this.request(
        replayEmail(this.http, '', {
          id: delivery.id,
          body: { version: delivery.version },
          'X-CSRF-TOKEN': token,
        }),
      ),
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
