import { Component, OnInit, OnDestroy, signal, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { MessagingService, MessageDto, StaffStore, ToastService, SignalRService } from 'shared-lib';
import { Button } from 'primeng/button';
import { Textarea } from 'primeng/textarea';
import { Tabs, TabList, Tab } from 'primeng/tabs';
import { Card } from 'primeng/card';
import { ProgressSpinner } from 'primeng/progressspinner';
import { Select } from 'primeng/select';

@Component({
  selector: 'app-messaging',
  standalone: true,
  imports: [DatePipe, FormsModule, Button, Textarea, Card, ProgressSpinner, Select, Tabs, TabList, Tab],
  templateUrl: './messaging.component.html',
  styleUrl: './messaging.component.scss',
})
export class MessagingComponent implements OnInit, OnDestroy {
  private readonly msgSvc     = inject(MessagingService);
  private readonly staffStore = inject(StaffStore);
  private readonly signalR    = inject(SignalRService);
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

  private msgSub?: Subscription;

  ngOnInit() {
    this.staffStore.loadStaff();
    this.loadInbox();
    this.loadSent();
    // Reload inbox in real time whenever a new message arrives via SignalR
    this.msgSub = this.signalR.notifications$
      .pipe(filter(n => n.type === 'message'))
      .subscribe(() => this.loadInbox());
  }

  ngOnDestroy() { this.msgSub?.unsubscribe(); }

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

  openCompose(): void {
    this.compose = { recipientId: '', recipientName: '', content: '' };
    this.showCompose.set(true);
  }

  reply(msg: MessageDto): void {
    this.compose = { recipientId: msg.senderId, recipientName: msg.senderName, content: '' };
    this.showCompose.set(true);
  }

  send() {
    if (!this.compose.content.trim()) return;
    this.sending.set(true);
    const s = this.staff().find(s => s.id === this.compose.recipientId);
    this.msgSvc.send({
      recipientId:   this.compose.recipientId || null,
      recipientName: (s?.fullName ?? this.compose.recipientName) || null,
      content:       this.compose.content,
    }).subscribe({
      next: () => { this.sending.set(false); this.showCompose.set(false); this.compose = { recipientId: '', recipientName: '', content: '' }; this.loadSent(); this.toast.success('Message sent.'); },
      error: () => { this.toast.error('Failed to send message.'); this.sending.set(false); },
    });
  }
}
