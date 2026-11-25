import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiWrite } from '../api-write';
import { Api } from '../api/api';
import {
  createListingDraft,
  deleteListingDraft,
  getMyListingDraft,
  listListingCatalog,
  listMyListingDrafts,
  openMyListingPhoto,
  recallListingDraft,
  saveListingDraft,
  submitListingDraft,
  uploadListingPhoto,
} from '../api/functions';
import { CatalogEntryResponse, ListingDraftResponse, ListingPhotoResponse } from '../api/models';
import { AccountApi, accountProblem, errorMessage } from './account-api';

const maximumPrice = 999_999_999_999;
const submissionErrors: Record<string, string> = {
  title: 'Заполни заголовок.',
  description: 'Заполни описание.',
  categoryId: 'Выбери категорию.',
  districtId: 'Выбери район.',
  price: 'Укажи цену в этой категории.',
  phone: 'Проверь номер телефона. Если телефона нет, укажи Email.',
  email: 'Проверь адрес Email.',
  photos: 'Дождись обработки фотографий или убери недоступные.',
};

@Component({
  imports: [FormsModule, RouterLink],
  selector: 'app-classifieds',
  styleUrl: './classifieds.css',
  templateUrl: './classifieds.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Classifieds implements OnInit {
  private readonly api = inject(Api);
  private readonly write = inject(ApiWrite);
  private readonly account = inject(AccountApi);

  protected readonly items = signal<ListingDraftResponse[]>([]);
  protected readonly categories = signal<CatalogEntryResponse[]>([]);
  protected readonly districts = signal<CatalogEntryResponse[]>([]);
  protected readonly current = signal<ListingDraftResponse | null>(null);
  protected readonly inReview = computed(() => this.current()?.state === 'InReview');
  protected readonly rejected = computed(() => this.current()?.state === 'Rejected');
  protected readonly published = computed(() =>
    ['Published', 'Hidden', 'Blocked'].includes(this.current()?.state ?? ''),
  );
  protected readonly accountBlocked = signal(false);
  protected readonly readOnly = computed(
    () => this.accountBlocked() || this.inReview() || this.rejected() || this.published(),
  );
  protected readonly photos = signal<ListingPhotoResponse[]>([]);
  protected readonly photoIndex = signal(0);
  protected readonly photo = computed(() => this.photos()[this.photoIndex()]);
  protected readonly page = signal(1);
  protected readonly hasNext = signal(false);
  protected readonly ready = signal(false);
  protected readonly busy = signal(false);
  protected readonly denied = signal<401 | 403 | null>(null);
  protected readonly message = signal('');
  protected readonly error = signal('');
  protected readonly maximumPrice = maximumPrice;
  protected dirty = false;
  protected title = '';
  protected description = '';
  protected category = '';
  protected district = '';
  protected price: number | null = null;
  protected phone = '';
  protected email = '';

  ngOnInit(): void {
    void this.load();
  }

  canLeave(): boolean {
    return !this.dirty || window.confirm('Закрыть черновик без сохранения правок?');
  }

  protected async load(): Promise<void> {
    await this.run(async () => {
      const profile = await this.account.profile();
      this.accountBlocked.set(!!profile.blockReason);
      if (!profile.emailConfirmed) {
        this.denied.set(403);
        return;
      }
      const [categories, districts] = await Promise.all([
        this.readCatalog('categories'),
        this.readCatalog('districts'),
        this.readPage(1),
      ]);
      this.categories.set(categories);
      this.districts.set(districts);
      this.denied.set(null);
      this.ready.set(true);
    });
  }

  protected async showPage(page: number): Promise<void> {
    await this.run(() => this.readPage(page));
  }

  protected async open(id: string): Promise<void> {
    if (!this.canLeave()) return;
    await this.run(async () => this.edit(await this.api.invoke(getMyListingDraft, { id })));
  }

  protected newDraft(): void {
    if (!this.accountBlocked() && this.canLeave()) this.edit(null);
  }

  protected async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = [...(input.files ?? [])];
    input.value = '';
    if (!files.length) return;
    if (
      files.length + this.photos().length > 5 ||
      files.some((file) => file.size > 10 * 1024 * 1024)
    ) {
      this.error.set('Можно прикрепить до пяти фотографий размером до 10 МиБ каждая.');
      return;
    }
    await this.run(async () => {
      const first = this.photos().length;
      for (const file of files) {
        const image = await this.write.invoke(uploadListingPhoto, { body: { file } });
        this.photos.update((photos) => [...photos, { image, alternativeText: '' }]);
        this.photoIndex.set(first);
        this.dirty = true;
      }
    });
  }

  protected changePhoto(offset: number): void {
    this.photoIndex.update(
      (index) => (index + offset + this.photos().length) % this.photos().length,
    );
  }

  protected updateAlternativeText(text: string): void {
    this.photos.update((photos) =>
      photos.map((photo, index) =>
        index === this.photoIndex() ? { ...photo, alternativeText: text } : photo,
      ),
    );
    this.dirty = true;
  }

  protected removePhoto(index: number): void {
    this.photos.update((photos) => photos.filter((_, position) => position !== index));
    this.photoIndex.set(Math.max(0, Math.min(index, this.photos().length - 1)));
    this.dirty = true;
  }

  protected imageUrl(photo: ListingPhotoResponse): string | null {
    const draft = this.current();
    if (!draft || photo.image.state !== 'Ready') return null;
    const path = openMyListingPhoto.PATH.replace('{id}', draft.id).replace(
      '{imageId}',
      photo.image.id,
    );
    return this.api.rootUrl + path;
  }

  protected async refreshPhotos(): Promise<void> {
    const draft = this.current();
    if (!draft) return;
    await this.run(async () => {
      const latest = await this.api.invoke(getMyListingDraft, { id: draft.id });
      const images = new Map(latest.photos?.map((photo) => [photo.image.id, photo.image]));
      this.photos.update((photos) =>
        photos.map((photo) => ({ ...photo, image: images.get(photo.image.id) ?? photo.image })),
      );
    });
  }

  protected async save(): Promise<void> {
    if (this.readOnly()) return;
    if (
      this.price !== null &&
      (!Number.isSafeInteger(this.price) || this.price < 0 || this.price > maximumPrice)
    ) {
      this.error.set('Укажи цену целым числом от 0 до 999 999 999 999.');
      return;
    }
    const missingText = this.photos().findIndex((photo) => !photo.alternativeText.trim());
    if (missingText >= 0) {
      this.photoIndex.set(missingText);
      this.error.set('Добавь альтернативный текст для каждой фотографии.');
      return;
    }
    await this.run(async () => {
      const details = {
        title: this.title,
        description: this.description,
        categoryId: this.category || null,
        districtId: this.district || null,
        price: this.price,
        phone: this.phone,
        email: this.email,
        photos: this.photos().map((photo) => ({
          imageId: photo.image.id,
          alternativeText: photo.alternativeText.trim(),
        })),
      };
      const draft = this.current();
      const saved = draft
        ? await this.write.invoke(saveListingDraft, {
            id: draft.id,
            body: { version: draft.version, details },
          })
        : await this.write.invoke(createListingDraft, { body: details });
      this.edit(saved);
      this.message.set('Черновик сохранён.');
      await this.readPage(1);
    });
  }

  protected async removeDraft(): Promise<void> {
    const draft = this.current();
    if (
      !draft ||
      this.inReview() ||
      this.published() ||
      this.accountBlocked() ||
      !window.confirm('Удалить объявление и его фотографии?')
    )
      return;
    await this.run(async () => {
      await this.write.invoke(deleteListingDraft, { id: draft.id, version: draft.version });
      this.edit(null);
      this.message.set('Объявление удалено.');
      await this.readPage(1);
    });
  }

  protected async toggleReview(): Promise<void> {
    const draft = this.current();
    if (!draft || this.dirty || this.rejected() || this.published() || this.accountBlocked())
      return;
    await this.run(async () => {
      const recalling = this.inReview();
      const operation = recalling ? recallListingDraft : submitListingDraft;
      const saved = await this.write.invoke(operation, {
        id: draft.id,
        body: { version: draft.version },
      });
      this.edit(saved);
      this.message.set(
        recalling
          ? 'Объявление отозвано. Черновик можно редактировать.'
          : 'Объявление отправлено на проверку.',
      );
      await this.readPage(1);
    });
  }

  protected hasCategory(id: string): boolean {
    return this.categories().some((entry) => entry.id === id);
  }

  private edit(draft: ListingDraftResponse | null): void {
    this.current.set(draft);
    const fields = draft?.details;
    this.title = fields?.title ?? '';
    this.description = fields?.description ?? '';
    this.category = fields?.categoryId ?? '';
    this.district = fields?.districtId ?? '';
    this.price = fields?.price == null ? null : Number(fields.price);
    this.phone = fields?.phone ?? '';
    this.email = fields?.email ?? '';
    this.photos.set(draft?.photos ?? []);
    this.photoIndex.set(0);
    this.dirty = false;
    this.error.set('');
    this.message.set('');
  }

  private async readPage(page: number): Promise<void> {
    const response = await this.api.invoke(listMyListingDrafts, { page });
    this.items.set(response.items);
    this.hasNext.set(response.hasNextPage);
    this.page.set(page);
  }

  private async readCatalog(kind: string): Promise<CatalogEntryResponse[]> {
    const entries: CatalogEntryResponse[] = [];
    for (let page = 1; ; page++) {
      const response = await this.api.invoke(listListingCatalog, { kind, page });
      entries.push(...response.items);
      if (!response.hasNextPage) return entries;
    }
  }

  private async run(operation: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    try {
      await operation();
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      if (status === 401 || status === 403) {
        this.ready.set(false);
        this.denied.set(status);
      } else {
        const problem = accountProblem(error);
        const validation = Object.keys(problem.errors ?? {})
          .map((field) => submissionErrors[field])
          .filter(Boolean)
          .join(' ');
        this.error.set(
          problem.code === 'listing_not_ready' && validation
            ? validation
            : status === 409
              ? 'Черновик изменился. Скопируй свои правки и открой его заново.'
              : errorMessage(error),
        );
      }
    } finally {
      this.busy.set(false);
    }
  }
}
