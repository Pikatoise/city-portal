import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { getCsrf } from '../api/fn/auth/get-csrf';
import { createSuggestion } from '../api/fn/suggestions/create-suggestion';
import { getMySuggestion } from '../api/fn/suggestions/get-my-suggestion';
import { listMySuggestions } from '../api/fn/suggestions/list-my-suggestions';
import { updateMySuggestion } from '../api/fn/suggestions/update-my-suggestion';
import { SuggestionResponse } from '../api/models/suggestion-response';
import { CityClock } from '../staff/city-clock';
import { AccountApi, errorMessage } from './account-api';

@Component({
  selector: 'app-suggestions',
  imports: [FormsModule, RouterLink],
  templateUrl: './suggestions.html',
  styleUrl: './suggestions.css',
})
export class Suggestions implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly account = inject(AccountApi);
  protected readonly clock = inject(CityClock);
  protected readonly items = signal<SuggestionResponse[]>([]);
  protected readonly selected = signal<SuggestionResponse | null>(null);
  protected readonly confirmed = signal(false);
  protected readonly signedOut = signal(false);
  protected readonly busy = signal(false);
  protected readonly loaded = signal(false);
  protected readonly page = signal(1);
  protected readonly hasMore = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly statusLabels: Record<string, string> = {
    Queued: 'В очереди',
    InProgress: 'В работе',
    Published: 'Опубликовано',
  };
  protected title = '';
  protected description = '';
  protected eventDate = '';
  protected location = '';
  protected contact = '';

  ngOnInit(): void {
    void this.load();
  }

  protected async load(page = 1): Promise<void> {
    await this.run(async () => {
      const profile = await this.account.profile();
      this.confirmed.set(profile.emailConfirmed);
      await this.readPage(page);
    });
  }

  protected async open(id: string): Promise<void> {
    await this.run(async () => {
      const { body } = await firstValueFrom(getMySuggestion(this.http, '', { id }));
      this.selected.set(body);
      this.items.update((items) => items.map((item) => (item.id === body.id ? body : item)));
      this.title = body.title;
      this.description = body.description;
      this.eventDate = body.eventDate ?? '';
      this.location = body.location ?? '';
      this.contact = body.contact ?? '';
    });
  }

  protected reset(): void {
    this.selected.set(null);
    this.title = '';
    this.description = '';
    this.eventDate = '';
    this.location = '';
    this.contact = '';
    this.error.set('');
    this.notice.set('');
  }

  protected async save(): Promise<void> {
    const title = this.title.trim();
    const description = this.description.trim();
    if (!title || !description) {
      this.error.set('Заполните заголовок и описание.');
      return;
    }

    await this.run(async () => {
      const { body: csrf } = await firstValueFrom(getCsrf(this.http, ''));
      const details = {
        title,
        description,
        eventDate: this.eventDate || null,
        location: this.location.trim() || null,
        contact: this.contact.trim() || null,
      };
      const current = this.selected();
      if (current) {
        await firstValueFrom(
          updateMySuggestion(this.http, '', {
            id: current.id,
            body: { details, version: current.version },
            'X-CSRF-TOKEN': csrf.token,
          }),
        );
      } else {
        await firstValueFrom(
          createSuggestion(this.http, '', { body: details, 'X-CSRF-TOKEN': csrf.token }),
        );
      }
      this.reset();
      this.notice.set(current ? 'Изменения сохранены.' : 'Предложение отправлено.');
      try {
        await this.readPage(current ? this.page() : 1);
      } catch {
        this.error.set('Не удалось обновить список. Обновите страницу, чтобы увидеть изменения.');
      }
    });
  }

  private async readPage(page: number): Promise<void> {
    const { body } = await firstValueFrom(listMySuggestions(this.http, '', { page }));
    this.items.set(body.items);
    this.page.set(page);
    this.hasMore.set(body.hasMore);
    this.loaded.set(true);
  }

  private async run(operation: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await operation();
      this.signedOut.set(false);
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      this.signedOut.set(status === 401);
      this.error.set(
        status === 401
          ? 'Войдите в аккаунт, чтобы открыть предложения.'
          : status === 403
            ? 'Подтвердите Email, чтобы отправлять предложения.'
            : status === 404
              ? 'Предложение больше недоступно.'
              : status === 409
                ? 'Предложение изменилось. Скопируйте свои правки и откройте его заново.'
                : errorMessage(error),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
