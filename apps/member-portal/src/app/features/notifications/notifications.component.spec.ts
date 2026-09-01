import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { NotificationsComponent } from './notifications.component';
import { Notification } from './notifications.models';

function notification(overrides: Partial<Notification> = {}): Notification {
  return {
    id: 'n1',
    title: 'Welcome to your Samaaj',
    body: 'Your membership is active.',
    channel: 'InApp',
    status: 'Sent',
    isBroadcast: false,
    createdAt: '2026-01-01T10:00:00Z',
    readAt: null,
    ...overrides,
  };
}

describe('NotificationsComponent', () => {
  let fixture: ComponentFixture<NotificationsComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [NotificationsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(NotificationsComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(rows: Notification[]): void {
    fixture.detectChanges();
    http.expectOne('/v1/notifications?limit=50').flush(rows);
    fixture.detectChanges();
  }

  it('says what the screen is for when there is nothing on it', () => {
    load([]);

    expect(text()).toContain('Nothing yet');
  });

  it('offers Mark read only on the ones this member has not read', () => {
    load([
      notification({ id: 'unread' }),
      notification({ id: 'read', readAt: '2026-01-02T10:00:00Z' }),
    ]);

    const buttons = fixture.nativeElement.querySelectorAll('button');

    // One per unread row, plus "Mark all as read".
    expect(buttons.length).toBe(2);
    expect(text()).toContain('Read');
  });

  it('hides Mark all as read once everything has been read', () => {
    load([notification({ readAt: '2026-01-02T10:00:00Z' })]);

    expect(text()).not.toContain('Mark all as read');
  });

  it('counts only the unread ones on the Mark all button', () => {
    load([
      notification({ id: 'a' }),
      notification({ id: 'b' }),
      notification({ id: 'c', readAt: '2026-01-02T10:00:00Z' }),
    ]);

    expect(text()).toContain('Mark all as read (2)');
  });

  it('says a broadcast went to everyone, so a member knows it was not about them', () => {
    load([notification({ isBroadcast: true, title: 'Paryushan schedule' })]);

    expect(text()).toContain('to everyone in your Samaaj');
  });

  it('re-reads the list after marking one read rather than guessing at the result', () => {
    load([notification({ id: 'n1' })]);

    fixture.nativeElement.querySelector('button').click();

    http.expectOne('/v1/notifications/n1/read').flush({
      notificationId: 'n1',
      readAt: '2026-01-03T10:00:00Z',
      alreadyRead: false,
    });

    // The response says when it was read; the screen takes the server's answer
    // rather than showing its own.
    http.expectOne('/v1/notifications?limit=50').flush([
      notification({ id: 'n1', readAt: '2026-01-03T10:00:00Z' }),
    ]);

    fixture.detectChanges();

    expect(text()).not.toContain('Mark all as read');
  });

  it('sends one request for Mark all as read, not one per row', () => {
    load([notification({ id: 'a' }), notification({ id: 'b' }), notification({ id: 'c' })]);

    const buttons = fixture.nativeElement.querySelectorAll('button');

    buttons[buttons.length - 1].click();

    http.expectOne('/v1/notifications/read-all').flush({ markedRead: 3 });
    http.expectOne('/v1/notifications?limit=50').flush([]);

    fixture.detectChanges();

    expect(text()).toContain('Nothing yet');
  });

  it('shows the failure rather than pretending the list is empty', () => {
    fixture.detectChanges();

    http
      .expectOne('/v1/notifications?limit=50')
      .flush({}, { status: 500, statusText: 'Server Error' });

    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });
});
