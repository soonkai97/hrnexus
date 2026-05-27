import { Component, inject, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AppUpdateService } from 'shared-lib';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: '<router-outlet />',
})
export class App implements OnInit {
  private readonly appUpdate = inject(AppUpdateService);

  ngOnInit(): void {
    // Init here (not only in ShellComponent) so the SW update check fires
    // on every page load — including the login page — and the new version
    // activates as soon as it finishes downloading, regardless of auth state.
    this.appUpdate.init();
  }
}
