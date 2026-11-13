import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CityClock } from './city-clock';
import {
  AdministrationApi,
  EmailDeliveryPage,
  EmailDeliverySummary,
  administrationError,
} from './administration-api';

const states: Record<string, string> = {
  Queued: 'В очереди',
  Sending: 'Отправляется',
  RetryScheduled: 'Ожидает повтора',
  Sent: 'Отправлено',
  Failed: 'Ошибка',
  Skipped: 'Пропущено',
};
const errors: Record<string, string> = {
  smtp_permanent_rejection: 'Почтовый сервер отклонил письмо',
  smtp_temporary_rejection: 'Временный отказ почтового сервера',
  smtp_unavailable: 'Почтовый сервер недоступен',
  protected_payload_invalid: 'Не удалось прочитать данные письма',
  smtp_authentication_failed: 'Ошибка входа на почтовый сервер',
  smtp_tls_failed: 'Ошибка защищённого соединения',
};

@Component({
  selector: 'app-email-deliveries',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="staff-panel" aria-labelledby="deliveries-title">
      <p class="eyebrow">Администрирование</p>
      <h1 id="deliveries-title">Доставка Email</h1>
      <p>Часовой пояс города: {{ clock.timeZone() }}</p>
      <nav class="staff-nav">
        <a routerLink="/staff/accounts">Аккаунты</a
        ><a routerLink="/staff/email-deliveries">Доставка Email</a
        ><a routerLink="/account">Мой профиль</a>
      </nav>
      <form class="staff-filters" (ngSubmit)="load(1)">
        <label for="delivery-state">Состояние</label
        ><select id="delivery-state" name="state" [(ngModel)]="state">
          <option value="">Все состояния</option>
          @for (item of stateOptions; track item) {
            <option [value]="item">{{ stateLabel(item) }}</option>
          }</select
        ><button type="submit" class="secondary-button" [disabled]="busy()">Обновить</button>
      </form>
      @if (error()) {
        <p role="alert" class="form-error">{{ error() }}</p>
      }
      @if (notice()) {
        <p role="status">{{ notice() }}</p>
      }
      @if (deliveries(); as result) {
        <p>
          Найдено: {{ result.totalCount }}. Отправлено означает, что письмо принял почтовый сервер.
        </p>
        <div class="staff-table">
          <table>
            <thead>
              <tr>
                <th>Аккаунт и назначение</th>
                <th>Состояние</th>
                <th>Попытки</th>
                <th>Срок ссылки</th>
                <th>Действия</th>
              </tr>
            </thead>
            <tbody>
              @for (item of result.items; track item.id) {
                <tr>
                  <td>
                    {{ item.accountId }}<br />{{
                      item.purpose === 'recovery' ? 'Восстановление пароля' : 'Подтверждение Email'
                    }}
                  </td>
                  <td>
                    {{ stateLabel(item.state) }}
                    @if (item.errorCode) {
                      <p>{{ errorLabel(item.errorCode) }}</p>
                    }
                    @if (item.nextAttemptAtUtc) {
                      <p>Следующая попытка: {{ clock.format(item.nextAttemptAtUtc) }}</p>
                    }
                  </td>
                  <td>{{ item.attemptCount }}</td>
                  <td>{{ clock.format(item.expiresAtUtc) }}</td>
                  <td>
                    @if (item.state === 'Failed') {
                      <button
                        class="secondary-button"
                        type="button"
                        (click)="replay(item)"
                        [disabled]="busy()"
                      >
                        Повторить отправку
                      </button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (!result.items.length) {
          <p>Писем с такими условиями нет.</p>
        }
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
      @if (busy()) {
        <p role="status">Загружаем…</p>
      }
    </section>
  `,
})
export class EmailDeliveries implements OnInit {
  protected readonly clock = inject(CityClock);
  private readonly api = inject(AdministrationApi);
  protected state = '';
  protected readonly stateOptions = Object.keys(states);
  protected readonly deliveries = signal<EmailDeliveryPage | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');

  ngOnInit(): void {
    void this.load(1);
  }
  protected stateLabel(state: string): string {
    return states[state] ?? state;
  }
  protected errorLabel(code: string): string {
    return errors[code] ?? 'Ошибка доставки';
  }

  protected async load(page: number): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      this.deliveries.set(await this.api.deliveries(this.state, page));
    } catch (error) {
      this.deliveries.set(null);
      this.error.set(administrationError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async replay(delivery: EmailDeliverySummary): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    let failure: unknown;
    try {
      await this.api.replay(delivery);
      this.notice.set('Повторная отправка поставлена в очередь. Действие записано в историю.');
    } catch (error) {
      failure = error;
    } finally {
      this.busy.set(false);
      await this.load(this.deliveries()?.page ?? 1);
      if (failure) this.error.set(administrationError(failure));
    }
  }
}
