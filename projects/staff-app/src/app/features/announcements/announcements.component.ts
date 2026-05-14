import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnnouncementService, AnnouncementDto, ToastService } from 'shared-lib';

@Component({
  selector: 'app-announcements',
  standalone: true,
  imports: [CommonModule, DatePipe, TranslatePipe],
  templateUrl: './announcements.component.html',
  styleUrl: './announcements.component.scss',
})
export class AnnouncementsComponent implements OnInit {
  private readonly announcementSvc = inject(AnnouncementService);
  private readonly toast           = inject(ToastService);
  private readonly translate       = inject(TranslateService);

  items   = signal<AnnouncementDto[]>([]);
  loading = signal(false);

  unreadCount  = computed(() => this.items().filter(a => !a.isRead).length);
  hasUnread    = computed(() => this.unreadCount() > 0);

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.announcementSvc.getMine().subscribe({
      next:  list => { this.items.set(list); this.loading.set(false); },
      error: ()   => { this.toast.error('Failed to load announcements.'); this.loading.set(false); },
    });
  }

  markRead(a: AnnouncementDto): void {
    if (a.isRead) return;
    this.announcementSvc.markRead(a.id).subscribe();
    this.items.update(list => list.map(x => x.id === a.id ? { ...x, isRead: true } : x));
  }

  markAllRead(): void {
    const unread = this.items().filter(a => !a.isRead);
    unread.forEach(a => this.announcementSvc.markRead(a.id).subscribe());
    this.items.update(list => list.map(x => ({ ...x, isRead: true })));
    this.toast.success(this.translate.instant('announcements.markAllRead'));
  }
}
