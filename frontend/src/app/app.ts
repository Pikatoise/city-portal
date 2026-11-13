import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { InstallationState } from './installation-state';
import { SiteMetadata } from './site-metadata';

@Component({
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  protected readonly site = inject(SiteMetadata);
  protected readonly installation = inject(InstallationState);
}
