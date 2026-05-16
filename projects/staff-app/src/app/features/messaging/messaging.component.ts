import { Component, OnInit, OnDestroy, signal, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { MessagingService, MessageDto, ToastService, SignalRService } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Textarea } from 'primeng/textarea';
import { Skeleton } from 'primeng/skeleton';
import { Tabs, TabList, Tab } from 'primeng/tabs';

@Component({
  selector: 'app-messaging',
  standalone: true,
  imports: [DatePipe, FormsModule, Button, Card, Textarea, Skeleton, Tabs, TabList, Tab],
  templateUrl: './messaging.component.html',
  styleUrl: './messaging.component.scss',
})
export class MessagingComponent implements OnInit, OnDestroy {
  private readonly msgSvc  = inject(MessagingService);
  private readonly signalR = inject(SignalRService);
  private readonly toast   = inject(ToastService);

  tab     = signal<'inbox' | 'sent'>('inbox');
  inbox   = signal<MessageDto[]>([]);
  sent    = signal<MessageDto[]>([]);
  loading = signal(false);
  sending = signal(false);
  showCompose = signal(false);
  replyContent = '';

  unread = computed(() => this.inbox().filter(m => !m.readAt).length);

  private msgSub?: Subscription;

  ngOnInit() {
    this.loadAll();
    // Reload inbox in real time whenever a new message arrives via SignalR
    this.msgSub = this.signalR.notifications$
      .pipe(filter(n => n.type === 'message'))
      .subscribe(() => this.loadAll());
  }

  ngOnDestroy() { this.msgSub?.unsubscribe(); }

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
