import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { NotificationBellComponent } from './notification-bell.component';
import { NotificationService } from '../../services/notification.service';
import { SignalRService } from '../../services/signalr.service';

/**
 * NotificationBellComponent — per-portal filtering.
 *
 * WHY this matters: an admin/manager who logs into the STAFF portal must NOT see
 * admin-context notifications (e.g. "pending leave approval"), because the staff
 * portal has no route to act on them. Conversely the admin portal hides personal
 * staff notifications. 'announcement' is relevant in both.
 */
describe('NotificationBellComponent — portal filtering', () => {
  const NOTIFS = [
    { id: '1', type: 'leave_pending',   title: '', message: '', isRead: false, relatedId: null, createdAt: '' }, // admin
    { id: '2', type: 'leave_approved',  title: '', message: '', isRead: false, relatedId: null, createdAt: '' }, // staff
    { id: '3', type: 'announcement',    title: '', message: '', isRead: true,  relatedId: null, createdAt: '' }, // both
    { id: '4', type: 'password_reset',  title: '', message: '', isRead: false, relatedId: null, createdAt: '' }, // admin
    { id: '5', type: 'message',         title: '', message: '', isRead: false, relatedId: null, createdAt: '' }, // staff
  ];

  function make(portal: 'admin' | 'staff'): NotificationBellComponent {
    TestBed.configureTestingModule({
      imports: [NotificationBellComponent],
      providers: [
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: NotificationService, useValue: {
            notifications: signal(NOTIFS as any),
            unreadCount:   signal(0),
            load:          () => of([]),
            push:          () => {},
        } },
        { provide: SignalRService, useValue: { notifications$: new Subject() } },
      ],
    });
    const fixture = TestBed.createComponent(NotificationBellComponent);
    fixture.componentRef.setInput('portal', portal);
    return fixture.componentInstance;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('staff portal shows only staff + announcement notifications', () => {
    const c = make('staff');
    expect(c.visible().map(n => n.id)).toEqual(['2', '3', '5']);
    expect(c.visibleUnread()).toBe(2);   // ids 2 & 5 unread; 3 is read
  });

  it('admin portal shows only admin + announcement notifications', () => {
    const c = make('admin');
    expect(c.visible().map(n => n.id)).toEqual(['1', '3', '4']);
    expect(c.visibleUnread()).toBe(2);   // ids 1 & 4 unread; 3 is read
  });

  it('hides the admin "pending approval" notification from the staff portal', () => {
    const c = make('staff');
    expect(c.visible().some(n => n.type === 'leave_pending')).toBe(false);
  });
});
