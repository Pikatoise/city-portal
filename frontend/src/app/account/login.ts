import { Component, inject, signal } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AccountApi, errorMessage } from './account-api';

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="account-panel" aria-labelledby="login-title">
      <p class="eyebrow">Личный кабинет</p>
      <h1 id="login-title">Вход</h1>
      <p>Нет аккаунта? <a routerLink="/account/register">Зарегистрироваться</a></p>
      <form #form="ngForm" (ngSubmit)="submit(form)" [attr.aria-busy]="busy()">
        <label for="login-email">Email</label>
        <input
          id="login-email"
          name="email"
          type="email"
          autocomplete="username"
          required
          maxlength="254"
          [(ngModel)]="email"
        />
        <label for="login-password">Пароль</label>
        <input
          id="login-password"
          name="password"
          type="password"
          autocomplete="current-password"
          required
          [(ngModel)]="password"
        />
        <label class="checkbox-label"
          ><input name="rememberMe" type="checkbox" [(ngModel)]="rememberMe" /> Запомнить меня на 30
          дней</label
        >
        @if (error()) {
          <p class="form-error" role="alert">{{ error() }}</p>
        }
        <button class="primary-button" type="submit" [disabled]="busy() || form.invalid">
          {{ busy() ? 'Входим…' : 'Войти' }}
        </button>
      </form>
      <p><a routerLink="/account/forgot-password">Забыли пароль?</a></p>
    </section>
  `,
})
export class Login {
  private readonly api = inject(AccountApi);
  private readonly router = inject(Router);
  protected email = '';
  protected password = '';
  protected rememberMe = false;
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async submit(form: NgForm): Promise<void> {
    if (this.busy() || form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.login({
        email: this.email,
        password: this.password,
        rememberMe: this.rememberMe,
      });
      this.password = '';
      await this.router.navigateByUrl('/account');
    } catch (error) {
      this.password = '';
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
