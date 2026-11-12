import { Location } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule, NgForm } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AccountApi, accountProblem, errorMessage } from './account-api';

@Component({
  selector: 'app-reset-password',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="account-panel" aria-labelledby="reset-title">
      <p class="eyebrow">Личный кабинет</p>
      <h1 id="reset-title">Новый пароль</h1>
      @if (done()) {
        <p role="status">Пароль изменён. Все сеансы завершены. Войдите с новым паролем.</p>
      } @else if (validLink()) {
        <form #form="ngForm" (ngSubmit)="submit(form)" [attr.aria-busy]="busy()">
          <label for="reset-password">Новый пароль</label>
          <input
            id="reset-password"
            name="newPassword"
            type="password"
            autocomplete="new-password"
            required
            minlength="8"
            maxlength="30"
            [(ngModel)]="newPassword"
            aria-describedby="reset-hint"
          />
          <p id="reset-hint" class="field-hint">
            8–30 символов ASCII без пробелов, хотя бы один знак. Пароль изменится после нажатия
            кнопки.
          </p>
          <button class="primary-button" type="submit" [disabled]="busy() || form.invalid">
            {{ busy() ? 'Меняем пароль…' : 'Изменить пароль' }}
          </button>
        </form>
      }
      @if (error()) {
        <p role="alert" class="form-error">{{ error() }}</p>
      }
      <p>
        <a routerLink="/account/forgot-password">Запросить новое письмо</a> ·
        <a routerLink="/account/login">Войти</a>
      </p>
    </section>
  `,
})
export class ResetPassword {
  private readonly api = inject(AccountApi);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private linkId = '';
  private token = '';
  private generation = 0;
  protected newPassword = '';
  protected readonly validLink = signal(false);
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal('');

  constructor() {
    this.route.fragment.pipe(takeUntilDestroyed()).subscribe((fragment) => {
      this.generation++;
      const parameters = new URLSearchParams(fragment ?? '');
      this.linkId = parameters.get('linkId') ?? '';
      this.token = parameters.get('token') ?? '';
      this.location.replaceState('/account/reset-password');
      this.newPassword = '';
      this.busy.set(false);
      this.done.set(false);
      this.validLink.set(
        /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(this.linkId) &&
          this.linkId !== '00000000-0000-0000-0000-000000000000' &&
          this.token.trim().length > 0 &&
          this.token.length <= 128,
      );
      this.error.set(this.validLink() ? '' : 'Ссылка недействительна. Запросите новое письмо.');
    });
  }

  protected async submit(form: NgForm): Promise<void> {
    if (this.busy() || !this.validLink() || form.invalid) return;
    const generation = this.generation;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.resetPassword({
        linkId: this.linkId,
        token: this.token,
        newPassword: this.newPassword,
      });
      if (generation !== this.generation) return;
      this.done.set(true);
      this.token = '';
    } catch (error) {
      if (generation !== this.generation) return;
      const code = accountProblem(error).code;
      if (code === 'invalid_link' || code === 'expired_link') this.validLink.set(false);
      this.error.set(errorMessage(error));
    } finally {
      if (generation === this.generation) {
        this.newPassword = '';
        this.busy.set(false);
      }
    }
  }
}
