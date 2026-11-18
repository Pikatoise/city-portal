export const articleState = (state: string): string =>
  ({
    Draft: 'Черновик',
    Deleted: 'Удалено автором',
    InReview: 'На проверке',
    Approved: 'Одобрено',
    FinalRejected: 'Окончательно отклонено',
    Published: 'Опубликовано',
    Withdrawn: 'Снято с публикации',
    Superseded: 'Закрыто',
  })[state] ?? state;

export const reviewAction = (action: string): string =>
  ({
    Created: 'Создан черновик',
    CreatedFromSuggestion: 'Черновик создан по предложению жителя',
    Deleted: 'Статья удалена',
    submit: 'Отправлено на проверку',
    recall: 'Отозвано автором',
    claim: 'Назначен редактор',
    release: 'Возвращено в очередь',
    takeover: 'Проверка передана',
    return: 'Возвращено на доработку',
    reject: 'Окончательно отклонено',
    approve: 'Одобрено',
    publish: 'Опубликовано',
    propose: 'Создано предложение изменений',
    'working-version': 'Создано предложение изменений',
    withdraw: 'Снято с публикации',
    correct: 'Редакторское исправление',
    supersede: 'Рабочая версия закрыта',
    delete: 'Статья удалена',
    'roles-released': 'Назначение снято после изменения ролей',
  })[action] ?? action;
