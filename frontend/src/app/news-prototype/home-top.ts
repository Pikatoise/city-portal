import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

// Three new studies of the area before news; the same B news layout follows each.
@Component({
  selector: 'app-home-top',
  imports: [FormsModule, RouterLink],
  templateUrl: './home-top.html',
  styleUrl: './home-top.css',
})
export class HomeTop {
  readonly name = input.required<string>();
  readonly city = input('Белогорск');
  readonly design = input('A');
  readonly search = output<string>();
  readonly category = output<string>();
  readonly today = new Date().toLocaleDateString('ru-RU', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  });
  searchText = '';
}
