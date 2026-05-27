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
    this.appUpdate.init();
  }
}
