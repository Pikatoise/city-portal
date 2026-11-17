import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AccountApi } from '../account/account-api';
import { NewsApi } from './news-api';
import { Publications } from './publications';

describe('Editorial publications', () => {
  const api = {
    publications: vi.fn(),
    act: vi.fn(),
    homepage: vi.fn(),
    taxonomy: vi.fn(),
    saveHomepage: vi.fn(),
    feature: vi.fn(),
  };
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
  it('retains category order input when a stale homepage save fails', async () => {
    api.homepage.mockResolvedValue({ version: 3, firstCategoryId: 'a', secondCategoryId: 'b' });
    api.taxonomy.mockResolvedValue([
      { id: 'a', name: 'A', archived: false },
      { id: 'b', name: 'B', archived: false },
      { id: 'c', name: 'Archived', archived: true },
    ]);
    const fixture = TestBed.createComponent(Publications);
    fixture.detectChanges();
    await fixture.whenStable();
    await fixture.componentInstance.configureHomepage();
    const component = fixture.componentInstance;
    expect(component.categories().map((item) => item.id)).toEqual(['a', 'b']);
    component.firstCategoryId = 'b';
    component.secondCategoryId = 'a';
    api.saveHomepage.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 }));
    await component.saveHomepage();
    expect(api.saveHomepage).toHaveBeenCalledWith(3, 'b', 'a');
    expect(component.firstCategoryId).toBe('b');
    expect(component.secondCategoryId).toBe('a');
    expect(component.selection()?.version).toBe(3);
  });
  it('sends the article version when adding and removing editorial selections', async () => {
    const fixture = TestBed.createComponent(Publications);
    await fixture.componentInstance.ngOnInit();
    api.feature.mockResolvedValue({});
    await fixture.componentInstance.feature(row);
    expect(api.feature).toHaveBeenCalledWith(row.id, 7, true);
    await fixture.componentInstance.feature({ ...row, featured: true, version: 8 });
    expect(api.feature).toHaveBeenCalledWith(row.id, 8, false);
  });

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
