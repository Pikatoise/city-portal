import { Component, inject, OnInit, signal } from '@angular/core';
import { TelegramRecoveryResponse } from '../api/models/telegram-recovery-response';
import { newsError } from './news-api';
import { TelegramAnnouncement } from './telegram-announcement';
import { TelegramApi } from './telegram-api';

@Component({
  selector: 'app-telegram-recovery',
  imports: [TelegramAnnouncement],
  template: `
    <section aria-labelledby="telegram-recovery-title">
      <h2 id="telegram-recovery-title">Отправки с неизвестным результатом</h2>
      @if (error()) {
        <p role="alert">{{ error() }}</p>
      }
      <button type="button" (click)="load()" [disabled]="busy()">Обновить список</button>
      <ul>
        @for (row of rows(); track key(row)) {
          <li>
            <p>{{ row.channel }} · {{ row.text }}</p>
            <button
              type="button"
              [attr.aria-expanded]="selected() === key(row)"
              (click)="selected.set(selected() === key(row) ? null : key(row))"
            >
              Сверить отправку
            </button>
            @if (selected() === key(row)) {
              <app-telegram-announcement
                [id]="row.articleId"
                [channel]="row.channel"
                (resolved)="load()"
              />
            }
          </li>
        } @empty {
          @if (!busy() && !error()) {
            <li>Отправок для сверки нет.</li>
          }
        }
      </ul>
    </section>
  `,
})
export class TelegramRecovery implements OnInit {
  private readonly api = inject(TelegramApi);
  readonly rows = signal<TelegramRecoveryResponse[]>([]);
  readonly selected = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const rows = await this.api.recovery();
      this.rows.set(rows);
      if (!rows.some((row) => this.key(row) === this.selected())) this.selected.set(null);
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }

  key(row: TelegramRecoveryResponse): string {
    return `${row.articleId}:${row.channel}`;
  }
}
