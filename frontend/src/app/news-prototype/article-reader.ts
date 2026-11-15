import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

export interface ReaderStory {
  id: number;
  title: string;
  summary: string;
  category: string;
  tag: string;
  date: string;
  time: string;
}

// Fictional content for visual review; no publication API or SSR contract is implemented here.
@Component({
  selector: 'app-article-reader',
  imports: [RouterLink],
  templateUrl: './article-reader.html',
  styleUrl: './article-reader.css',
})
export class ArticleReader {
  readonly story = input.required<ReaderStory>();
  readonly stories = input.required<ReaderStory[]>();
  readonly open = output<number>();
  readonly category = output<string>();
  readonly tag = output<string>();
  readonly related = computed(() => {
    const current = this.story();
    const others = this.stories().filter((story) => story.id !== current.id);
    return [
      ...others.filter((story) => story.tag === current.tag),
      ...others.filter((story) => story.tag !== current.tag && story.category === current.category),
    ].slice(0, 5);
  });
  readonly paragraphs = computed(() => {
    const category = this.story().category;
    if (category === 'Транспорт')
      return [
        'В Белогорске готовят изменения в работе городского транспорта. На отдельных участках маршруты будут проходить по соседним улицам, а у временных остановок появятся указатели.',
        'Перед поездкой стоит проверить схему движения и заложить немного больше времени на дорогу. Пешеходные подходы к остановкам сохранят, чтобы пересадки оставались удобными.',
        'Обновлённую схему разместят на остановках. После завершения работ транспорт вернётся к привычному маршруту.',
      ];
    if (category === 'Культура')
      return [
        'В Белогорске готовят новую встречу для тех, кто интересуется историей города. В программе собраны фотографии, рассказы жителей и небольшие детали повседневной жизни, которые редко попадают в большие хроники.',
        'Организаторы предлагают посмотреть на знакомые места глазами разных поколений. Часть материалов жители принесли из семейных архивов, сохранив подписи и воспоминания.',
        'Посетители смогут познакомиться с материалами и поделиться собственными историями. Подробности программы и расписание будут доступны на площадке мероприятия.',
      ];
    return [
      'В Белогорске продолжают приводить в порядок общественные пространства. На новом участке появились удобные дорожки, места для отдыха и озеленение. Теперь здесь можно пройти вдоль реки и остановиться у воды.',
      'При подготовке проекта учитывали повседневные маршруты жителей. Дорожки связывают соседние кварталы, а скамейки расположены так, чтобы можно было выбрать место на солнце или в тени.',
      'Работы продолжатся на соседних участках. После их завершения отдельные прогулочные зоны соединятся в один маршрут, доступный для неспешных прогулок и коротких поездок по городу.',
    ];
  });
  formatDate(value: string): string {
    return new Date(value).toLocaleDateString('ru-RU', {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC',
    });
  }
}
