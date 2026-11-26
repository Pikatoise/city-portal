import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TelegramAnnouncementResponse } from '../api/models/telegram-announcement-response';
import { TelegramDeliveryResponse } from '../api/models/telegram-delivery-response';
import { newsError } from './news-api';
import { TelegramApi } from './telegram-api';

const stateLabels: Record<string, string> = {
  Queued: 'В очереди',
  Sending: 'Отправляется',
  RetryScheduled: 'Telegram ограничил отправку. Повтор запланирован.',
  Sent: 'Опубликовано в Telegram',
  NeedsReview: 'Результат неизвестен. Сверь публикации в канале.',
  Failed: 'Telegram отклонил отправку',
  Cancelled: 'Отправка отменена. Проверь статью и настройки канала.',
};

@Component({
  selector: 'app-telegram-announcement',
  imports: [FormsModule],
  templateUrl: './telegram-announcement.html',
  styles: `
    :host {
      display: block;
      margin-top: 1rem;
    }

    textarea,
    input[type='url'] {
      display: block;
      width: 100%;
    }

    .actions {
      display: flex;
      gap: 0.5rem;
      flex-wrap: wrap;
      margin-top: 0.75rem;
    }
  `,
})
export class TelegramAnnouncement implements OnInit {
  private readonly api = inject(TelegramApi);
  readonly id = input.required<string>();
  readonly channel = input<string>();
  readonly resolved = output<void>();
  readonly announcement = signal<TelegramAnnouncementResponse | null>(null);
  readonly delivery = computed(() => this.announcement()?.delivery);
  readonly stateLabel = computed(() => stateLabels[this.delivery()?.state ?? ''] ?? '');
  readonly needsReview = computed(() => this.delivery()?.state === 'NeedsReview');
  readonly canQueue = computed(() => {
    const announcement = this.announcement();
    const delivery = this.delivery();
    return (
      !!announcement?.enabled &&
      !!announcement.revisionId &&
      !this.needsReview() &&
      (!delivery ||
        delivery.channel !== announcement.channel ||
        ['Failed', 'Cancelled'].includes(delivery.state))
    );
  });
  readonly canRetry = computed(() => {
    const announcement = this.announcement();
    return (
      this.needsReview() &&
      !!announcement?.enabled &&
      !!announcement.revisionId &&
      this.delivery()?.channel === announcement.channel
    );
  });
  readonly retryUsesNewRevision = computed(
    () => this.canRetry() && this.delivery()?.revisionId !== this.announcement()?.revisionId,
  );
  readonly busy = signal(false);
  readonly error = signal('');
  readonly suggestion = signal('');
  text = '';
  postUrl = '';
  checkedNoPost = false;

  ngOnInit(): void {
    void this.refresh();
  }

  async refresh(): Promise<void> {
    await this.action(async () => {
      const previous = this.announcement();
      const next = await this.api.get(this.id(), this.channel());
      if (!previous || this.text === (previous.suggestedText ?? ''))
        this.text = next.suggestedText ?? '';
      this.announcement.set(next);
      this.suggestion.set('');
      this.checkedNoPost = false;
    });
  }

  async generate(): Promise<void> {
    const current = this.announcement();
    if (!current?.aiEnabled || !current.revisionId) return;
    const { revisionId } = current;
    await this.action(async () => {
      const result = await this.api.suggest(this.id(), revisionId);
      this.suggestion.set(result.text);
    }, true);
  }

  useSuggestion(): void {
    this.text = this.suggestion();
    this.suggestion.set('');
  }

  async queue(): Promise<void> {
    const current = this.announcement();
    if (!this.canQueue() || !current?.channel || !current.revisionId || !this.text.trim()) return;
    const { channel, revisionId } = current;
    await this.action(async () => {
      this.updateDelivery(await this.api.queue(this.id(), revisionId, channel, this.text.trim()));
    });
  }

  async resolve(decision: 'retry' | 'cancel' | 'link'): Promise<void> {
    const delivery = this.delivery();
    if (!delivery || !this.needsReview()) return;
    const retry = decision === 'retry';
    const cancel = decision === 'cancel';
    if (retry && (!this.canRetry() || !this.checkedNoPost)) return;
    if (cancel && !this.checkedNoPost) return;
    if (decision === 'link' && !this.postUrl.trim()) return;
    const { channel, version } = delivery;
    await this.action(async () => {
      const result = await this.api.resolve(this.id(), {
        channel,
        version,
        retry,
        cancel,
        postUrl: decision === 'link' ? this.postUrl.trim() : null,
        revisionId: retry ? (this.announcement()?.revisionId ?? null) : null,
        text: retry && this.retryUsesNewRevision() ? this.text.trim() : null,
      });
      this.updateDelivery(result);
      this.checkedNoPost = false;
      this.postUrl = '';
      this.resolved.emit();
    });
  }

  private updateDelivery(delivery: TelegramDeliveryResponse): void {
    this.announcement.update((current) => (current ? { ...current, delivery } : null));
    this.suggestion.set('');
  }

  private async action(action: () => Promise<void>, generating = false): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        this.error.set('Статья, канал или отправка изменились. Обнови данные и проверь анонс.');
      } else if (generating && error instanceof HttpErrorResponse && error.status === 503) {
        this.error.set('Не удалось предложить текст. Можно отправить анонс вручную.');
      } else {
        this.error.set(newsError(error));
      }
    } finally {
      this.busy.set(false);
    }
  }
}
