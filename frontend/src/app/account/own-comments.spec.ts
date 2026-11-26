import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiWrite } from '../api-write';
import { Api } from '../api/api';
import { deleteOwnComment, listOwnComments } from '../api/functions';
import { CityClock } from '../staff/city-clock';
import { OwnComments } from './own-comments';

describe('My Comments', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('lets the owner delete a listed comment without opening its article', async () => {
    const invoke = vi
      .fn()
      .mockResolvedValueOnce({
        items: [
          {
            id: 'hidden-comment',
            body: 'Private owner text',
            createdAtUtc: '2026-11-25',
            articleUrl: null,
          },
          {
            id: 'public-comment',
            body: 'Public article comment',
            createdAtUtc: '2026-11-24',
            articleUrl: '/news/public-article',
          },
        ],
        canDelete: true,
        hasMore: false,
      })
      .mockResolvedValueOnce({
        items: [
          {
            id: 'public-comment',
            body: 'Public article comment',
            createdAtUtc: '2026-11-24',
            articleUrl: '/news/public-article',
          },
        ],
        canDelete: true,
        hasMore: false,
      });
    const write = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: Api, useValue: { invoke } },
        { provide: ApiWrite, useValue: { invoke: write } },
        { provide: CityClock, useValue: { format: (value: string) => value } },
      ],
    });
    const fixture = TestBed.createComponent(OwnComments);
    const host: HTMLElement = fixture.nativeElement;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(invoke).toHaveBeenCalledWith(listOwnComments, { page: 1 });
    expect(host.textContent).toContain('Private owner text');
    const [withdrawn, published] = host.querySelectorAll('li');
    expect(withdrawn.textContent).toContain('Статья скрыта');
    expect(withdrawn.querySelector('[role="link"][aria-disabled="true"]')?.textContent).toContain(
      'Открыть статью',
    );
    expect(withdrawn.querySelector('a[href]')).toBeNull();
    expect(published.querySelector('a')?.getAttribute('href')).toBe('/news/public-article');
    expect(published.textContent).not.toContain('Статья скрыта');
    fixture.debugElement.query(By.css('li button')).triggerEventHandler('click');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(write).not.toHaveBeenCalled();
    fixture.debugElement.query(By.css('li .actions button')).triggerEventHandler('click');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(write).toHaveBeenCalledExactlyOnceWith(deleteOwnComment, { id: 'hidden-comment' });
    expect(host.textContent).not.toContain('Private owner text');
    expect(host.textContent).toContain('Public article comment');
  });
});
