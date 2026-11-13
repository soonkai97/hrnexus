import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { UpperCasePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AuthService, LeaveService, LeaveBalance } from 'shared-lib';
import { Card } from 'primeng/card';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [UpperCasePipe, TranslatePipe, Card],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent implements OnInit {
  private readonly auth     = inject(AuthService);
  private readonly leaveSvc = inject(LeaveService);

  loading = signal(true);

  leave = signal<LeaveBalance | null>(null);

  // Greeting
  user      = computed(() => this.auth.user());
  firstName = computed(() => this.user()?.fullName.split(' ')[0] ?? '');
  greeting  = computed(() => {
    const h = new Date().getHours();
    if (h < 12) return 'home.goodMorning';
    if (h < 17) return 'home.goodAfternoon';
    return 'home.goodEvening';
  });

  ngOnInit(): void {
    this.leaveSvc.getMyBalance().pipe(catchError(() => of(null))).subscribe(leave => {
      this.leave.set(leave);
      this.loading.set(false);
    });
  }
}
