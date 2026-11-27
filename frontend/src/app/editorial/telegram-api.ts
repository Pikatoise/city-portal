import { inject, Injectable } from '@angular/core';
import { Api } from '../api/api';
import { ApiWrite } from '../api-write';
import { getTelegramAnnouncement } from '../api/fn/telegram-announcements/get-telegram-announcement';
import { listTelegramRecovery } from '../api/fn/telegram-announcements/list-telegram-recovery';
import { queueTelegramAnnouncement } from '../api/fn/telegram-announcements/queue-telegram-announcement';
import { resolveTelegramAnnouncement } from '../api/fn/telegram-announcements/resolve-telegram-announcement';
import { suggestTelegramAnnouncement } from '../api/fn/telegram-announcements/suggest-telegram-announcement';
import { ResolveTelegramAnnouncementRequest } from '../api/models/resolve-telegram-announcement-request';

@Injectable({ providedIn: 'root' })
export class TelegramApi {
  private readonly api = inject(Api);
  private readonly write = inject(ApiWrite);

  get(id: string, channel?: string) {
    return this.api.invoke(getTelegramAnnouncement, { id, channel });
  }

  recovery() {
    return this.api.invoke(listTelegramRecovery);
  }

  queue(id: string, revisionId: string, channel: string, text: string) {
    return this.write.invoke(queueTelegramAnnouncement, {
      id,
      body: { revisionId, channel, text },
    });
  }

  suggest(id: string, revisionId: string) {
    return this.write.invoke(suggestTelegramAnnouncement, { id, body: { revisionId } });
  }

  resolve(id: string, body: ResolveTelegramAnnouncementRequest) {
    return this.write.invoke(resolveTelegramAnnouncement, { id, body });
  }
}
