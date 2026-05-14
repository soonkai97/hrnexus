import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface MessageDto {
  id: string;
  senderId: string;
  senderName: string;
  senderIsAdmin: boolean;
  recipientId: string | null;
  recipientName: string | null;
  content: string;
  sentAt: string;
  readAt: string | null;
}

export interface SendMessageRequest {
  recipientId: string | null;
  recipientName: string | null;
  content: string;
}

@Injectable({ providedIn: 'root' })
export class MessagingService {
  constructor(private http: HttpClient) {}

  getInbox(): Observable<MessageDto[]> {
    return this.http.get<MessageDto[]>('/api/messages/inbox');
  }

  getSent(): Observable<MessageDto[]> {
    return this.http.get<MessageDto[]>('/api/messages/sent');
  }

  send(body: SendMessageRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/messages', body);
  }

  markRead(id: string): Observable<void> {
    return this.http.patch<void>(`/api/messages/${id}/read`, {});
  }
}
