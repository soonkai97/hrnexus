import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnnouncementService, AnnouncementDto, ToastService } from 'shared-lib';
import { Button } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { Skeleton } from 'primeng/skeleton';

@Component({
  selector: 'app-announcements',
  standalone: true,
  imports: [DatePipe, TranslatePipe, Button, Tag, Skeleton],
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
    // Optimistic UI — update signal immediately; silently retry on next page load if it fails
    this.items.update(list => list.map(x => x.id === a.id ? { ...x, isRead: true } : x));
    this.announcementSvc.markRead(a.id).subscribe({
      error: () => {
        // Revert the optimistic update so the user can try again
        this.items.update(list => list.map(x => x.id === a.id ? { ...x, isRead: false } : x));
        this.toast.error('Failed to mark as read.');
      },
    });
  }

  markAllRead(): void {
    const unread = this.items().filter(a => !a.isRead);
    this.items.update(list => list.map(x => ({ ...x, isRead: true })));
    this.toast.success(this.translate.instant('announcements.markAllRead'));
    unread.forEach(a =>
      this.announcementSvc.markRead(a.id).subscribe({
        error: () => {
          // Revert individual item if its call fails
          this.items.update(list => list.map(x => x.id === a.id ? { ...x, isRead: false } : x));
        },
      })
    );
  }
}
