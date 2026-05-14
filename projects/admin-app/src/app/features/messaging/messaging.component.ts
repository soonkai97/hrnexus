import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MessagingService, MessageDto, StaffStore, ToastService } from 'shared-lib';

@Component({
  selector: 'app-messaging',
  standalone: true,
  imports: [DatePipe, FormsModule],
  templateUrl: './messaging.component.html',
  styleUrl: './messaging.component.scss',
})
export class MessagingComponent implements OnInit {
  private readonly msgSvc     = inject(MessagingService);
  private readonly staffStore = inject(StaffStore);
  private readonly toast      = inject(ToastService);

  readonly staff = this.staffStore.staff;
  tab      = signal<'inbox' | 'sent'>('inbox');
  inbox    = signal<MessageDto[]>([]);
  sent     = signal<MessageDto[]>([]);
  loading  = signal(false);
  sending  = signal(false);
  showCompose = signal(false);

  compose = { recipientId: '', recipientName: '', content: '' };

  unread = computed(() => this.inbox().filter(m => !m.readAt).length);

  ngOnInit() {
    this.staffStore.loadStaff();
    this.loadInbox();
    this.loadSent();
  }

  loadInbox() {
    this.loading.set(true);
    this.msgSvc.getInbox().subscribe({ next: d => { this.inbox.set(d); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  loadSent() {
    this.msgSvc.getSent().subscribe({ next: d => this.sent.set(d) });
  }

  markRead(msg: MessageDto) {
    if (msg.readAt) return;
    this.msgSvc.markRead(msg.id).subscribe({ next: () => this.loadInbox() });
  }

  send() {
    if (!this.compose.content.trim()) return;
    this.sending.set(true);
    const s = this.staff().find(s => s.id === this.compose.recipientId);
    this.msgSvc.send({ recipientId: this.compose.recipientId || null, recipientName: s?.fullName ?? null, content: this.compose.content }).subscribe({
      next: () => { this.sending.set(false); this.showCompose.set(false); this.compose = { recipientId: '', recipientName: '', content: '' }; this.loadSent(); this.toast.success('Message sent.'); },
      error: () => { this.toast.error('Failed to send message.'); this.sending.set(false); },
    });
  }
}
