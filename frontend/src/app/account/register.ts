import { Component, inject, signal } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccountApi, accountProblem, errorMessage } from './account-api';

const validationMessages: Record<string, string> = {
  email: 'Введите корректный Email длиной не более 254 символов.',
  displayName: 'Имя должно содержать от 3 до 20 букв и цифр.',
  password: 'Пароль должен содержать 8–30 символов ASCII без пробелов и хотя бы один знак.',
};

@Component({
  selector: 'app-register',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="account-panel" aria-labelledby="register-title">
      <p class="eyebrow">Личный кабинет</p>
      <h1 id="register-title">Регистрация</h1>
      @if (created()) {
        <div role="status">
          <h2>Аккаунт создан</h2>
          <p>
            Для подтверждения Email откройте ссылку из письма. Проверить отправку и запросить новое
            письмо можно в профиле после входа.
          </p>
          <a class="primary-link" routerLink="/account/login">Войти в аккаунт</a>
        </div>
      } @else {
        <p>Уже есть аккаунт? <a routerLink="/account/login">Войти</a></p>
        <form #form="ngForm" (ngSubmit)="submit(form)" [attr.aria-busy]="busy()">
          <label for="register-email">Email</label>
          <input
            id="register-email"
            name="email"
            type="email"
            autocomplete="email"
            required
            maxlength="254"
            [(ngModel)]="email"
            [attr.aria-invalid]="!!fieldErrors()['email']"
            aria-describedby="email-errors"
          />
          <p id="email-errors" class="field-error">{{ fieldErrors()['email'] }}</p>
          <label for="display-name">Имя на портале</label>
          <input
            id="display-name"
            name="displayName"
            autocomplete="nickname"
            required
            [(ngModel)]="displayName"
            aria-describedby="name-hint name-errors"
            [attr.aria-invalid]="!!fieldErrors()['displayName']"
          />
          <p id="name-hint" class="field-hint">От 3 до 20 букв и цифр. Имя может повторяться.</p>
          <p id="name-errors" class="field-error">{{ fieldErrors()['displayName'] }}</p>
          <label for="register-password">Пароль</label>
          <input
            id="register-password"
            name="password"
            type="password"
            autocomplete="new-password"
            required
            minlength="8"
            maxlength="30"
            [(ngModel)]="password"
            aria-describedby="password-hint password-errors"
            [attr.aria-invalid]="!!fieldErrors()['password']"
          />
          <p id="password-hint" class="field-hint">
            8–30 символов: латинские буквы, цифры и знаки ASCII. Хотя бы один знак обязателен.
            Пробелы не допускаются.
          </p>
          <p id="password-errors" class="field-error">{{ fieldErrors()['password'] }}</p>
          @if (error()) {
            <p role="alert" class="form-error">{{ error() }}</p>
          }
          @if (duplicate()) {
            <p>
              Этот Email уже зарегистрирован.
              <a routerLink="/account/login">Войдите в аккаунт</a> или
              <a routerLink="/account/forgot-password">восстановите пароль</a>.
            </p>
          }
          <button class="primary-button" type="submit" [disabled]="busy() || form.invalid">
            {{ busy() ? 'Создаём аккаунт…' : 'Зарегистрироваться' }}
          </button>
        </form>
      }
    </section>
  `,
})
export class Register {
  private readonly api = inject(AccountApi);
  protected email = '';
  protected displayName = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly created = signal(false);
  protected readonly duplicate = signal(false);
  protected readonly error = signal('');
  protected readonly fieldErrors = signal<Record<string, string>>({});

  protected async submit(form: NgForm): Promise<void> {
    if (this.busy() || form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    this.duplicate.set(false);
    this.fieldErrors.set({});
    try {
      await this.api.register({
        email: this.email,
        displayName: this.displayName,
        password: this.password,
      });
      this.password = '';
      form.resetForm();
      this.created.set(true);
    } catch (error) {
      const problem = accountProblem(error);
      if (problem.code === 'duplicate_email') this.duplicate.set(true);
      else this.error.set(errorMessage(error));
      this.fieldErrors.set(
        Object.fromEntries(
          Object.keys(problem.errors ?? {}).map((field) => [
            field.charAt(0).toLowerCase() + field.slice(1),
            validationMessages[field.charAt(0).toLowerCase() + field.slice(1)] ??
              'Проверьте значение поля.',
          ]),
        ),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
