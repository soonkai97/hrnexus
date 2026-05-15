import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PagedResult } from './work-log.service';

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
  getAll(page = 1, pageSize = 10): Observable<PagedResult<AdminAnnouncementDto>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<AdminAnnouncementDto>>('/api/announcements/all', { params });
  }
  create(dto: UpsertAnnouncementDto): Observable<{ id: string }> { return this.http.post<{ id: string }>('/api/announcements', dto); }
  update(id: string, dto: UpsertAnnouncementDto): Observable<void> { return this.http.put<void>(`/api/announcements/${id}`, dto); }
  delete(id: string): Observable<void> { return this.http.delete<void>(`/api/announcements/${id}`); }
}
