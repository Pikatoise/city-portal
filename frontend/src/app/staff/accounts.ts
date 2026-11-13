import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CityClock } from './city-clock';
import { AccountApi, accountProblem } from '../account/account-api';
import {
  AccountPage,
  AccountSummary,
  AdministrationApi,
  RoleHistoryPage,
  administrationError,
  roleOptions,
  rolesText,
} from './administration-api';

@Component({
  selector: 'app-admin-accounts',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="staff-panel" aria-labelledby="accounts-title">
      <p class="eyebrow">Администрирование</p>
      <h1 id="accounts-title">Аккаунты и роли</h1>
      <p>Часовой пояс города: {{ clock.timeZone() }}</p>
      <nav class="staff-nav">
        <a routerLink="/staff/accounts">Аккаунты</a
        ><a routerLink="/staff/email-deliveries">Доставка Email</a
        ><a routerLink="/account">Мой профиль</a>
      </nav>
      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }
      @if (notice()) {
        <p role="status">{{ notice() }}</p>
      }
      @if (allowed()) {
        <form class="staff-filters" (ngSubmit)="load(1)">
          <label for="account-search">Email или имя</label
          ><input id="account-search" name="query" maxlength="254" [(ngModel)]="query" />
          <label for="role-filter">Роль</label
          ><select id="role-filter" name="role" [(ngModel)]="role">
            <option value="">Все роли</option>
            <option value="RegularUser">Пользователь</option>
            @for (option of roleOptions; track option.value) {
              <option [value]="option.value">{{ option.label }}</option>
            }
          </select>
          <label for="confirmed-filter">Email</label
          ><select id="confirmed-filter" name="emailConfirmed" [(ngModel)]="emailConfirmed">
            <option value="">Все аккаунты</option>
            <option value="true">Подтверждён</option>
            <option value="false">Не подтверждён</option>
          </select>
          <button class="secondary-button" type="submit" [disabled]="busy()">Найти</button>
        </form>
        @if (accounts(); as result) {
          <p role="status">Найдено: {{ result.totalCount }}</p>
          <div class="staff-table">
            <table>
              <thead>
                <tr>
                  <th>Email</th>
                  <th>Имя</th>
                  <th>Роли</th>
                  <th>Действия</th>
                </tr>
              </thead>
              <tbody>
                @for (item of result.items; track item.id) {
                  <tr>
                    <td>
                      {{ item.email }}<br />{{
                        item.emailConfirmed ? 'Подтверждён' : 'Не подтверждён'
                      }}
                    </td>
                    <td>{{ item.displayName }}</td>
                    <td>{{ rolesText(item.roles) }}</td>
                    <td>
                      <button
                        type="button"
                        class="text-button"
                        (click)="open(item.id)"
                        [disabled]="busy()"
                      >
                        Открыть
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="staff-pagination">
            <button
              type="button"
              (click)="load(result.page - 1)"
              [disabled]="busy() || result.page <= 1"
            >
              Назад</button
            ><span>Страница {{ result.page }}</span
            ><button
              type="button"
              (click)="load(result.page + 1)"
              [disabled]="busy() || result.page * result.pageSize >= result.totalCount"
            >
              Далее
            </button>
          </div>
        }
        @if (selected(); as account) {
          <section class="confirmation-card" aria-labelledby="account-details-title">
            <h2 id="account-details-title">{{ account.displayName }}</h2>
            <p>{{ account.email }}</p>
            <p>{{ account.emailConfirmed ? 'Email подтверждён' : 'Email не подтверждён' }}</p>
            <form (ngSubmit)="save()">
              <p>Роль «Пользователь» сохраняется всегда.</p>
              @for (option of roleOptions; track option.value) {
                <label class="checkbox-label"
                  ><input
                    type="checkbox"
                    [name]="option.value"
                    [ngModel]="chosen.includes(option.value)"
                    (ngModelChange)="toggle(option.value, $event)"
                  />{{ option.label }}</label
                >
              }
              <button class="primary-button" type="submit" [disabled]="busy()">
                Сохранить роли
              </button>
            </form>
            <h3>История ролей</h3>
            @if (history(); as changes) {
              @for (change of changes.items; track change.id) {
                <p>
                  <time [attr.datetime]="change.createdAtUtc">{{
                    clock.format(change.createdAtUtc)
                  }}</time><br />Кто изменил:
                  {{ change.actorKind === 'Bootstrap' ? 'Первичная настройка' : change.actorId
                  }}<br />{{ rolesText(change.oldRoles) || 'Нет ролей' }} →
                  {{ rolesText(change.newRoles) }}
                </p>
              }
              @if (!changes.items.length) {
                <p>Изменений ещё нет.</p>
              }
              <div class="staff-pagination">
                <button
                  type="button"
                  (click)="loadHistory(changes.page - 1)"
                  [disabled]="busy() || changes.page <= 1"
                >
                  Назад</button
                ><span>История: {{ changes.page }}</span
                ><button
                  type="button"
                  (click)="loadHistory(changes.page + 1)"
                  [disabled]="busy() || changes.page * changes.pageSize >= changes.totalCount"
                >
                  Далее
                </button>
              </div>
            }
          </section>
        }
      } @else {
        <p><a routerLink="/account/login">Войти</a></p>
      }
      @if (busy()) {
        <p role="status">Загружаем…</p>
      }
    </section>
  `,
})
export class Accounts implements OnInit {
  protected readonly clock = inject(CityClock);
  private readonly api = inject(AdministrationApi);
  private readonly ownAccount = inject(AccountApi);
  private viewerId = '';
  protected readonly roleOptions = roleOptions;
  protected readonly rolesText = rolesText;
  protected query = '';
  protected role = '';
  protected emailConfirmed = '';
  protected chosen: string[] = [];
  protected readonly accounts = signal<AccountPage | null>(null);
  protected readonly selected = signal<AccountSummary | null>(null);
  protected readonly history = signal<RoleHistoryPage | null>(null);
  protected readonly allowed = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');

  ngOnInit(): void {
    void this.initialize();
  }

  private async initialize(): Promise<void> {
    try {
      const profile = await this.ownAccount.profile();
      this.viewerId = profile.id;
      if (!profile.emailConfirmed || !profile.roles.includes('Administrator')) {
        this.error.set('Доступ разрешён только администратору с подтверждённым Email.');
        return;
      }
      this.allowed.set(true);
      await this.load(1);
    } catch (error) {
      this.error.set(administrationError(error));
    }
  }

  protected async load(page: number): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      this.accounts.set(await this.api.accounts(this.query, this.role, this.emailConfirmed, page));
    } catch (error) {
      this.error.set(administrationError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async open(id: string): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await this.details(id);
    } catch (error) {
      this.error.set(administrationError(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async details(id: string): Promise<void> {
    const [account, history] = await Promise.all([this.api.account(id), this.api.history(id, 1)]);
    this.selected.set(account);
    this.chosen = [...account.roles];
    this.history.set(history);
  }

  protected toggle(role: string, checked: boolean): void {
    this.chosen = checked ? [...this.chosen, role] : this.chosen.filter((item) => item !== role);
  }

  protected async save(): Promise<void> {
    const account = this.selected();
    if (!account || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      const updated = await this.api.roles(account.id, this.chosen, account.version);
      if (updated.id === this.viewerId && !updated.roles.includes('Administrator')) {
        this.allowed.set(false);
        this.selected.set(null);
        this.accounts.set(null);
        this.notice.set('Права администратора сняты. Личный кабинет доступен.');
        return;
      }
      await this.details(account.id);
      this.accounts.set(
        await this.api.accounts(
          this.query,
          this.role,
          this.emailConfirmed,
          this.accounts()?.page ?? 1,
        ),
      );
      this.notice.set('Роли сохранены.');
    } catch (error) {
      if (accountProblem(error).code === 'version_conflict')
        await this.details(account.id).catch(() => this.selected.set(null));
      this.error.set(administrationError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async loadHistory(page: number): Promise<void> {
    const account = this.selected();
    if (!account || this.busy()) return;
    this.busy.set(true);
    try {
      this.history.set(await this.api.history(account.id, page));
    } catch (error) {
      this.error.set(administrationError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
