import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  AccountApi,
  AccountProfile,
  ConfirmationDelivery,
  accountProblem,
  errorMessage,
} from './account-api';

const roleLabels: Record<string, string> = {
  RegularUser: 'Пользователь',
  'Regular User': 'Пользователь',
  Author: 'Автор',
  Editor: 'Редактор',
  Moderator: 'Модератор',
  Administrator: 'Администратор',
};

@Component({
  selector: 'app-profile',
  imports: [RouterLink],
  template: `
    <section class="account-panel" aria-labelledby="profile-title">
      <p class="eyebrow">Личный кабинет</p>
      <h1 id="profile-title">Профиль</h1>
      @if (profile(); as account) {
        <dl class="profile-fields">
          <dt>Email</dt>
          <dd>{{ account.email }}</dd>
          <dt>Имя на портале</dt>
          <dd>{{ account.displayName }}</dd>
          <dt>Подтверждение Email</dt>
          <dd>{{ account.emailConfirmed ? 'Подтверждён' : 'Не подтверждён' }}</dd>
          <dt>Роли</dt>
          <dd>{{ roles(account.roles) }}</dd>
        </dl>
        @if (!account.emailConfirmed) {
          <section class="confirmation-card" aria-labelledby="confirmation-title">
            <h2 id="confirmation-title">Подтвердите Email</h2>
            <p>
              Откройте ссылку из письма. Она действует 24 часа. Новое письмо отменяет предыдущую
              ссылку.
            </p>
            <p role="status" aria-live="polite" [attr.aria-busy]="delivery()?.state === 'Loading'">
              @switch (delivery()?.state) {
                @case ('Sent') {
                  Отправлено
                }
                @case ('Error') {
                  Ошибка, повторите письмо
                }
                @default {
                  <span class="spinner" aria-hidden="true"></span
                  ><span class="visually-hidden">Отправляем письмо…</span>
                }
              }
            </p>
            @if (delivery()?.state === 'Sent') {
              <p class="field-hint">
                Почтовый сервер принял письмо. Проверьте входящие и папку «Спам».
              </p>
            }
            <button
              type="button"
              class="secondary-button"
              (click)="resend()"
              [disabled]="busy() || waitSeconds() > 0"
            >
              {{ busy() ? 'Запрашиваем письмо…' : 'Отправить новое письмо' }}
            </button>
            @if (waitSeconds() > 0) {
              <p class="field-hint">Повторная отправка доступна через {{ waitSeconds() }} сек.</p>
            }
            <button type="button" class="text-button" (click)="refresh()" [disabled]="refreshing()">
              Обновить
            </button>
          </section>
        }
        @if (error()) {
          <p role="alert" class="form-error">{{ error() }}</p>
        }
        <button type="button" class="secondary-button" (click)="logout()" [disabled]="busy()">
          Выйти
        </button>
      } @else if (error()) {
        <p role="alert" class="form-error">{{ error() }}</p>
        <button class="secondary-button" type="button" (click)="refresh()">Повторить</button>
        <p><a routerLink="/account/login">Войти</a></p>
      } @else {
        <p role="status">Загружаем профиль…</p>
      }
    </section>
  `,
})
export class Profile {
  private readonly api = inject(AccountApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private destroyed = false;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private waitUntil = 0;
  private deliveryGeneration = 0;
  protected readonly profile = signal<AccountProfile | null>(null);
  protected readonly delivery = signal<ConfirmationDelivery | null>(null);
  protected readonly waitSeconds = signal(0);
  protected readonly busy = signal(false);
  protected readonly refreshing = signal(false);
  protected readonly error = signal('');

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      clearTimeout(this.timer);
    });
    void this.refresh();
    this.tick();
  }

  protected roles(roles: string[]): string {
    return roles.map((role) => roleLabels[role] ?? role).join(', ');
  }

  protected async refresh(): Promise<void> {
    if (this.refreshing() || this.destroyed) return;
    this.refreshing.set(true);
    const deliveryGeneration = this.deliveryGeneration;
    try {
      const account = await this.api.profile();
      if (this.destroyed || deliveryGeneration !== this.deliveryGeneration) return;
      this.profile.set(account);
      this.setDelivery(account.confirmationDelivery);
      this.error.set('');
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401)
        await this.router.navigateByUrl('/account/login');
      else this.error.set(errorMessage(error));
    } finally {
      this.refreshing.set(false);
    }
  }

  protected async resend(): Promise<void> {
    if (this.busy() || this.waitSeconds() > 0) return;
    this.busy.set(true);
    this.deliveryGeneration++;
    this.error.set('');
    try {
      this.setDelivery(await this.api.resend());
    } catch (error) {
      const seconds = accountProblem(error).retryAfterSeconds;
      if (seconds) {
        this.waitUntil = Date.now() + seconds * 1000;
        this.updateWait();
      }
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async logout(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.deliveryGeneration++;
    try {
      await this.api.logout();
      this.profile.set(null);
      await this.router.navigateByUrl('/account/login');
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  private setDelivery(delivery: ConfirmationDelivery): void {
    this.delivery.set(delivery);
    this.waitUntil = Date.now() + Number(delivery.resendAfterSeconds) * 1000;
    this.updateWait();
  }

  private updateWait(): void {
    this.waitSeconds.set(Math.max(0, Math.ceil((this.waitUntil - Date.now()) / 1000)));
  }

  private tick(): void {
    this.timer = setTimeout(() => {
      this.updateWait();
      // Polling continues after a failed attempt because background delivery may recover.
      if (this.profile() && !this.profile()!.emailConfirmed && !this.busy()) void this.refresh();
      if (!this.destroyed) this.tick();
    }, 5000);
  }
}
