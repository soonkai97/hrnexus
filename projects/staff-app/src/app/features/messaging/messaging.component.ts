import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MessagingService, MessageDto, ToastService } from 'shared-lib';

@Component({
  selector: 'app-messaging',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './messaging.component.html',
  styleUrl: './messaging.component.scss',
})
export class MessagingComponent implements OnInit {
  private readonly msgSvc = inject(MessagingService);
  private readonly toast  = inject(ToastService);

  tab     = signal<'inbox' | 'sent'>('inbox');
  inbox   = signal<MessageDto[]>([]);
  sent    = signal<MessageDto[]>([]);
  loading = signal(false);
  sending = signal(false);
  showCompose = signal(false);
  replyContent = '';

  unread = computed(() => this.inbox().filter(m => !m.readAt).length);

  ngOnInit() { this.loadAll(); }

  loadAll() {
    this.loading.set(true);
    this.msgSvc.getInbox().subscribe({ next: d => { this.inbox.set(d); this.loading.set(false); }, error: () => this.loading.set(false) });
    this.msgSvc.getSent().subscribe({ next: d => this.sent.set(d) });
  }

  markRead(msg: MessageDto) {
    if (msg.readAt) return;
    this.msgSvc.markRead(msg.id).subscribe({ next: () => this.msgSvc.getInbox().subscribe(d => this.inbox.set(d)) });
  }

  send() {
    if (!this.replyContent.trim()) return;
    this.sending.set(true);
    // Staff sends to "all admins" (recipientId = null)
    this.msgSvc.send({ recipientId: null, recipientName: null, content: this.replyContent }).subscribe({
      next: () => { this.sending.set(false); this.showCompose.set(false); this.replyContent = ''; this.loadAll(); this.toast.success('Message sent to management.'); },
      error: () => { this.toast.error('Failed to send.'); this.sending.set(false); },
    });
  }
}
