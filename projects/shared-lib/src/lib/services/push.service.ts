import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class PushService {
  constructor(private http: HttpClient) {}

  getPublicKey(): Observable<{ publicKey: string }> {
    return this.http.get<{ publicKey: string }>('/api/push/vapid-public-key');
  }

  subscribe(sub: { endpoint: string; p256dh: string; auth: string }): Observable<void> {
    return this.http.post<void>('/api/push/subscribe', sub);
  }

  unsubscribe(endpoint: string): Observable<void> {
    return this.http.delete<void>('/api/push/unsubscribe', { body: { endpoint } });
  }

  /** Request browser push permission and subscribe to service worker. Returns PushSubscription or null. */
  async requestPermission(publicKey: string): Promise<PushSubscription | null> {
    if (!('serviceWorker' in navigator) || !('PushManager' in window)) return null;
    const reg = await navigator.serviceWorker.ready;
    const existing = await reg.pushManager.getSubscription();
    if (existing) return existing;
    const sub = await reg.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: this.urlBase64ToUint8Array(publicKey),
    });
    return sub;
  }

  private urlBase64ToUint8Array(base64String: string): ArrayBuffer {
    const padding = '='.repeat((4 - base64String.length % 4) % 4);
    const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const raw = window.atob(base64);
    const arr = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) arr[i] = raw.charCodeAt(i);
    return arr.buffer as ArrayBuffer;
  }
}
