import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AccountApi } from '../account/account-api';
import { NewsApi } from './news-api';
import { Publications } from './publications';

describe('Editorial publications', () => {
  const api = { publications: vi.fn(), act: vi.fn() };
  const accounts = { profile: vi.fn() };
  const row = {
    id: 'article-id',
    version: 7,
    title: 'Public title',
    state: 'Published',
    updatedAtUtc: '',
  };
  beforeEach(() => {
    vi.resetAllMocks();
    accounts.profile.mockResolvedValue({ emailConfirmed: true, roles: ['Editor'] });
    api.publications.mockResolvedValue({ items: [row], total: 1 });
    api.act.mockResolvedValue({});
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: NewsApi, useValue: api },
        { provide: AccountApi, useValue: accounts },
      ],
    });
  });
  afterEach(() => TestBed.resetTestingModule());

  it('requires a reason and retains withdrawal input on a stale version', async () => {
    const fixture = TestBed.createComponent(Publications);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Опубликованные статьи');
    component.withdrawing.set(row);
    await component.withdraw();
    expect(api.act).not.toHaveBeenCalled();
    component.reason = 'Verified error';
    api.act.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 }));
    await component.withdraw();
    expect(api.act).toHaveBeenCalledWith(row.id, row.version, 'withdraw', 'Verified error');
    expect(component.reason).toBe('Verified error');
    expect(component.withdrawing()).toEqual(row);
    expect(component.error()).toContain('Данные изменились');
    await component.withdraw();
    expect(component.withdrawing()).toBeNull();
    expect(component.notice()).toContain('снят с публикации');
  });

  it('does not request publications without effective editorial permission', async () => {
    accounts.profile.mockResolvedValue({ emailConfirmed: true, roles: ['Author'] });
    const component = TestBed.createComponent(Publications).componentInstance;
    await component.ngOnInit();
    expect(component.allowed()).toBe(false);
    expect(api.publications).not.toHaveBeenCalled();
  });
});
