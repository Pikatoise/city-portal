import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiWrite } from '../api-write';
import { Api } from '../api/api';
import { getIntegrationSettings } from '../api/fn/integration-administration/get-integration-settings';
import { saveIntegrationSettings } from '../api/fn/integration-administration/save-integration-settings';
import { IntegrationStatus } from '../api/models/integration-status';
import { SaveIntegrationSettings } from '../api/models/save-integration-settings';
import { administrationError } from './administration-api';
import { CityClock } from './city-clock';

@Component({
  selector: 'app-integrations',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="staff-panel" aria-labelledby="integrations-title">
      <h1 id="integrations-title">Интеграции</h1>
      <nav class="staff-nav" aria-label="Администрирование">
        <a routerLink="/staff/accounts">Аккаунты</a>
        <a routerLink="/staff/email-deliveries">Доставка Email</a>
      </nav>
      @if (error()) {
        <p role="alert" class="form-error">{{ error() }}</p>
      }
      @if (notice()) {
        <p role="status">{{ notice() }}</p>
      }
      @if (draft(); as settings) {
        <form (ngSubmit)="save(settings)" #form="ngForm">
          <fieldset [disabled]="busy()">
            <legend>Новости</legend>
            <label
              ><input name="rss" type="checkbox" [(ngModel)]="settings.rssEnabled" /> RSS</label
            >
            <label
              ><input name="mcp" type="checkbox" [(ngModel)]="settings.mcpEnabled" /> MCP</label
            >
            @if (!status()?.mcpConfigured) {
              <p>OAuth для MCP ещё не настроен на сервере.</p>
            }
          </fieldset>
          <fieldset [disabled]="busy()">
            <legend>Погода</legend>
            <label>
              <input name="weather" type="checkbox" [(ngModel)]="settings.weatherEnabled" />
              Показывать погоду на главной
            </label>
            <label>
              Широта
              <input
                name="latitude"
                type="number"
                min="-90"
                max="90"
                step="any"
                [required]="settings.weatherEnabled"
                [(ngModel)]="settings.latitude"
              />
            </label>
            <label>
              Долгота
              <input
                name="longitude"
                type="number"
                min="-180"
                max="180"
                step="any"
                [required]="settings.weatherEnabled"
                [(ngModel)]="settings.longitude"
              />
            </label>
            @if (status()?.weatherRefreshedAtUtc; as refreshedAt) {
              <p>Последнее обновление {{ clock.format(refreshedAt) }}</p>
            }
          </fieldset>
          <fieldset [disabled]="busy()">
            <legend>Telegram</legend>
            <label>
              <input name="telegram" type="checkbox" [(ngModel)]="settings.telegramEnabled" />
              Разрешить отправку анонсов
            </label>
            <label>
              Публичный канал
              <input
                name="channel"
                placeholder="@news_channel"
                maxlength="33"
                [required]="settings.telegramEnabled"
                [(ngModel)]="settings.telegramChannel"
              />
            </label>
            @if (!status()?.telegramConfigured) {
              <p>Токен Telegram ещё не настроен на сервере.</p>
            }
            <p>
              В очереди {{ status()?.telegramPending }}. Требуют внимания
              {{ status()?.telegramNeedsAttention }}.
            </p>
            <label>
              <input name="ai" type="checkbox" [(ngModel)]="settings.aiEnabled" />
              Предлагать текст анонса с помощью AI
            </label>
            @if (!status()?.aiConfigured) {
              <p>Ключ OpenAI ещё не настроен на сервере.</p>
            }
          </fieldset>
          <button class="primary-button" type="submit" [disabled]="busy() || form.invalid">
            Сохранить настройки
          </button>
          <button class="secondary-button" type="button" [disabled]="busy()" (click)="load()">
            Обновить с сервера
          </button>
        </form>
      } @else if (busy()) {
        <p role="status">Загружаем настройки…</p>
      } @else {
        <button class="secondary-button" type="button" (click)="load()">Обновить с сервера</button>
      }
    </section>
  `,
  styles: `
    form {
      display: grid;
      gap: 1rem;
      justify-items: start;
      max-width: 42rem;
    }
    fieldset {
      display: grid;
      gap: 0.75rem;
      width: 100%;
      min-width: 0;
    }
    legend {
      font-weight: 600;
    }
    label {
      display: flex;
      gap: 0.5rem;
      align-items: center;
      flex-wrap: wrap;
    }
    input[type='number'],
    input[type='text'],
    input[name='channel'] {
      width: min(100%, 16rem);
    }
    fieldset p {
      margin: 0;
    }
  `,
})
export class Integrations implements OnInit {
  private readonly api = inject(Api);
  private readonly write = inject(ApiWrite);
  protected readonly clock = inject(CityClock);
  protected readonly draft = signal<SaveIntegrationSettings | null>(null);
  protected readonly status = signal<IntegrationStatus | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');

  ngOnInit(): void {
    void this.load();
  }

  protected load(): Promise<void> {
    return this.update(() => this.api.invoke(getIntegrationSettings));
  }

  protected save(settings: SaveIntegrationSettings): Promise<void> {
    return this.update(
      () => this.write.invoke(saveIntegrationSettings, { body: { ...settings } }),
      'Настройки сохранены.',
    );
  }

  private async update(operation: () => Promise<IntegrationStatus>, notice = ''): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      const status = await operation();
      this.status.set(status);
      this.draft.set({ ...status.settings });
      this.notice.set(notice);
    } catch (error) {
      this.error.set(administrationError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
