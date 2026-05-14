import { Injectable, OnDestroy, signal, computed } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AppNotification } from '../models';

export type HubState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting' | 'failed';

@Injectable({ providedIn: 'root' })
export class SignalRService implements OnDestroy {
  private hub: signalR.HubConnection | null = null;

  readonly hubState = signal<HubState>('disconnected');
  readonly isConnected = computed(() => this.hubState() === 'connected');

  private _liveStats$ = new Subject<LiveStats>();
  private _notifications$ = new Subject<AppNotification>();

  readonly liveStats$ = this._liveStats$.asObservable();
  readonly notifications$ = this._notifications$.asObservable();

  startConnection(hubUrl = '/hubs/shift'): void {
    if (this.hub) return;

    this.hub = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => localStorage.getItem('access_token') ?? '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Information)
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
    this._liveStats$.complete();
    this._notifications$.complete();
  }

  private registerHandlers(): void {
    if (!this.hub) return;

    this.hub.on('LiveStatsUpdate', (stats: LiveStats) => {
      this._liveStats$.next(stats);
    });

    this.hub.on('Notification', (n: AppNotification) => {
      this._notifications$.next(n);
    });

    this.hub.onreconnecting(() => this.hubState.set('reconnecting'));
    this.hub.onreconnected(() => this.hubState.set('connected'));
    this.hub.onclose(() => this.hubState.set('disconnected'));
  }

  private connect(): void {
    if (!this.hub) return;
    this.hubState.set('connecting');
    this.hub.start()
      .then(() => this.hubState.set('connected'))
      .catch(() => this.hubState.set('failed'));
  }
}

export interface LiveStats {
  onShiftCount: number;
  totalStaff: number;
  tonightHours: number;
  estimatedLabourCost: number;
}
