import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { AppNotification } from '../models';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private _notifications = signal<AppNotification[]>([]);

  readonly notifications  = this._notifications.asReadonly();
  readonly unreadCount    = computed(() =>
    this._notifications().filter(n => !n.isRead).length);

  constructor(private http: HttpClient) {}

  load(): Observable<AppNotification[]> {
    return this.http.get<AppNotification[]>('/api/notifications').pipe(
      tap(list => this._notifications.set(list))
    );
  }

  /** Called by SignalR when a new notification arrives in real-time. */
  push(n: AppNotification): void {
    this._notifications.update(list => [n, ...list]);
  }

  /** Immediately update the local signal (optimistic UI). Call before markRead(). */
  markReadOptimistic(id: string): void {
    this._notifications.update(list =>
      list.map(n => n.id === id ? { ...n, isRead: true } : n));
  }

  markRead(id: string): Observable<void> {
    return this.http.post<void>(`/api/notifications/${id}/read`, {});
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>('/api/notifications/read-all', {}).pipe(
      tap(() => this._notifications.update(list =>
        list.map(n => ({ ...n, isRead: true }))))
    );
  }
}
