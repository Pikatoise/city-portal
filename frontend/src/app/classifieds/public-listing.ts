import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  OnInit,
  PendingTasks,
  RESPONSE_INIT,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { Api } from '../api/api';
import { getPublicListing, openPublicListingPhoto } from '../api/functions';
import { PublicListingResponse } from '../api/models';

import { ReportForm } from '../reports/report-form';

@Component({
  selector: 'app-public-listing',
  imports: [RouterLink, ReportForm],
  templateUrl: './public-listing.html',
  styleUrl: './public-listing.css',
})
export class PublicListing implements OnInit {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly response = inject(RESPONSE_INIT, { optional: true });
  private readonly pending = inject(PendingTasks);
  private readonly destroy = inject(DestroyRef);
  private readonly title = inject(Title);
  private generation = 0;
  protected readonly listing = signal<PublicListingResponse | null>(null);
  protected readonly state = signal<'loading' | 'missing' | 'failed' | 'ready'>('loading');
  protected readonly photoIndex = signal(0);
  protected readonly photo = computed(() => this.listing()?.photos[this.photoIndex()]);

  ngOnInit(): void {
    const subscription = this.route.paramMap.subscribe((params) => {
      const complete = this.pending.add();
      void this.load(params.get('id') ?? '').finally(complete);
    });
    this.destroy.onDestroy(() => {
      this.generation++;
      subscription.unsubscribe();
    });
  }

  protected changePhoto(offset: number): void {
    const count = this.listing()?.photos.length ?? 0;
    if (count > 0) this.photoIndex.update((index) => (index + offset + count) % count);
  }

  protected photoUrl(listing: PublicListingResponse, imageId: string): string {
    return openPublicListingPhoto.PATH.replace('{id}', listing.id).replace('{imageId}', imageId);
  }

  private async load(id: string): Promise<void> {
    const generation = ++this.generation;
    this.state.set('loading');
    this.listing.set(null);
    this.photoIndex.set(0);
    if (this.response)
      this.response.headers = {
        'Cache-Control': 'no-store',
        'Content-Type': 'text/html; charset=utf-8',
      };
    try {
      const result = await this.api.invoke(getPublicListing, { id });
      if (generation !== this.generation) return;
      this.listing.set(result);
      this.state.set('ready');
      this.title.setTitle(result.title);
      if (this.response) this.response.status = 200;
    } catch (error) {
      if (generation !== this.generation) return;
      const missing = error instanceof HttpErrorResponse && error.status === 404;
      this.state.set(missing ? 'missing' : 'failed');
      this.title.setTitle(missing ? 'Страница не найдена' : 'Объявление недоступно');
      if (this.response) this.response.status = missing ? 404 : 503;
    }
  }
}
