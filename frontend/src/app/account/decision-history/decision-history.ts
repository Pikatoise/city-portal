import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, linkedSignal, resource } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { Api } from '../../api/api';
import {
  getAccountBlockHistory,
  getListingModerationHistory,
  getMyAccountBlockHistory,
  getMyListingHistory,
} from '../../api/functions';
import { AccountBlockHistory, ClassifiedsPageOfListingHistoryItem } from '../../api/models';
import { CityClock } from '../../staff/city-clock';
import { errorMessage } from '../account-api';

@Component({
  imports: [RouterLink],
  selector: 'app-decision-history',
  styleUrl: './decision-history.css',
  templateUrl: './decision-history.html',
})
export class DecisionHistory {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly id = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('id') ?? '')),
    {
      initialValue: '',
    },
  );
  protected readonly staff = this.route.snapshot.data['staff'] === true;
  protected readonly listings = this.route.snapshot.data['listings'] === true;
  protected readonly clock = inject(CityClock);
  protected readonly page = linkedSignal({ source: this.id, computation: () => 1 });
  protected readonly history = resource({
    params: () => ({ id: this.id(), page: this.page() }),
    loader: ({
      params: { id, page },
    }): Promise<AccountBlockHistory | ClassifiedsPageOfListingHistoryItem> =>
      this.listings
        ? this.staff
          ? this.api.invoke(getListingModerationHistory, { id: id || undefined, page })
          : this.api.invoke(getMyListingHistory, { id, page })
        : this.staff
          ? this.api.invoke(getAccountBlockHistory, { id, page })
          : this.api.invoke(getMyAccountBlockHistory, { page }),
  });
  protected readonly rows = computed(() =>
    this.history.hasValue() ? this.history.value().items : [],
  );
  protected readonly hasNextPage = computed(
    () => this.history.hasValue() && this.history.value().hasNextPage,
  );
  private readonly errorStatus = computed(() => {
    const error = this.history.error();
    return error instanceof HttpErrorResponse ? error.status : undefined;
  });
  protected readonly signedOut = computed(() => this.errorStatus() === 401);
  protected readonly error = computed(() => {
    const error = this.history.error();
    if (!error) return '';
    if (this.signedOut()) return 'Войди в аккаунт, чтобы открыть историю.';
    const status = this.errorStatus();
    return status === 403 || status === 404 ? 'История недоступна.' : errorMessage(error);
  });
  protected readonly listingOutcomes: Record<string, string> = {
    Approved: 'Одобрено',
    Returned: 'Возвращено на доработку',
    Rejected: 'Отклонено',
    Reviewed: 'Проверка правок завершена',
    Blocked: 'Скрыто модератором',
  };
}
