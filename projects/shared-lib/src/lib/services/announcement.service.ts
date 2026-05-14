import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface AnnouncementDto {
  id: string; title: string; body: string; createdByName: string;
  createdAt: string; expiresAt: string | null; isRead: boolean;
}
export interface AdminAnnouncementDto {
  id: string; title: string; body: string; targetRoles: string;
  isActive: boolean; createdByName: string; createdAt: string;
  expiresAt: string | null; readCount: number;
}
export interface UpsertAnnouncementDto {
  title: string; body: string; targetRoles: string; expiresAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class AnnouncementService {
  constructor(private http: HttpClient) {}
  getMine(): Observable<AnnouncementDto[]> { return this.http.get<AnnouncementDto[]>('/api/announcements'); }
  markRead(id: string): Observable<void> { return this.http.post<void>(`/api/announcements/${id}/read`, {}); }
  getAll(): Observable<AdminAnnouncementDto[]> { return this.http.get<AdminAnnouncementDto[]>('/api/announcements/all'); }
  create(dto: UpsertAnnouncementDto): Observable<{ id: string }> { return this.http.post<{ id: string }>('/api/announcements', dto); }
  update(id: string, dto: UpsertAnnouncementDto): Observable<void> { return this.http.put<void>(`/api/announcements/${id}`, dto); }
  delete(id: string): Observable<void> { return this.http.delete<void>(`/api/announcements/${id}`); }
}
