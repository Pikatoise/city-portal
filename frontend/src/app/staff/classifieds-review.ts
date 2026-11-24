import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { errorMessage } from '../account/account-api';
import { ApiWrite } from '../api-write';
import { Api } from '../api/api';
import {
  getListingReview,
  listListingReviews,
  openListingReviewPhoto,
  rejectListingReview,
  returnListingReview,
} from '../api/functions';
import { ListingReviewPreview, ListingReviewQueueItem } from '../api/models';
import { CityClock } from './city-clock';

@Component({
  selector: 'app-classifieds-review',
  imports: [FormsModule, RouterLink],
  templateUrl: './classifieds-review.html',
  styleUrl: './classifieds-review.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClassifiedsReview implements OnInit {
  private readonly api = inject(Api);
  private readonly write = inject(ApiWrite);
  protected readonly clock = inject(CityClock);
  protected readonly items = signal<ListingReviewQueueItem[]>([]);
  protected readonly preview = signal<ListingReviewPreview | null>(null);
  protected readonly page = signal(1);
  protected readonly hasNext = signal(false);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected reason = '';

  ngOnInit(): void {
    void this.load(1);
  }

  protected async load(page: number): Promise<void> {
    await this.run(() => this.readPage(page));
  }

  protected async open(id: string): Promise<void> {
    await this.run(async () => {
      this.preview.set(await this.api.invoke(getListingReview, { id }));
    });
  }

  protected close(): void {
    this.preview.set(null);
    this.reason = '';
  }

  protected imageUrl(preview: ListingReviewPreview, imageId: string): string {
    const { id, version } = preview.submission;
    const path = openListingReviewPhoto.PATH.replace('{id}', id).replace('{imageId}', imageId);
    return this.api.rootUrl + path + '?version=' + version;
  }

  protected async decide(reject: boolean): Promise<void> {
    const preview = this.preview();
    const reason = this.reason.trim();
    if (!preview || !reason || reason.length > 500) return;
    if (reject && !window.confirm('Отклонить объявление окончательно?')) return;
    await this.run(async () => {
      const { id, version } = preview.submission;
      const operation = reject ? rejectListingReview : returnListingReview;
      await this.write.invoke(operation, { id, body: { version, reason } });
      this.close();
      this.items.update((items) => items.filter((item) => item.id !== id));
      this.notice.set(reject ? 'Объявление отклонено.' : 'Объявление возвращено на доработку.');
      await this.readPage(1);
    });
  }

  private async readPage(page: number): Promise<void> {
    const response = await this.api.invoke(listListingReviews, { page });
    this.items.set(response.items);
    this.hasNext.set(response.hasNextPage);
    this.page.set(page);
    this.loaded.set(true);
  }

  private async run(operation: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await operation();
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      if (status === 401 || status === 403) {
        this.close();
        this.items.set([]);
        this.loaded.set(false);
      }
      if (status === 404 || status === 409) {
        this.close();
        this.error.set('Заявка больше недоступна для проверки. Обнови очередь.');
      } else {
        this.error.set(errorMessage(error));
      }
    } finally {
      this.busy.set(false);
    }
  }
}
