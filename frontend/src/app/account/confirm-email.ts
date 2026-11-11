import { Location } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AccountApi, accountProblem, errorMessage } from './account-api';

@Component({
  selector: 'app-confirm-email',
  imports: [RouterLink],
  template: `
    <section class="account-panel" aria-labelledby="confirm-title">
      <p class="eyebrow">Личный кабинет</p>
      <h1 id="confirm-title">Подтверждение Email</h1>
      <p role="status" aria-live="polite">{{ message() }}</p>
      @if (canRetry()) {
        <button class="primary-button" type="button" (click)="confirm()">
          {{ buttonLabel() }}
        </button>
      }
      <p>
        <a routerLink="/account">Перейти в профиль</a> · <a routerLink="/account/login">Войти</a>
      </p>
    </section>
  `,
})
export class ConfirmEmail {
  private readonly api = inject(AccountApi);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private linkId = '';
  private token = '';
  private linkVersion = 0;
  private busy = false;
  protected readonly message = signal('Нажмите кнопку, чтобы подтвердить Email.');
  protected readonly canRetry = signal(false);
  protected readonly buttonLabel = signal('Подтвердить Email');

  constructor() {
    this.route.fragment.pipe(takeUntilDestroyed()).subscribe((fragment) => this.readLink(fragment));
  }

  private readLink(fragment: string | null): void {
    this.linkVersion++;
    this.busy = false;
    const parameters = new URLSearchParams(fragment ?? '');
    this.linkId = parameters.get('linkId') ?? '';
    this.token = parameters.get('token') ?? '';
    // Remove the bearer secret from browser history before any asynchronous request.
    this.location.replaceState('/account/confirm-email');
    this.canRetry.set(this.hasValidLink());
    this.buttonLabel.set('Подтвердить Email');
    if (this.canRetry()) this.message.set('Нажмите кнопку, чтобы подтвердить Email.');
    else
      this.message.set(
        'Ссылка недействительна. Запросить новое письмо можно в профиле после входа.',
      );
  }

  protected async confirm(): Promise<void> {
    if (this.busy) return;
    if (!this.hasValidLink()) {
      this.message.set(
        'Ссылка недействительна. Запросить новое письмо можно в профиле после входа.',
      );
      return;
    }
    this.busy = true;
    const version = this.linkVersion;
    this.canRetry.set(false);
    this.message.set('Подтверждаем Email…');
    try {
      const result = await this.api.confirm({ linkId: this.linkId, token: this.token });
      if (version !== this.linkVersion) return;
      this.message.set(
        result.status === 'AlreadyConfirmed' ? 'Email уже подтверждён.' : 'Email подтверждён.',
      );
    } catch (error) {
      if (version !== this.linkVersion) return;
      const code = accountProblem(error).code;
      if (code === 'expired_link')
        this.message.set(
          'Срок действия ссылки истёк. Запросить новое письмо можно в профиле после входа.',
        );
      else if (code === 'invalid_link')
        this.message.set(
          'Ссылка недействительна. Запросить новое письмо можно в профиле после входа.',
        );
      else {
        this.message.set(errorMessage(error));
        this.buttonLabel.set('Повторить');
        this.canRetry.set(true);
      }
    } finally {
      if (version === this.linkVersion) this.busy = false;
    }
  }

  private hasValidLink(): boolean {
    return (
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(this.linkId) &&
      this.linkId !== '00000000-0000-0000-0000-000000000000' &&
      this.token.trim().length > 0 &&
      this.token.length <= 128
    );
  }
}
