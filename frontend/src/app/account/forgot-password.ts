import { Component, inject, signal } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccountApi, errorMessage } from './account-api';

@Component({
  selector: 'app-forgot-password',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="account-panel" aria-labelledby="recovery-title">
      <p class="eyebrow">Личный кабинет</p>
      <h1 id="recovery-title">Восстановление пароля</h1>
      <p>
        Укажите Email аккаунта. Ссылка действует один час. Новое письмо отменяет предыдущую ссылку.
      </p>
      <form #form="ngForm" (ngSubmit)="submit(form)" [attr.aria-busy]="busy()">
        <label for="recovery-email">Email</label>
        <input
          id="recovery-email"
          name="email"
          type="email"
          autocomplete="email"
          required
          maxlength="254"
          [(ngModel)]="email"
        />
        <button class="primary-button" type="submit" [disabled]="busy() || form.invalid">
          {{ busy() ? 'Запрашиваем письмо…' : 'Запросить письмо' }}
        </button>
      </form>
      @if (accepted()) {
        <p role="status">
          Если аккаунт с таким Email существует и отправка доступна, мы отправим ссылку для
          восстановления пароля. Проверьте входящие и папку «Спам».
        </p>
      }
      @if (error()) {
        <p role="alert" class="form-error">{{ error() }}</p>
      }
      <p><a routerLink="/account/login">Войти</a></p>
    </section>
  `,
})
export class ForgotPassword {
  private readonly api = inject(AccountApi);
  protected email = '';
  protected readonly busy = signal(false);
  protected readonly accepted = signal(false);
  protected readonly error = signal('');

  protected async submit(form: NgForm): Promise<void> {
    if (this.busy() || form.invalid) return;
    this.busy.set(true);
    this.accepted.set(false);
    this.error.set('');
    try {
      await this.api.forgotPassword(this.email);
      this.accepted.set(true);
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
