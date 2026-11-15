import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

// Selected homepage header.
@Component({
  selector: 'app-home-top',
  imports: [FormsModule, RouterLink],
  templateUrl: './home-top.html',
  styleUrl: './home-top.css',
})
export class HomeTop {
  readonly name = input.required<string>();
  readonly city = input('Белогорск');
  readonly categories = input.required<string[]>();
  readonly showCategories = input(true);
  readonly search = output<string>();
  readonly category = output<string>();
  readonly allNews = output<void>();
  readonly today = new Date().toLocaleDateString('ru-RU', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  });
  searchText = '';
}
