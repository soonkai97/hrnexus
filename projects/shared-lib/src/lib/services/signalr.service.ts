import { Injectable, OnDestroy, signal, computed } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AppNotification } from '../models';

export type HubState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting' | 'failed';

/**
 * Custom SignalR logger that suppresses the full maintenance-page HTML that
 * gets dumped to the console when /hubs/negotiate returns 503 during a deploy.
 * All other messages are forwarded to the browser console unchanged.
 */
class FilteredLogger implements signalR.ILogger {
  log(logLevel: signalR.LogLevel, message: string): void {
    if (message.includes('<!DOCTYPE') || message.includes("Status code '503'")) {
      return; // silently drop maintenance-mode negotiation errors
    }
    switch (logLevel) {
      case signalR.LogLevel.Critical:
      case signalR.LogLevel.Error:
        console.error(message); break;
      case signalR.LogLevel.Warning:
        console.warn(message); break;
      case signalR.LogLevel.Information:
        console.info(message); break;
      default:
        console.debug(message);
    }
  }
}

@Injectable({ providedIn: 'root' })
export class SignalRService implements OnDestroy {
  private hub: signalR.HubConnection | null = null;

  readonly hubState = signal<HubState>('disconnected');
  readonly isConnected = computed(() => this.hubState() === 'connected');

  private _notifications$ = new Subject<AppNotification>();

  readonly notifications$ = this._notifications$.asObservable();

  startConnection(hubUrl = '/hubs/shift'): void {
    if (this.hub) return;

    this.hub = new signalR.HubConnectionBuilder()
      // Auth rides in the httpOnly cookie — withCredentials makes the browser send it on the
      // negotiate request and the WebSocket upgrade (same-origin), so no accessTokenFactory needed.
      .withUrl(hubUrl, { withCredentials: true })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(new FilteredLogger())
      .build();

    this.registerHandlers();
    this.connect();
  }

  stopConnection(): void {
    this.hub?.stop();
    this.hub = null;
    this.hubState.set('disconnected');
  }

  ngOnDestroy(): void {
    this.stopConnection();
    this._notifications$.complete();
  }

  private registerHandlers(): void {
    if (!this.hub) return;

    this.hub.on('Notification', (n: AppNotification) => {
      this._notifications$.next(n);
    });

    this.hub.onreconnecting(() => this.hubState.set('reconnecting'));
    this.hub.onreconnected(() => this.hubState.set('connected'));
    this.hub.onclose(() => this.hubState.set('disconnected'));
  }

  private connect(): Promise<void> {
    if (!this.hub) return Promise.resolve();
    this.hubState.set('connecting');
    return this.hub.start()
      .then(() => this.hubState.set('connected'))
      .catch((err: unknown) => {
        const msg = err instanceof Error ? err.message : String(err);
        // Redirect to the maintenance page when the hub endpoint returns 503
        // (the negotiation response body contains the maintenance HTML).
        if (msg.includes("Status code '503'") || msg.includes('<!DOCTYPE')) {
          window.location.href = '/maintenance.html';
          return;
        }
        this.hubState.set('failed');
      });
  }
}
