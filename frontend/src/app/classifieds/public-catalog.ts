import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  PendingTasks,
  RESPONSE_INIT,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { Api } from '../api/api';
import { getPublicListingFilters, listPublicListings } from '../api/functions';
import { ClassifiedsPageOfPublicListingSummary, PublicListingFilters } from '../api/models';
import { SiteMetadata } from '../site-metadata';

@Component({
  selector: 'app-public-catalog',
  imports: [FormsModule, RouterLink],
  templateUrl: './public-catalog.html',
  styleUrl: './public-catalog.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PublicCatalog {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly pending = inject(PendingTasks);
  private readonly response = inject(RESPONSE_INIT, { optional: true });
  private generation = 0;
  protected readonly result = signal<ClassifiedsPageOfPublicListingSummary | null>(null);
  protected readonly filters = signal<PublicListingFilters>({ categories: [], districts: [] });
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly page = signal(1);
  protected query = '';
  protected categoryId = '';
  protected districtId = '';
  protected minPrice: number | null = null;
  protected maxPrice: number | null = null;

  constructor() {
    inject(Title).setTitle('Объявления · ' + inject(SiteMetadata).name());
    const subscription = this.route.queryParamMap.subscribe((params) => this.load(params));
    inject(DestroyRef).onDestroy(() => {
      this.generation++;
      subscription.unsubscribe();
    });
  }

  protected search(): void {
    void this.router.navigate(['/classifieds'], {
      queryParams: {
        query: this.query.trim() || null,
        categoryId: this.categoryId || null,
        districtId: this.districtId || null,
        minPrice: this.minPrice,
        maxPrice: this.maxPrice,
      },
    });
  }

  protected reset(): void {
    this.query = '';
    this.categoryId = '';
    this.districtId = '';
    this.minPrice = null;
    this.maxPrice = null;
    this.search();
  }

  protected retry(): void {
    this.load(this.route.snapshot.queryParamMap);
  }

  protected priceLabel(price: number | string): string {
    return Number(price) === 0 ? 'Бесплатно' : Number(price).toLocaleString('ru-RU');
  }

  private load(params: ParamMap): void {
    void this.pending.run(() => this.read(params));
  }

  private async read(params: ParamMap): Promise<void> {
    const generation = ++this.generation;
    this.loading.set(true);
    this.result.set(null);
    this.error.set('');
    if (this.response) {
      this.response.status = 200;
      this.response.headers = {
        'Cache-Control': 'no-store',
        'Content-Type': 'text/html; charset=utf-8',
      };
    }
    const request = {
      Query: params.get('query') || undefined,
      CategoryId: params.get('categoryId') || undefined,
      DistrictId: params.get('districtId') || undefined,
      MinPrice: params.get('minPrice') ?? undefined,
      MaxPrice: params.get('maxPrice') ?? undefined,
      Page: params.get('page') ?? undefined,
    };
    this.query = request.Query ?? '';
    this.categoryId = request.CategoryId ?? '';
    this.districtId = request.DistrictId ?? '';
    this.minPrice = this.priceInput(request.MinPrice);
    this.maxPrice = this.priceInput(request.MaxPrice);
    this.page.set(Number(request.Page ?? 1));

    try {
      const [, result] = await Promise.all([
        this.api.invoke(getPublicListingFilters).then((filters) => {
          if (generation === this.generation) this.filters.set(filters);
        }),
        this.api.invoke(listPublicListings, request),
      ]);
      if (generation === this.generation) this.result.set(result);
    } catch (error) {
      if (generation !== this.generation) return;
      const invalid = error instanceof HttpErrorResponse && error.status === 400;
      this.error.set(
        invalid
          ? 'Проверь поисковый запрос, фильтры и номер страницы.'
          : 'Не удалось загрузить объявления.',
      );
      if (this.response) this.response.status = invalid ? 400 : 503;
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }

  private priceInput(value: string | undefined): number | null {
    return value && /^\d+$/.test(value) ? Number(value) : null;
  }
}
