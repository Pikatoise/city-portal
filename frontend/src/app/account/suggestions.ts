import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiWrite } from '../api-write';
import { createSuggestion } from '../api/fn/suggestions/create-suggestion';
import { getMySuggestion } from '../api/fn/suggestions/get-my-suggestion';
import { listMySuggestions } from '../api/fn/suggestions/list-my-suggestions';
import { updateMySuggestion } from '../api/fn/suggestions/update-my-suggestion';
import { uploadSuggestionImage } from '../api/fn/suggestions/upload-suggestion-image';
import { ImageResponse } from '../api/models/image-response';
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
  private readonly write = inject(ApiWrite);
  private readonly account = inject(AccountApi);
  protected readonly clock = inject(CityClock);
  protected readonly items = signal<SuggestionResponse[]>([]);
  protected readonly images = signal<ImageResponse[]>([]);
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
      this.images.set(body.images);
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
    this.images.set([]);
    this.title = '';
    this.description = '';
    this.eventDate = '';
    this.location = '';
    this.contact = '';
    this.error.set('');
    this.notice.set('');
  }

  protected async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = [...(input.files ?? [])];
    input.value = '';
    if (!files.length) return;
    if (
      files.length + this.images().length > 5 ||
      files.some((file) => file.size > 10 * 1024 * 1024)
    ) {
      this.error.set('Выберите до пяти изображений размером до 10 МиБ каждое.');
      return;
    }
    await this.run(async () => {
      for (const file of files) {
        const image = await this.write.invoke(uploadSuggestionImage, { body: { file } });
        this.images.update((items) => [...items, image]);
      }
    });
  }

  protected removeImage(id: string): void {
    this.images.update((images) => images.filter((image) => image.id !== id));
  }

  protected imageUrl(id: string): string | undefined {
    const selected = this.selected();
    return selected?.images.some((image) => image.id === id && image.state === 'Ready')
      ? `/api/account/suggestions/${selected.id}/images/${id}`
      : undefined;
  }

  protected async save(): Promise<void> {
    const title = this.title.trim();
    const description = this.description.trim();
    if (!title || !description) {
      this.error.set('Заполните заголовок и описание.');
      return;
    }

    await this.run(async () => {
      const details = {
        title,
        description,
        eventDate: this.eventDate || null,
        location: this.location.trim() || null,
        contact: this.contact.trim() || null,
        imageIds: this.images().map((image) => image.id),
      };
      const current = this.selected();
      if (current) {
        await this.write.invoke(updateMySuggestion, {
          id: current.id,
          body: { details, version: current.version },
        });
      } else {
        await this.write.invoke(createSuggestion, { body: details });
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
