import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AccountNotificationResponse } from '../api/models/account-notification-response';
import { NotificationPreferenceResponse } from '../api/models/notification-preference-response';
import { CityClock } from '../staff/city-clock';
import { errorMessage } from './account-api';
import { NotificationApi } from './notification-api';

@Component({
  imports: [RouterLink],
  selector: 'app-notifications',
  styleUrl: './notifications.css',
  templateUrl: './notifications.html',
})
export class Notifications implements OnInit {
  private readonly api = inject(NotificationApi);
  protected readonly clock = inject(CityClock);
  readonly items = signal<AccountNotificationResponse[]>([]);
  readonly preferences = signal<NotificationPreferenceResponse[]>([]);
  readonly busy = signal(false);
  readonly loaded = signal(false);
  readonly signedOut = signal(false);
  readonly dirty = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly page = signal(1);
  readonly total = signal(0);
  readonly labels: Record<string, string> = {
    'article.returned': 'Возврат статьи на доработку',
    'article.rejected': 'Окончательное отклонение статьи',
    'article.approved': 'Одобрение статьи',
    'article.published': 'Публикация статьи',
    'suggestion.published': 'Публикация статьи по вашему предложению',
    'article.corrected': 'Редакторское исправление',
    'article.withdrawn': 'Снятие статьи с публикации',
    'article.superseded': 'Закрытие рабочей версии',
    'review.resubmitted': 'Повторная отправка на вашу проверку',
    'review.withdrawn': 'Отзыв статьи автором',
    'review.deleted': 'Удаление статьи автором',
    'review.transferred': 'Передача проверки',
    'template.approved': 'Одобрение шаблона',
    'template.rejected': 'Отклонение шаблона',
    'template.archived': 'Архивация шаблона',
    'template.blocked': 'Блокировка шаблона',
    'template.unblocked': 'Снятие блокировки шаблона',
    'image.blocked': 'Блокировка изображения в ваших статьях',
    'image.unblocked': 'Снятие блокировки изображения в ваших статьях',
  };

  ngOnInit(): void {
    void this.load();
  }

  private fail(error: unknown): void {
    this.signedOut.set(error instanceof HttpErrorResponse && error.status === 401);
    this.error.set(
      this.signedOut() ? 'Войди в аккаунт, чтобы открыть уведомления.' : errorMessage(error),
    );
  }

  async load(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const [list, preferences] = await Promise.all([this.api.list(), this.api.preferences()]);
      this.items.set(list.items);
      this.total.set(Number(list.total));
      this.page.set(Number(list.page));
      this.preferences.set(preferences);
      this.dirty.set(false);
      this.loaded.set(true);
      this.signedOut.set(false);
    } catch (error) {
      this.fail(error);
    } finally {
      this.busy.set(false);
    }
  }

  setChannel(type: string, channel: 'account' | 'email', enabled: boolean): void {
    this.preferences.update((items) =>
      items.map((item) => (item.type === type ? { ...item, [channel]: enabled } : item)),
    );
    this.dirty.set(true);
    this.notice.set('');
  }

  async save(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      this.preferences.set(await this.api.save(this.preferences()));
      this.dirty.set(false);
      this.notice.set('Настройки уведомлений сохранены.');
    } catch (error) {
      this.fail(error);
    } finally {
      this.busy.set(false);
    }
  }

  async changePage(page: number): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const list = await this.api.list(page);
      this.items.set(list.items);
      this.total.set(Number(list.total));
      this.page.set(Number(list.page));
    } catch (error) {
      this.fail(error);
    } finally {
      this.busy.set(false);
    }
  }

  async act(item: AccountNotificationResponse, action: 'read' | 'delete'): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      if (action === 'read') {
        await this.api.read(item.id);
        this.items.update((items) =>
          items.map((entry) => (entry.id === item.id ? { ...entry, read: true } : entry)),
        );
      } else {
        await this.api.delete(item.id);
        await this.changePage(
          Math.min(this.page(), Math.max(1, Math.ceil((this.total() - 1) / 20))),
        );
      }
    } catch (error) {
      this.fail(error);
    } finally {
      this.busy.set(false);
    }
  }
}
