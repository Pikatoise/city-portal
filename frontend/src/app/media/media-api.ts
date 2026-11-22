import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, firstValueFrom, map } from 'rxjs';
import { StrictHttpResponse } from '../api/strict-http-response';
import { ApiWrite } from '../api-write';
import { listImages } from '../api/fn/media/list-images';
import { listTemplates } from '../api/fn/media/list-templates';
import { uploadImage } from '../api/fn/media/upload-image';
import { createTemplate } from '../api/fn/media/create-template';
import { templateAction } from '../api/fn/media/template-action';
import { templateHistory } from '../api/fn/media/template-history';
import { requestTemplateRemoval } from '../api/fn/media/request-template-removal';
import { templateRemovalQueue } from '../api/fn/media/template-removal-queue';
import { resolveTemplateRemoval } from '../api/fn/media/resolve-template-removal';
import { TemplateResponse } from '../api/models/template-response';
import { RemovalRequestResponse } from '../api/models/removal-request-response';
import { accountProblem, errorMessage } from '../account/account-api';
export type { ImageResponse } from '../api/models/image-response';
export type { TemplateResponse } from '../api/models/template-response';
export type { TemplateHistoryResponse } from '../api/models/template-history-response';
export type { RemovalRequestResponse } from '../api/models/removal-request-response';

@Injectable({ providedIn: 'root' })
export class MediaApi {
  private readonly http = inject(HttpClient);
  private readonly write = inject(ApiWrite);
  private request<T>(response: Observable<StrictHttpResponse<T>>): Promise<T> {
    return firstValueFrom(response.pipe(map((value) => value.body)));
  }
  images(page = 1) {
    return this.request(listImages(this.http, '', { page, pageSize: 20 }));
  }
  templates(scope: string, page = 1) {
    return this.request(listTemplates(this.http, '', { scope, page, pageSize: 20 }));
  }
  upload(file: File) {
    return this.write.invoke(uploadImage, { body: { file } });
  }
  propose(imageId: string, name: string) {
    return this.write.invoke(createTemplate, { body: { imageId, name } });
  }
  act(template: TemplateResponse, operation: string, reason = '', imageId?: string, name?: string) {
    return this.write.invoke(templateAction, {
      id: template.id,
      operation,
      body: { version: template.version, reason, imageId, name },
    });
  }
  history(id: string, page = 1) {
    return this.request(templateHistory(this.http, '', { id, page, pageSize: 20 }));
  }
  requestRemoval(id: string) {
    return this.write.invoke(requestTemplateRemoval, { id });
  }
  removals(page = 1) {
    return this.request(templateRemovalQueue(this.http, '', { page, pageSize: 20 }));
  }
  resolve(request: RemovalRequestResponse) {
    return this.write.invoke(resolveTemplateRemoval, {
      id: request.id,
      body: { version: request.version },
    });
  }
}

export function mediaError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 401) return 'Войди в аккаунт, чтобы открыть медиатеку.';
    if (error.status === 403)
      return 'Для этого действия нужна соответствующая роль и подтверждённый Email.';
    if (error.status === 404) return 'Изображение или шаблон недоступны.';
    if (error.status === 409)
      return 'Данные изменились или действие больше недоступно. Обнови список и повтори.';
    if (error.status === 413) return 'Файл слишком большой. Максимальный размер: 10 МиБ.';
    if (error.status === 415)
      return 'Нужен JPEG, PNG или статический WebP с правильным типом файла.';
    if (error.status === 503) return 'Медиатека временно недоступна. Попробуй позже.';
  }
  const code = accountProblem(error).code;
  if (code === 'image_name')
    return 'Переименуй файл: название должно содержать от 1 до 200 символов.';
  if (code === 'template_name') return 'Укажи название шаблона, не более 100 символов.';
  if (code === 'image_dimensions') return 'Изображение должно содержать не более 40 мегапикселей.';
  if (code === 'reason_required') return 'Укажи причину, не более 2000 символов.';
  return errorMessage(error);
}
