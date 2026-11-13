import { Component, inject, signal } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api } from './api/api';
import { InstallationState } from './installation-state';
import { installSite } from './api/fn/installation/install-site';
import { getCsrf } from './api/fn/auth/get-csrf';
import { accountProblem } from './account/account-api';
import { SiteMetadata } from './site-metadata';

@Component({
  selector: 'app-setup',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="account-panel" aria-labelledby="setup-title">
      <p class="eyebrow">Первый запуск</p>
      <h1 id="setup-title">Настройка сайта</h1>
      @if (installed()) {
        <p role="status">Сайт настроен. Мастер установки закрыт.</p>
        <p><a routerLink="/account/login">Войти в аккаунт</a></p>
      } @else if (ready() && !canInstall()) {
        <p role="status">
          Мастер установки отключён. Сначала настройте сайт в локальном режиме, затем откройте
          доступ из сети.
        </p>
      } @else if (ready()) {
        <p>
          Укажите параметры портала и первого администратора. После проверки почты настройки
          сохранятся вместе.
        </p>
        <form #form="ngForm" (ngSubmit)="submit(form)" [attr.aria-busy]="busy()">
          <h2>Сайт и город</h2>
          <label for="setup-name">Название сайта</label>
          <input id="setup-name" name="name" required maxlength="120" [(ngModel)]="name" />
          <label for="setup-city">Город</label>
          <input id="setup-city" name="city" required maxlength="120" [(ngModel)]="city" />
          <label for="setup-region">Регион (необязательно)</label>
          <input id="setup-region" name="region" maxlength="120" [(ngModel)]="region" />
          <label for="setup-zone">Часовой пояс</label>
          <input
            id="setup-zone"
            name="timeZone"
            required
            list="setup-zones"
            [(ngModel)]="timeZone"
          />
          <datalist id="setup-zones">
            @for (zone of zones; track zone) {
              <option [value]="zone"></option>
            }
          </datalist>
          <h2>Первый администратор</h2>
          <label for="setup-email">Email для входа</label>
          <input
            id="setup-email"
            name="email"
            type="email"
            autocomplete="username"
            required
            maxlength="254"
            [(ngModel)]="email"
          />
          <label for="setup-display-name">Имя на портале</label>
          <input
            id="setup-display-name"
            name="displayName"
            autocomplete="nickname"
            required
            [(ngModel)]="displayName"
          />
          <p class="field-hint">3–20 букв или цифр.</p>
          <label for="setup-password">Пароль</label>
          <input
            id="setup-password"
            name="password"
            type="password"
            autocomplete="new-password"
            required
            minlength="8"
            maxlength="30"
            [(ngModel)]="password"
          />
          <p class="field-hint">
            8–30 символов ASCII без пробелов, хотя бы один знак. Администратор сразу получает
            доступ.
          </p>
          <h2>Отправка Email</h2>
          <label for="setup-smtp-host">SMTP-сервер</label>
          <input
            id="setup-smtp-host"
            name="smtpHost"
            required
            maxlength="253"
            [(ngModel)]="smtpHost"
          />
          <label for="setup-smtp-port">Порт</label>
          <input
            id="setup-smtp-port"
            name="smtpPort"
            type="number"
            required
            min="1"
            max="65535"
            [(ngModel)]="smtpPort"
          />
          <label for="setup-security">Защита соединения</label>
          <select id="setup-security" name="security" [(ngModel)]="security">
            <option value="StartTls">STARTTLS (обычно порт 587)</option>
            <option value="Tls">TLS при подключении (обычно порт 465)</option>
            <option value="None">Без TLS (только локальный сервер)</option>
          </select>
          <p class="field-hint">
            Отключайте только для локального почтового сервера, например Mailpit.
          </p>
          <label for="setup-sender">Email отправителя</label>
          <input
            id="setup-sender"
            name="senderAddress"
            type="email"
            required
            maxlength="254"
            [(ngModel)]="senderAddress"
          />
          <label for="setup-smtp-user">SMTP-логин (необязательно)</label>
          <input
            id="setup-smtp-user"
            name="smtpUsername"
            autocomplete="off"
            maxlength="1000"
            [(ngModel)]="smtpUsername"
          />
          <label for="setup-smtp-password">SMTP-пароль (необязательно)</label>
          <input
            id="setup-smtp-password"
            name="smtpPassword"
            type="password"
            autocomplete="off"
            maxlength="1000"
            [(ngModel)]="smtpPassword"
          />
          <p>
            Тестовое письмо будет отправлено на Email администратора. При ошибке установка останется
            незавершённой.
          </p>
          <button class="primary-button" type="submit" [disabled]="busy() || form.invalid">
            {{ busy() ? 'Проверяем почту и сохраняем…' : 'Проверить почту и завершить' }}
          </button>
        </form>
      } @else if (!error()) {
        <p role="status">Проверяем состояние установки…</p>
      }
      @if (error()) {
        <p role="alert" class="form-error">{{ error() }}</p>
        @if (!ready()) {
          <button type="button" (click)="load()">Повторить</button>
        }
      }
    </section>
  `,
})
export class Setup {
  private readonly installation = inject(InstallationState);
  private readonly api = inject(Api);
  private readonly site = inject(SiteMetadata);
  protected readonly installed = signal(false);
  protected readonly canInstall = signal(false);
  protected readonly ready = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly zones = ['UTC', ...Intl.supportedValuesOf('timeZone')];
  protected name = '';
  protected city = '';
  protected region = '';
  protected timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  protected email = '';
  protected displayName = '';
  protected password = '';
  protected smtpHost = '';
  protected smtpPort = 587;
  protected security = 'StartTls';
  protected senderAddress = '';
  protected smtpUsername = '';
  protected smtpPassword = '';

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set('');
    try {
      const status = await this.installation.load();
      if (!status) throw new Error('Installation status unavailable.');
      this.installed.set(status.installed);
      this.canInstall.set(status.canInstall);
      this.ready.set(true);
    } catch {
      this.error.set('Не удалось проверить состояние установки. Повторите запрос.');
    }
  }

  protected async submit(form: NgForm): Promise<void> {
    if (this.busy() || form.invalid || !this.canInstall()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const csrf = await this.api.invoke(getCsrf);
      await this.api.invoke(installSite, {
        'X-CSRF-TOKEN': csrf.token,
        body: {
          email: this.email,
          displayName: this.displayName,
          password: this.password,
          name: this.name,
          city: this.city,
          region: this.region,
          timeZone: this.timeZone,
          smtpHost: this.smtpHost,
          smtpPort: this.smtpPort,
          senderAddress: this.senderAddress,
          smtpUsername: this.smtpUsername || null,
          smtpPassword: this.smtpPassword || null,
          useTls: this.security !== 'None',
          implicitTls: this.security === 'Tls',
        },
      });
      this.password = this.smtpPassword = '';
      this.installed.set(true);
      this.installation.complete();
      await this.site.load();
    } catch (error) {
      const code = accountProblem(error).code;
      const messages: Record<string, string> = {
        installation_disabled:
          'Установка отключена. Переключите сервер в локальный режим настройки.',
        already_installed: 'Сайт уже настроен. Повторная установка запрещена.',
        smtp_test_failed:
          'Не удалось отправить тестовое письмо. Проверьте SMTP-сервер, порт, защиту и учётные данные.',
        invalid_time_zone: 'Выберите корректный часовой пояс.',
        invalid_public_origin: 'Владелец сервера должен настроить публичный HTTPS-адрес сайта.',
      };
      this.error.set(
        messages[code ?? ''] ??
          'Не удалось завершить установку. Проверьте поля и повторите запрос.',
      );
      if (code === 'already_installed') {
        this.installed.set(true);
        this.installation.complete();
        this.password = this.smtpPassword = '';
      }
    } finally {
      this.busy.set(false);
    }
  }
}
